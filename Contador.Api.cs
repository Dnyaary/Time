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

// Respostas do painel (JSON) e as planilhas CSV para baixar.

partial class Contador
{
    string JsonResumo() => JsonResumo(comRemotos: true);

    string JsonResumoLocal() => JsonResumo(comRemotos: false);

    string JsonResumo(bool comRemotos)
    {
        string chave = Dia(DateTime.Now);
        long segundos, parado;
        string inicio, ultima;
        long[] porHora;
        var itens = new List<KeyValuePair<string, long>>();

        lock (trava)
        {
            porHora = HorasDoDia(chave);
            dias.TryGetValue(chave, out var hoje);
            segundos = (hoje?.Ms ?? 0) / 1000;
            parado = (hoje?.MsParado ?? 0) / 1000;
            inicio = Hm(hoje?.Primeira);
            ultima = Hm(hoje?.Ultima);

            if (apps.TryGetValue(chave, out var porItem))
                itens = porItem.OrderByDescending(x => x.Value).ToList();
        }

        var porPc = new List<object>
        {
            new
            {
                id = PC_LOCAL,
                nome = Environment.MachineName,
                segundos,
                atual = itemAgora,
                contando = ativo,
                pausado = Pausado(),
                online = true,
                este = true
            }
        };

        long total = segundos;
        var appsTotais = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in itens) appsTotais[x.Key] = x.Value / 1000;

        var remotosDados = comRemotos ? ConsultarRemotos() : new List<RemotoDados>();
        foreach (var r in remotosDados)
        {
            porPc.Add(new
            {
                id = IdDoRemoto(r.Remoto),
                nome = r.Remoto.Nome,
                segundos = r.Segundos,
                atual = r.Atual,
                contando = r.Contando,
                pausado = r.Pausado,
                online = r.Online,
                guardado = r.Guardado,
                este = false
            });

            if (!r.TemDados) continue;

            parado += r.Parado;

            total += r.Segundos;
            foreach (var par in r.Apps)
                appsTotais[par.Key] = appsTotais.GetValueOrDefault(par.Key) + par.Value;
            for (int h = 0; h < 24; h++) porHora[h] += r.Horas[h];

            inicio = MenorHora(inicio, r.Inicio);
            ultima = MaiorHora(ultima, r.Ultima);
        }

        var resposta = new
        {
            maquina = Environment.MachineName,
            usuario = Environment.UserName,
            data = chave,
            segundos = total,
            parado,
            inicio,
            ultima,
            contando = ativo,
            pausado = Pausado(),
            pausaAte = FimDaPausa(),
            atual = itemAgora,
            pcs = porPc,
            apps = appsTotais
                .OrderByDescending(x => x.Value)
                .Select(x => new { nome = x.Key, segundos = x.Value }),
            horas = porHora
        };

