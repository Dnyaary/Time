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
// Servidor opcional: envia o resumo dos dias para um servidor próprio (pasta servidor/).

partial class Contador
{
    // ---------- Servidor (opcional) ----------
    // A cada minuto, manda para POST {servidor}/api/enviar os dias que mudaram desde o
    // último envio (hoje sempre muda). Sempre o total do dia: reenviar só substitui.
    // Sem internet, continua contando aqui e manda tudo quando a conexão voltar.

    string servidorUrl = "";
    string servidorChave = "";
    bool servidorLigado;
    bool ajustandoServidor;
    volatile string servidorStatus = "";
    volatile bool enviarJa;
    Thread? threadEnvio;

    // O que o servidor já recebeu de cada dia: (segundos de uso, segundos parado).
    readonly Dictionary<string, (long Uso, long Parado)> enviados = new();

    static readonly HttpClient httpServidor = new() { Timeout = TimeSpan.FromSeconds(20) };

    const int INTERVALO_ENVIO_S = 60;
    const int DIAS_POR_ENVIO = 31;

    bool ServidorConfigurado => servidorUrl.Length > 0 && servidorChave.Length > 0;

    void IniciarEnvio()
    {
        janela.Servidor.Checked = servidorLigado && ServidorConfigurado;
        janela.Servidor.CheckedChanged += (_, _) => AlternarServidor();
        janela.ConfigurarServidor.Click += (_, _) => ConfigurarServidor();

        threadEnvio = new Thread(LacoEnvio) { IsBackground = true, Name = "Time - envio" };
        threadEnvio.Start();
    }

    void AlternarServidor()
    {
        if (ajustandoServidor) return;
        ajustandoServidor = true;
        try
        {
            if (janela.Servidor.Checked && !ServidorConfigurado && !ConfigurarServidor())
            {
                janela.Servidor.Checked = false;
                return;
            }
            servidorLigado = janela.Servidor.Checked;
            servidorStatus = "";
            if (servidorLigado) enviarJa = true;
            SalvarConfig();
        }
        finally { ajustandoServidor = false; }
    }

