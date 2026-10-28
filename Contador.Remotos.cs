// Time — contador de tempo de uso real do PC
// Copyright (c) 2026 Daniel 'dnyaary' Carvalho — https://instagram.com/dnyaary
// SPDX-License-Identifier: MIT
// Distribuído sob a licença MIT. Veja o arquivo LICENSE na raiz do projeto.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

// Outros PCs: consulta pela rede, troca de IP confirmada e o que cada um respondeu.

partial class Contador
{
    // ---------- outros PCs (pcs.txt) ----------
    // Cada PC roda o próprio Time. Este aqui é o "hub": consulta os outros na
    // rede local (rota /api/tudo) e soma tudo em uma resposta só, para o celular
    // abrir um link único. O que estiver desligado aparece como offline.

    static readonly HttpClient http = new() { Timeout = TimeSpan.FromMilliseconds(1500) };

    List<RemotoDados> ConsultarRemotos()
    {
        lock (travaRemotos)
        {
            if (cacheRemotos != null && (DateTime.UtcNow - cacheRemotosEm).TotalSeconds < 8)
                return cacheRemotos;
        }

        Remoto[] lista;
        lock (travaRemotos) { lista = remotos.ToArray(); }

        // Antes de perguntar, acerta o endereço de quem mudou de IP na rede.
        bool mudou = false;
        for (int i = 0; i < lista.Length; i++)
        {
            var resolvido = ResolverEndereco(lista[i]);
            if (!ReferenceEquals(resolvido, lista[i]))
            {
                lista[i] = resolvido;
                mudou = true;
            }
        }

        if (mudou)
        {
            // Aplica sobre a lista ATUAL, não sobre a cópia tirada lá em cima:
            // enquanto os endereços eram conferidos (até ~1,5 s por PC), alguém
            // pode ter adicionado ou removido um PC pelo painel.
            var porNome = new Dictionary<string, Remoto>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in lista) porNome[r.Nome] = r;

            lock (travaRemotos)
            {
                var atualizada = remotos
                    .Select(r => porNome.TryGetValue(r.Nome, out var novo) ? novo : r)
                    .ToList();
                SalvarPcs(atualizada);
                AplicarPcs(atualizada);
                lista = atualizada.ToArray();
            }
        }

        var dados = new List<RemotoDados>();
        if (lista.Length > 0)
        {
            var tarefas = lista.Select(r => Task.Run(() => BuscarRemoto(r))).ToArray();
            Task.WaitAll(tarefas);
            dados = ComUltimosDados(tarefas.Select(t => t.Result).ToList());
        }