        return JsonSerializer.Serialize(resposta);
    }

    string JsonDias(int quantidade) => JsonDias(quantidade, comRemotos: true);

    string JsonDiasLocal(int quantidade) => JsonDias(quantidade, comRemotos: false);

    string JsonDias(int quantidade, bool comRemotos)
    {
        var segundosPorDia = new SortedDictionary<string, long>();
        var faixas = new Dictionary<string, (string Inicio, string Ultima)>();

        lock (trava)
        {
            foreach (var t in dias)
            {
                if (t.Value.Ms <= 0) continue;   // dia só com o PC parado
                segundosPorDia[t.Key] = t.Value.Ms / 1000;
                faixas[t.Key] = (Hm(t.Value.Primeira), Hm(t.Value.Ultima));
            }
        }

        if (comRemotos)
        {
            foreach (var r in ConsultarRemotos())
            {
                if (!r.TemDados) continue;

                foreach (var d in r.Dias)
                    segundosPorDia[d.Key] = segundosPorDia.GetValueOrDefault(d.Key) + d.Value;
            }
        }

        var lista = segundosPorDia
            .OrderByDescending(x => x.Key)
            .Take(quantidade)
            .Select(x => new
            {
                data = x.Key,
                segundos = x.Value,
                inicio = faixas.TryGetValue(x.Key, out var f) ? f.Inicio : "--:--",
                ultima = faixas.TryGetValue(x.Key, out var g) ? g.Ultima : "--:--"
            })
            .ToList();

        return JsonSerializer.Serialize(lista);
    }

    // /api/tudo: o que outro hub precisa para somar este PC (sem consultar os
    // remotos deste, para não somar a mesma máquina duas vezes).
    // /api/pc: a mesma coisa, mas de um PC escolhido no painel (id "este" = este).
    string JsonPc(string id, int quantidade)
    {
        if (id.Length == 0 || id == PC_LOCAL)
            return "{\"resumo\":" + JsonResumoLocal() + ",\"dias\":" + JsonDiasLocal(quantidade) + "}";

        var dados = ConsultarRemotos().FirstOrDefault(r => IdDoRemoto(r.Remoto) == id);
        if (dados == null) return "{\"erro\":\"pc\"}";
        if (!dados.TemDados) return "{\"erro\":\"offline\"}";

        var apps = dados.Apps
            .OrderByDescending(x => x.Value)
            .Select(x => new { nome = x.Key, segundos = x.Value });

        var resumo = new
        {
            maquina = dados.Remoto.Nome,
            usuario = dados.Usuario,
            data = Dia(DateTime.Now),
            segundos = dados.Segundos,
            inicio = dados.Inicio,
            ultima = dados.Ultima,
            contando = dados.Contando,
            pausado = dados.Pausado,
            pausaAte = dados.PausaAte,
            parado = dados.Parado,
            atual = dados.Atual,
            pcs = Array.Empty<object>(),
            apps,
            horas = dados.Horas
        };

        var dias = dados.Dias
            .OrderByDescending(x => x.Key)
            .Take(quantidade)
            .Select(x => new { data = x.Key, segundos = x.Value, inicio = "--:--", ultima = "--:--" })
            .ToList();

        return "{\"resumo\":" + JsonSerializer.Serialize(resumo) +
               ",\"dias\":" + JsonSerializer.Serialize(dias) + "}";
    }

    // /api/dia: um dia do histórico com todos os programas usados nele.
    // id vazio = soma deste PC com os remotos; "este" = só este; outro = aquele PC.
    string JsonDia(string data, string id)
    {
        if (!DataValida(data)) return "{\"erro\":\"data\"}";

        if (id.Length > 0 && id != PC_LOCAL)
        {
            var alvo = ConsultarRemotos().FirstOrDefault(r => IdDoRemoto(r.Remoto) == id);
            if (alvo == null) return "{\"erro\":\"pc\"}";
            if (!alvo.Online) return "{\"erro\":\"offline\"}";
            return BuscarDiaRemoto(alvo.Remoto, data) ?? "{\"erro\":\"offline\"}";
        }

        long segundos = 0, parado = 0;
        string inicio = "--:--", ultima = "--:--";
        var itens = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long[] porHora;

        lock (trava)
        {
            porHora = HorasDoDia(data);
            if (dias.TryGetValue(data, out var reg))
            {
                segundos = reg.Ms / 1000;
                parado = reg.MsParado / 1000;
                inicio = Hm(reg.Primeira);
                ultima = Hm(reg.Ultima);
            }

            if (apps.TryGetValue(data, out var porItem))
                foreach (var x in porItem)
                    itens[x.Key] = x.Value / 1000;
        }

        if (id.Length == 0)
        {
            foreach (var r in ConsultarRemotos())
            {
                // PC fora do ar: entra o total guardado daquele dia, sem a lista de programas.
                if (!r.Online)
                {
                    if (r.Guardado) segundos += r.Dias.GetValueOrDefault(data);
                    continue;
                }

                string? corpo = BuscarDiaRemoto(r.Remoto, data);
                if (corpo == null) continue;

                try
                {
                    using var doc = JsonDocument.Parse(corpo);
                    var raiz = doc.RootElement;

                    if (raiz.TryGetProperty("erro", out _)) continue;

                    segundos += raiz.GetProperty("segundos").GetInt64();
                    if (raiz.TryGetProperty("parado", out var paradoRemoto) && paradoRemoto.TryGetInt64(out long pr))
                        parado += pr;
                    inicio = MenorHora(inicio, raiz.GetProperty("inicio").GetString() ?? "--:--");
                    ultima = MaiorHora(ultima, raiz.GetProperty("ultima").GetString() ?? "--:--");

                    var horasRemoto = LerHoras(raiz);
                    for (int h = 0; h < 24; h++) porHora[h] += horasRemoto[h];

                    foreach (var a in raiz.GetProperty("apps").EnumerateArray())
                    {
                        string nome = a.GetProperty("nome").GetString() ?? "";
                        if (nome.Length == 0) continue;
                        long s = a.GetProperty("segundos").GetInt64();
                        itens[nome] = itens.GetValueOrDefault(nome) + s;
                    }
                }
                catch { }
            }
        }

        var resposta = new
        {
            data,
            segundos,
            parado,
            inicio,
            ultima,
            horas = porHora,
            apps = itens
                .OrderByDescending(x => x.Value)
                .Select(x => new { nome = x.Key, segundos = x.Value })
                .ToList()
        };

        return JsonSerializer.Serialize(resposta);
    }

    // /api/semana: "quantos" dias a partir de "inicio" (uma semana ou um mês), cada um
    // com o tempo de uso, o tempo parado e os programas. O painel monta o resumo com isso.
    // id vazio = soma deste PC com os remotos; "este" = só este; outro = aquele PC.
    string JsonSemana(string inicio, string id, int quantos)
    {
        if (!DataValida(inicio)) return "{\"erro\":\"data\"}";
        quantos = Math.Clamp(quantos, 1, 31);
        var primeiro = DateTime.ParseExact(inicio, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var datas = Enumerable.Range(0, quantos).Select(i => Dia(primeiro.AddDays(i))).ToArray();

        var segundos = datas.ToDictionary(d => d, _ => 0L);
        var parado = datas.ToDictionary(d => d, _ => 0L);
        var itens = datas.ToDictionary(d => d, _ => new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase));

        void SomarResposta(string corpo)
        {
            try
            {
                using var doc = JsonDocument.Parse(corpo);
                if (!doc.RootElement.TryGetProperty("dias", out var lista)) return;
                foreach (var d in lista.EnumerateArray())
                {
                    string data = d.GetProperty("data").GetString() ?? "";
                    if (!segundos.ContainsKey(data)) continue;
                    segundos[data] += d.GetProperty("segundos").GetInt64();
                    parado[data] += d.TryGetProperty("parado", out var p) && p.TryGetInt64(out long pp) ? pp : 0;
                    foreach (var a in d.GetProperty("apps").EnumerateArray())
                    {
                        string nome = a.GetProperty("nome").GetString() ?? "";
                        if (nome.Length == 0) continue;
                        itens[data][nome] = itens[data].GetValueOrDefault(nome) + a.GetProperty("segundos").GetInt64();
                    }
                }
            }
            catch { }
        }

        if (id.Length > 0 && id != PC_LOCAL)
        {
            Remoto? alvo;
            lock (travaRemotos) alvo = remotos.FirstOrDefault(r => IdDoRemoto(r) == id);
            if (alvo == null) return "{\"erro\":\"pc\"}";
            string? corpo = BuscarSemanaRemoto(alvo, inicio, quantos);
            if (corpo == null) return "{\"erro\":\"offline\"}";
            SomarResposta(corpo);
        }
        else
        {
            lock (trava)
            {
                foreach (string data in datas)
                {
                    if (dias.TryGetValue(data, out var reg))
                    {
                        segundos[data] = reg.Ms / 1000;
                        parado[data] = reg.MsParado / 1000;
                    }
                    if (apps.TryGetValue(data, out var porItem))
                        foreach (var x in porItem)
                            if (x.Value >= 1000) itens[data][x.Key] = x.Value / 1000;
                }
            }

            if (id.Length == 0)
            {
                foreach (var r in ConsultarRemotos())
                {
                    string? corpo = r.Online ? BuscarSemanaRemoto(r.Remoto, inicio, quantos) : null;
                    if (corpo != null)
                    {
                        SomarResposta(corpo);
                    }
                    else if (r.TemDados)
                    {
                        // Fora do ar (ou versão sem /api/semana): entra o total de cada dia, sem os programas.
                        foreach (string data in datas) segundos[data] += r.Dias.GetValueOrDefault(data);
                    }
                }
            }
        }

        var resposta = new
        {
            inicio,
            dias = datas.Select(d => new
            {
                data = d,
                segundos = segundos[d],
                parado = parado[d],
                apps = itens[d].OrderByDescending(x => x.Value).Select(x => new { nome = x.Key, segundos = x.Value })
            })
        };
        return JsonSerializer.Serialize(resposta);
    }

    static string? BuscarSemanaRemoto(Remoto remoto, string inicio, int quantos)
    {
        try
        {
            var alvo = new UriBuilder(remoto.Endereco)
            {
                Path = "/api/semana",
                Query = "inicio=" + Uri.EscapeDataString(inicio) + "&n=" + quantos + "&id=" + PC_LOCAL +
                        "&k=" + Uri.EscapeDataString(remoto.Codigo)
            };
            string corpo = http.GetStringAsync(alvo.Uri).GetAwaiter().GetResult();
            return corpo.Contains("\"erro\"") ? null : corpo;
        }
        catch { return null; }
    }

    // Pede o dia direto para outro PC (só os dados dele, sem somar os remotos dele).
    static string? BuscarDiaRemoto(Remoto remoto, string data)
    {
        try
        {
            var alvo = new UriBuilder(remoto.Endereco)
            {
                Path = "/api/dia",
                Query = "data=" + Uri.EscapeDataString(data) +
                        "&id=" + PC_LOCAL +
                        "&k=" + Uri.EscapeDataString(remoto.Codigo)
            };

            return http.GetStringAsync(alvo.Uri).GetAwaiter().GetResult();
        }
        catch { return null; }
    }

    // /api/grupos: nome do programa/site -> grupo, conforme o categorias.txt deste PC,
    // e grupo -> "produtivo" ou "distracao". O painel usa para somar o tempo de cada
    // grupo e montar a barra de foco.
    string JsonGrupos()
    {
        Dictionary<string, string> mapa, foco;
        lock (trava)
        {
            mapa = grupos;
            foco = focos.Where(x => x.Value != FOCO_NEUTRO)
                        .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        }
        return JsonSerializer.Serialize(new { mapa, foco });
    }

    // /api/app: o histórico de um programa/site (segundos por dia).
    // id vazio = soma deste PC com os remotos; "este" = só este; outro = aquele PC.
    string JsonApp(string nome, string id, int quantidade)
    {
        nome = (nome ?? "").Trim();
        if (nome.Length == 0) return "{\"erro\":\"app\"}";

        if (id.Length > 0 && id != PC_LOCAL)
        {
            Remoto? alvo;
            lock (travaRemotos) alvo = remotos.FirstOrDefault(r => IdDoRemoto(r) == id);
            if (alvo == null) return "{\"erro\":\"pc\"}";
            return BuscarAppRemoto(alvo, nome, quantidade) ?? "{\"erro\":\"offline\"}";
        }

        var porDia = new SortedDictionary<string, long>(StringComparer.Ordinal);
        string grupo;
        lock (trava)
        {
            foreach (var dia in apps)
            {
                long ms = 0;
                foreach (var item in dia.Value)
                    if (item.Key.Equals(nome, StringComparison.OrdinalIgnoreCase)) ms += item.Value;
                if (ms >= 1000) porDia[dia.Key] = ms / 1000;
            }
            grupo = grupos.GetValueOrDefault(nome) ?? "";
        }

        if (id.Length == 0)
        {
            Remoto[] lista;
            lock (travaRemotos) lista = remotos.ToArray();

            var tarefas = lista.Select(r => Task.Run(() => BuscarAppRemoto(r, nome, quantidade))).ToArray();
            Task.WaitAll(tarefas);

            foreach (var tarefa in tarefas)
            {
                string? corpo = tarefa.Result;
                if (corpo == null) continue;
                try
                {
                    using var doc = JsonDocument.Parse(corpo);
                    if (!doc.RootElement.TryGetProperty("dias", out var diasRemoto)) continue;
                    foreach (var d in diasRemoto.EnumerateArray())
                    {
                        string data = d.GetProperty("data").GetString() ?? "";
                        if (!DataValida(data)) continue;
                        porDia[data] = porDia.GetValueOrDefault(data) + d.GetProperty("segundos").GetInt64();
                    }
                }
                catch { /* PC com versão sem /api/app ou resposta estranha: fica de fora */ }
            }
        }

        var resposta = new
        {
            nome,
            grupo,
            dias = porDia
                .OrderByDescending(x => x.Key)
                .Take(quantidade)
                .Select(x => new { data = x.Key, segundos = x.Value })
                .ToList()
        };

        return JsonSerializer.Serialize(resposta);
    }

    // Pede o histórico de um programa a outro PC (só os dados dele). null = não respondeu.
    static string? BuscarAppRemoto(Remoto remoto, string nome, int quantidade)
    {
        try
        {
            var alvo = new UriBuilder(remoto.Endereco)
            {
                Path = "/api/app",
                Query = "nome=" + Uri.EscapeDataString(nome) +
                        "&n=" + quantidade +
                        "&id=" + PC_LOCAL +
                        "&k=" + Uri.EscapeDataString(remoto.Codigo)
            };

            return http.GetStringAsync(alvo.Uri).GetAwaiter().GetResult();
        }
        catch { return null; }
    }

    // /api/csv: planilha para baixar pelo painel.
    //   tipo  dias, apps ou horas (as mesmas colunas dos arquivos da pasta de dados)
    //   id    vazio = este PC e todos os outros da lista; "este" = só este; outro = aquele PC
    // Devolve null quando o tipo é inválido ou o PC pedido não respondeu.
    string? Csv(string tipo, string id)
    {
        if (tipo is not ("dias" or "apps" or "horas")) return null;

        if (id == PC_LOCAL) return TextoCsv(LinhasLocais(tipo));

        if (id.Length > 0)
        {
            Remoto? alvo;
            lock (travaRemotos) alvo = remotos.FirstOrDefault(r => IdDoRemoto(r) == id);
            if (alvo == null) return null;

            var linhasRemoto = BuscarCsvRemoto(alvo, tipo);
            return linhasRemoto == null ? null : TextoCsv(linhasRemoto);
        }

        // Todos: as linhas deste PC e, sem repetir o cabeçalho, as de cada outro PC
        // (as colunas Usuario e Maquina dizem de quem é cada linha).
        var todas = new List<string>(LinhasLocais(tipo));

        Remoto[] lista;
        lock (travaRemotos) lista = remotos.ToArray();

        var tarefas = lista.Select(r => Task.Run(() => BuscarCsvRemoto(r, tipo))).ToArray();
        Task.WaitAll(tarefas);
        foreach (var tarefa in tarefas)
        {
            string[]? linhasPc = tarefa.Result;
            if (linhasPc != null) todas.AddRange(linhasPc.Skip(1));
        }

        return TextoCsv(todas);
    }

    string[] LinhasLocais(string tipo)
    {
        lock (trava)
        {
            return tipo switch
            {
                "apps" => LinhasApps(),
                "horas" => LinhasHoras(),
                _ => LinhasDias()
            };
        }
    }

    // BOM no começo: o Excel abre o UTF-8 com os acentos certos.
    static string TextoCsv(IEnumerable<string> linhas) => "\uFEFF" + string.Join("\r\n", linhas) + "\r\n";

    // Pede a planilha a outro PC (só os dados dele). null = não respondeu.
    static string[]? BuscarCsvRemoto(Remoto remoto, string tipo)
    {
        try
        {
            var alvo = new UriBuilder(remoto.Endereco)
            {
                Path = "/api/csv",
                Query = "tipo=" + tipo +
                        "&id=" + PC_LOCAL +
                        "&k=" + Uri.EscapeDataString(remoto.Codigo)
            };

            string corpo = http.GetStringAsync(alvo.Uri).GetAwaiter().GetResult();
            return corpo.TrimStart('\uFEFF')
                .Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.Length > 0)
                .ToArray();
        }
        catch { return null; }
    }
}