    // Abre a janelinha de endereço e chave. true = salvou.
    bool ConfigurarServidor()
    {
        using var caixa = new JanelaServidor(servidorUrl, servidorChave);
        if (caixa.ShowDialog(janela) != DialogResult.OK) return false;

        string endereco = caixa.Endereco.Text.Trim().TrimEnd('/');
        string chave = caixa.Chave.Text.Trim();
        if (!Uri.TryCreate(endereco, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) || chave.Length == 0)
        {
            MessageBox.Show(janela,
                L.T("Confira o endereço (começa com https://) e a chave de envio.",
                    "Check the address (it starts with https://) and the upload key."),
                "Time", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        // Outro servidor (ou chave): manda o histórico inteiro de novo.
        lock (enviados) enviados.Clear();
        servidorUrl = endereco;
        servidorChave = chave;
        servidorLigado = true;
        servidorStatus = "";
        enviarJa = true;

        ajustandoServidor = true;
        janela.Servidor.Checked = true;
        ajustandoServidor = false;
        SalvarConfig();
        return true;
    }

    void LacoEnvio()
    {
        var proximo = DateTime.UtcNow.AddSeconds(5);   // primeiro envio logo depois de abrir
        while (rodando)
        {
            Thread.Sleep(1000);
            if (!servidorLigado || !ServidorConfigurado) continue;
            if (!enviarJa && DateTime.UtcNow < proximo) continue;
            enviarJa = false;

            int espera = EnviarPendentes();
            proximo = DateTime.UtcNow.AddSeconds(espera);
        }
    }

    // Manda o que falta, em lotes. Devolve quantos segundos esperar até a próxima vez.
    int EnviarPendentes()
    {
        // A lista do ciclo é fixada aqui: hoje muda a cada segundo e não pode prender o laço.
        var pendentes = DiasPendentes();
        for (int i = 0; i < pendentes.Count && rodando; i += DIAS_POR_ENVIO)
        {
            var lote = pendentes.Skip(i).Take(DIAS_POR_ENVIO).ToList();
            var (corpo, enviadosAgora) = MontarEnvio(lote);

            try
            {
                using var pedido = new HttpRequestMessage(HttpMethod.Post, servidorUrl + "/api/enviar")
                {
                    Content = new StringContent(corpo, Encoding.UTF8, "application/json")
                };
                pedido.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", servidorChave);
                using var resposta = httpServidor.Send(pedido);

                if (resposta.StatusCode == HttpStatusCode.Unauthorized)
                {
                    servidorStatus = L.T("O servidor recusou a chave de envio.", "The server refused the upload key.");
                    return 300;
                }
                if (!resposta.IsSuccessStatusCode)
                {
                    servidorStatus = L.T($"O servidor respondeu com erro ({(int)resposta.StatusCode}). Tenta de novo em 1 min.",
                                         $"The server returned an error ({(int)resposta.StatusCode}). Retrying in 1 min.");
                    return INTERVALO_ENVIO_S;
                }
            }
            catch
            {
                servidorStatus = L.T("Sem conexão com o servidor. Tenta de novo em 1 min.",
                                     "No connection to the server. Retrying in 1 min.");
                return INTERVALO_ENVIO_S;
            }

            lock (enviados)
                foreach (var d in enviadosAgora) enviados[d.Data] = (d.Uso, d.Parado);
            servidorStatus = L.T($"Enviado às {DateTime.Now:HH:mm} para {HostDoServidor()}",
                                 $"Sent at {DateTime.Now:HH:mm} to {HostDoServidor()}");
        }
        return INTERVALO_ENVIO_S;
    }

    string HostDoServidor() => Uri.TryCreate(servidorUrl, UriKind.Absolute, out var u) ? u.Host : servidorUrl;

    // Os dias que o servidor ainda não tem como estão agora, dos mais antigos para os
    // mais novos. Hoje vai sempre: além dos números, leva o "estou ligado" deste PC.
    List<string> DiasPendentes()
    {
        string hoje = Dia(DateTime.Now);
        var lista = new List<string>();
        lock (trava)
        {
            Dictionary<string, (long Uso, long Parado)> ja;
            lock (enviados) ja = new(enviados);

            foreach (var par in dias.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (par.Key == hoje) continue;
                long uso = par.Value.Ms / 1000, parado = par.Value.MsParado / 1000;
                if (!ja.TryGetValue(par.Key, out var antes) || antes.Uso != uso || antes.Parado != parado)
                    lista.Add(par.Key);
            }
        }
        lista.Add(hoje);
        return lista;
    }

    // Corpo do envio de um lote de dias (dia sem registro vai zerado: só o sinal de vida).
    (string Corpo, List<(string Data, long Uso, long Parado)> Dias) MontarEnvio(List<string> datas)
    {
        var marcados = new List<(string Data, long Uso, long Parado)>();
        var lista = new List<object>();
        Dictionary<string, string> mapa, foco;

        lock (trava)
        {
            foreach (string data in datas)
            {
                dias.TryGetValue(data, out var reg);
                long uso = (reg?.Ms ?? 0) / 1000, parado = (reg?.MsParado ?? 0) / 1000;

                var porItem = apps.TryGetValue(data, out var itens)
                    ? itens.Where(x => x.Value >= 1000).ToDictionary(x => x.Key, x => x.Value / 1000)
                    : new Dictionary<string, long>();

                lista.Add(new
                {
                    data,
                    segundos = uso,
                    parado,
                    inicio = Hm(reg?.Primeira),
                    ultima = Hm(reg?.Ultima),
                    apps = porItem,
                    horas = HorasDoDia(data)
                });
                marcados.Add((data, uso, parado));
            }

            mapa = new Dictionary<string, string>(grupos, StringComparer.OrdinalIgnoreCase);
            foco = focos.Where(x => x.Value != FOCO_NEUTRO).ToDictionary(x => x.Key, x => x.Value);
        }

        var corpo = new
        {
            maquina = Environment.MachineName,
            usuario = Environment.UserName,
            versao = typeof(Contador).Assembly.GetName().Version?.ToString(3) ?? "",
            agora = new { atual = itemAgora, contando = ativo, pausado = Pausado(), pausaAte = FimDaPausa() },
            grupos = new { mapa, foco },
            dias = lista
        };
        return (JsonSerializer.Serialize(corpo), marcados);
    }
}