        lock (travaRemotos)
        {
            cacheRemotos = dados;
            cacheRemotosEm = DateTime.UtcNow;
        }
        return dados;
    }

    // Usa o aviso da rede para achar o IP atual do PC. Só troca de endereço depois
    // de um desafio respondido com o código — ninguém assume o nome de outro PC.
    Remoto ResolverEndereco(Remoto remoto)
    {
        var vizinho = Descoberta.ProcurarPorMaquina(remoto.Maquina);

        // PC cadastrado pelo link (sem o nome da máquina): aprende pelo endereço atual.
        if (vizinho == null && remoto.Maquina.Length == 0)
            vizinho = Descoberta.ProcurarPorEndereco(remoto.Endereco.Host, remoto.Endereco.Port);

        if (vizinho == null) return remoto;

        string maquina = remoto.Maquina.Length > 0 ? remoto.Maquina : vizinho.Nome;
        bool mesmoLugar = vizinho.Ip == remoto.Endereco.Host && vizinho.Porta == remoto.Endereco.Port;

        if (mesmoLugar)
            return maquina == remoto.Maquina ? remoto : remoto with { Maquina = maquina };

        if (Descoberta.Verificar(vizinho.Ip, remoto.Codigo) != Descoberta.Verificacao.Ok)
            return remoto;   // não confirmou: segue no último endereço conhecido

        return remoto with
        {
            Maquina = maquina,
            Endereco = new Uri($"http://{vizinho.Ip}:{vizinho.Porta}/")
        };
    }

    // /api/descobertos: quem está anunciando na rede agora (só aparece no painel).
    string JsonDescobertos()
    {
        Remoto[] lista;
        lock (travaRemotos) lista = remotos.ToArray();

        var vistos = Descoberta.Vizinhos()
            .GroupBy(v => v.Nome, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(v => v.Visto).First())
            .OrderBy(v => v.Nome, StringComparer.OrdinalIgnoreCase)
            .Select(v => new
            {
                nome = v.Nome,
                ip = v.Ip,
                porta = v.Porta,
                endereco = $"{v.Ip}:{v.Porta}",
                adicionado = lista.Any(r =>
                    r.Maquina.Equals(v.Nome, StringComparison.OrdinalIgnoreCase) ||
                    (r.Endereco.Host == v.Ip && r.Endereco.Port == v.Porta))
            })
            .ToList();

        return JsonSerializer.Serialize(vistos);
    }

    // Pergunta ao outro PC; devolve também a resposta crua, para guardar.
    static (RemotoDados Dados, string? Corpo) BuscarRemoto(Remoto remoto)
    {
        try
        {
            var alvo = new UriBuilder(remoto.Endereco)
            {
                Path = "/api/tudo",
                Query = "n=70&k=" + Uri.EscapeDataString(remoto.Codigo)
            };

            string corpo = http.GetStringAsync(alvo.Uri).GetAwaiter().GetResult();
            var dados = Interpretar(remoto, corpo);
            if (dados != null) return (dados, corpo);
        }
        catch { }
        return (new RemotoDados { Remoto = remoto, Online = false }, null);
    }

    // Lê a resposta de /api/tudo. null quando não dá para ler.
    static RemotoDados? Interpretar(Remoto remoto, string corpo)
    {
        try
        {
            using var documento = JsonDocument.Parse(corpo);
            var raiz = documento.RootElement;
            var resumo = raiz.GetProperty("resumo");

            var apps = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in resumo.GetProperty("apps").EnumerateArray())
                apps[a.GetProperty("nome").GetString() ?? OUTROS] = a.GetProperty("segundos").GetInt64();

            var dias = new Dictionary<string, long>();
            foreach (var d in raiz.GetProperty("dias").EnumerateArray())
                dias[d.GetProperty("data").GetString() ?? ""] = d.GetProperty("segundos").GetInt64();

            return new RemotoDados
            {
                Remoto = remoto,
                Online = true,
                Data = resumo.TryGetProperty("data", out var data) ? data.GetString() ?? "" : "",
                Parado = resumo.TryGetProperty("parado", out var parado) && parado.TryGetInt64(out long p) ? p : 0,
                Usuario = resumo.GetProperty("usuario").GetString() ?? "",
                Segundos = resumo.GetProperty("segundos").GetInt64(),
                Inicio = resumo.GetProperty("inicio").GetString() ?? "--:--",
                Ultima = resumo.GetProperty("ultima").GetString() ?? "--:--",
                Contando = resumo.GetProperty("contando").GetBoolean(),
                Pausado = resumo.TryGetProperty("pausado", out var pausado) && pausado.ValueKind == JsonValueKind.True,
                PausaAte = resumo.TryGetProperty("pausaAte", out var pausaAte) ? pausaAte.GetString() ?? "" : "",
                Atual = resumo.GetProperty("atual").GetString() ?? "",
                Apps = apps,
                Dias = dias,
                Horas = LerHoras(resumo)
            };
        }
        catch
        {
            return null;
        }
    }

    // ---------- Últimos dados de cada PC ----------
    // Quando um PC não responde (rede caiu, Wi-Fi oscilou, PC desligado), o painel usa
    // o que ele mandou da última vez. Assim o total não despenca e volta a cada queda:
    // o tempo daquele PC fica parado no último valor, com o PC marcado como offline.
    // Fica gravado em pcs_dados.json, para valer também depois de reiniciar este PC.

    readonly Dictionary<string, string> ultimosDados = new(StringComparer.OrdinalIgnoreCase);   // nome -> resposta crua
    readonly object travaUltimos = new();
    bool ultimosCarregados;
    DateTime ultimosGravadosEm;

    string ArquivoUltimos => Path.Combine(pasta, "pcs_dados.json");

    // Junta a resposta de agora com a guardada. Chamar fora dos outros locks.
    List<RemotoDados> ComUltimosDados(List<(RemotoDados Dados, string? Corpo)> respostas)
    {
        var resultado = new List<RemotoDados>();
        bool mudou = false;

        lock (travaUltimos)
        {
            if (!ultimosCarregados)
            {
                ultimosCarregados = true;
                try
                {
                    if (File.Exists(ArquivoUltimos))
                    {
                        var lidos = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ArquivoUltimos));
                        if (lidos != null)
                            foreach (var par in lidos) ultimosDados[par.Key] = par.Value;
                    }
                }
                catch { /* arquivo com problema: começa vazio */ }
            }

            foreach (var (dados, corpo) in respostas)
            {
                if (dados.Online && corpo != null)
                {
                    ultimosDados[dados.Remoto.Nome] = corpo;
                    mudou = true;
                    resultado.Add(dados);
                    continue;
                }

                var guardado = ultimosDados.TryGetValue(dados.Remoto.Nome, out string? velho)
                    ? Interpretar(dados.Remoto, velho)
                    : null;
                resultado.Add(guardado == null ? dados : DeHoje(guardado with { Online = false, Guardado = true }));
            }

            // Grava no máximo uma vez por minuto: o painel consulta a cada poucos segundos.
            if (mudou && (DateTime.UtcNow - ultimosGravadosEm).TotalSeconds >= 60)
            {
                ultimosGravadosEm = DateTime.UtcNow;
                try { File.WriteAllText(ArquivoUltimos, JsonSerializer.Serialize(ultimosDados)); } catch { }
            }
        }
        return resultado;
    }

    // Dados guardados de outro dia: o histórico vale, mas "hoje" daquele PC é zero.
    static RemotoDados DeHoje(RemotoDados d)
    {
        if (d.Data == Dia(DateTime.Now)) return d with { Contando = false, Pausado = false, Atual = "" };
        return d with
        {
            Segundos = 0, Parado = 0, Inicio = "--:--", Ultima = "--:--",
            Contando = false, Pausado = false, Atual = "",
            Apps = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Horas = new long[24]
        };
    }

    // "horas": 24 números (segundos em cada hora do dia). Sem o campo, fica tudo zero.
    static long[] LerHoras(JsonElement raiz)
    {
        var resultado = new long[24];
        if (raiz.TryGetProperty("horas", out var lista) && lista.ValueKind == JsonValueKind.Array)
        {
            int h = 0;
            foreach (var valor in lista.EnumerateArray())
            {
                if (h >= 24) break;
                if (valor.TryGetInt64(out long segundos)) resultado[h] = segundos;
                h++;
            }
        }
        return resultado;
    }

    // Segundos de uso em cada hora (0 a 23) de um dia deste PC. Chamar dentro de lock (trava).
    long[] HorasDoDia(string chave)
    {
        var resultado = new long[24];
        if (horas.TryGetValue(chave, out var ms))
            for (int h = 0; h < 24; h++) resultado[h] = ms[h] / 1000;
        return resultado;
    }

    static string MenorHora(string a, string b)
    {
        if (a == "--:--") return b;
        if (b == "--:--") return a;
        return string.CompareOrdinal(a, b) <= 0 ? a : b;
    }

    static string MaiorHora(string a, string b)
    {
        if (a == "--:--") return b;
        if (b == "--:--") return a;
        return string.CompareOrdinal(a, b) >= 0 ? a : b;
    }
}
