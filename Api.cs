// Time — contador de tempo de uso real do PC
// Copyright (c) 2026 Daniel 'dnyaary' Carvalho — https://instagram.com/dnyaary
// SPDX-License-Identifier: MIT
// Distribuído sob a licença MIT. Veja o arquivo LICENSE na raiz do projeto.

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

// Painel do celular: um servidor HTTP pequeno, embutido no Time.
//
// Usa TcpListener em vez de HttpListener porque o HttpListener depende de uma
// reserva de URL no Windows (netsh http add urlacl) e essa reserva exige
// administrador para cada porta. Um soquete comum abre sem elevação, como
// qualquer outro programa.
//
// O que é servido:
//   /                      a página do painel (não tem dados)
//   /manifest.webmanifest  manifesto para "adicionar à tela de início"
//   /icone.svg             ícone do painel
//   /api/resumo?k=codigo   dados de hoje
//   /api/dias?n=14&k=...   histórico
//   /api/csv?tipo=dias&k=  planilha CSV para baixar (dias, apps ou horas)
//   /api/app?nome=...&k=   histórico de um programa/site
//   /api/grupos?k=         programa -> grupo, conforme o categorias.txt
//
// O código é conferido em toda rota /api/. A página em si é pública (só a
// interface), então quem abrir o endereço sem o código vê a tela de acesso.

class ServidorApi : IDisposable
{
    public const int PORTA_PADRAO = 8787;

    const int LIMITE_LINHA = 2048;   // requisição maior que isso é descartada
    const string TIPO_JSON = "application/json; charset=utf-8";
    const string TIPO_TEXTO = "text/plain; charset=utf-8";

    readonly TcpListener ouvinte;
    readonly string codigo;
    readonly Func<string> resumo;
    readonly Func<int, string> dias;
    readonly Func<string, int, string> pc;
    readonly Func<string, string, string> dia;                            // data, id
    readonly Func<string> descobertos;
    readonly Func<string, string, string, string, string> adicionarPc;   // nome, link, codigo, origem
    readonly Func<string, string> removerPc;
    readonly Func<string, string, string?> csv;                          // tipo, id
    readonly Func<string, string, int, string> app;                      // nome, id, dias
    readonly Func<string> grupos;
    readonly Thread thread;
    volatile bool rodando = true;

    public ServidorApi(int porta, string codigo, Func<string> resumo, Func<int, string> dias, Func<string, int, string> pc,
                       Func<string, string, string> dia,
                       Func<string> descobertos, Func<string, string, string, string, string> adicionarPc,
                       Func<string, string> removerPc, Func<string, string, string?> csv,
                       Func<string, string, int, string> app, Func<string> grupos)
    {
        this.codigo = codigo;
        this.resumo = resumo;
        this.dias = dias;
        this.pc = pc;
        this.dia = dia;
        this.descobertos = descobertos;
        this.adicionarPc = adicionarPc;
        this.removerPc = removerPc;
        this.csv = csv;
        this.app = app;
        this.grupos = grupos;

        ouvinte = new TcpListener(IPAddress.Any, porta);
        ouvinte.Start();   // porta ocupada ou bloqueada: a exceção sobe para quem chamou

        thread = new Thread(Laco) { IsBackground = true, Name = "Time - painel" };
        thread.Start();
    }

    public void Dispose()
    {
        rodando = false;
        try { ouvinte.Stop(); } catch { }
    }

    void Laco()
    {
        while (rodando)
        {
            TcpClient cliente;
            try { cliente = ouvinte.AcceptTcpClient(); }
            catch { return; }   // soquete fechado no Dispose

            _ = Task.Run(() => Atender(cliente));
        }
    }

    void Atender(TcpClient cliente)
    {
        using (cliente)
        {
            try
            {
                cliente.ReceiveTimeout = 5000;
                cliente.SendTimeout = 5000;

                using var fluxo = cliente.GetStream();
                using var leitor = new StreamReader(fluxo, Encoding.ASCII, false, 1024, leaveOpen: true);

                string? linha = leitor.ReadLine();
                if (string.IsNullOrEmpty(linha) || linha.Length > LIMITE_LINHA) return;

                // Cabeçalhos: só interessa o Authorization. O resto é descartado.
                string? autorizacao = null;
                for (int i = 0; i < 100; i++)
                {
                    string? h = leitor.ReadLine();
                    if (string.IsNullOrEmpty(h)) break;
                    if (h.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                        autorizacao = h[14..].Trim();
                }

                string[] partes = linha.Split(' ');
                if (partes.Length < 2)
                {
                    Enviar(fluxo, 400, TIPO_TEXTO, "Requisição inválida");
                    return;
                }
                string metodo = partes[0];
                if (metodo != "GET" && metodo != "POST")
                {
                    Enviar(fluxo, 405, TIPO_TEXTO, "Somente GET e POST");
                    return;
                }

                string alvo = partes[1];
                string rota = alvo, consulta = "";
                int interrogacao = alvo.IndexOf('?');
                if (interrogacao >= 0)
                {
                    rota = alvo[..interrogacao];
                    consulta = alvo[(interrogacao + 1)..];
                }

                var parametros = LerConsulta(consulta);
                string? chave = parametros.GetValueOrDefault("k");
                string? portador = autorizacao != null && autorizacao.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? autorizacao[7..]
                    : null;
                bool liberado = CodigoConfere(chave) || CodigoConfere(portador);

                // Planilha para baixar: vai como arquivo, com nome, em vez de JSON.
                if (metodo == "GET" && rota == "/api/csv" && liberado)
                {
                    string tipo = parametros.GetValueOrDefault("tipo") ?? "";
                    string? planilha = csv(tipo, parametros.GetValueOrDefault("id") ?? "");
                    if (planilha == null)
                        Enviar(fluxo, 404, TIPO_JSON, "{\"erro\":\"csv\"}");
                    else
                        Enviar(fluxo, 200, "text/csv; charset=utf-8", planilha,
                               $"time-{tipo}-{DateTime.Now:yyyy-MM-dd}.csv");
                    return;
                }

                var resposta = Responder(rota, parametros, liberado, metodo);
                Enviar(fluxo, resposta.Status, resposta.Tipo, resposta.Corpo);
            }
            catch
            {
                // Conexão interrompida, tempo esgotado, cliente estranho: só encerra esta.
            }
        }
    }

    (int Status, string Tipo, string Corpo) Responder(string rota, Dictionary<string, string> parametros, bool liberado, string metodo)
    {
        // POST só existe para mexer na lista de PCs. Os dados vão na própria
        // consulta (nome/link/id), então não há corpo de requisição para ler.
        if (metodo == "POST")
        {
            if (!rota.StartsWith("/api/", StringComparison.Ordinal))
                return (404, TIPO_TEXTO, "Não encontrado");

            if (!liberado)
                return (401, TIPO_JSON, "{\"erro\":\"codigo\"}");

            return rota switch
            {
                "/api/pcs" => (200, TIPO_JSON, adicionarPc(
                    parametros.GetValueOrDefault("nome") ?? "",
                    parametros.GetValueOrDefault("link") ?? "",
                    parametros.GetValueOrDefault("codigo") ?? "",
                    parametros.GetValueOrDefault("origem") ?? "")),

                "/api/pcs/remover" => (200, TIPO_JSON, removerPc(
                    parametros.GetValueOrDefault("id") ?? "")),

                _ => (404, TIPO_JSON, "{\"erro\":\"rota\"}")
            };
        }

        switch (rota)
        {
            case "/":
            case "/index.html":
                return (200, "text/html; charset=utf-8", Pagina);

            case "/manifest.webmanifest":
                return (200, "application/manifest+json; charset=utf-8", Manifesto);

            case "/icone.svg":
                return (200, "image/svg+xml", Icone);
        }

        if (!rota.StartsWith("/api/", StringComparison.Ordinal))
            return (404, TIPO_TEXTO, "Não encontrado");

        if (!liberado)
            return (401, TIPO_JSON, "{\"erro\":\"codigo\"}");

        switch (rota)
        {
            case "/api/resumo":
                return (200, TIPO_JSON, resumo());

            case "/api/dias":
                int n = int.TryParse(parametros.GetValueOrDefault("n"), out int valor) ? Math.Clamp(valor, 1, 90) : 30;
                return (200, TIPO_JSON, dias(n));

            case "/api/tudo":
                int m = int.TryParse(parametros.GetValueOrDefault("n"), out int valor2) ? Math.Clamp(valor2, 1, 90) : 14;
                return (200, TIPO_JSON, pc("este", m));

            case "/api/pc":
                int q = int.TryParse(parametros.GetValueOrDefault("n"), out int valor3) ? Math.Clamp(valor3, 1, 90) : 14;
                return (200, TIPO_JSON, pc(parametros.GetValueOrDefault("id") ?? "", q));

            case "/api/dia":
                return (200, TIPO_JSON, dia(
                    parametros.GetValueOrDefault("data") ?? "",
                    parametros.GetValueOrDefault("id") ?? ""));

            case "/api/descobertos":
                return (200, TIPO_JSON, descobertos());

            case "/api/app":
                int na = int.TryParse(parametros.GetValueOrDefault("n"), out int valor4) ? Math.Clamp(valor4, 1, 90) : 70;
                return (200, TIPO_JSON, app(
                    parametros.GetValueOrDefault("nome") ?? "",
                    parametros.GetValueOrDefault("id") ?? "", na));

            case "/api/grupos":
                return (200, TIPO_JSON, grupos());

            default:
                return (404, TIPO_JSON, "{\"erro\":\"rota\"}");
        }
    }

    // Maiúsculas e minúsculas valem igual (o código é mostrado em maiúsculas, mas
    // quem digita o link à mão no celular costuma usar minúsculas). A comparação
    // leva o mesmo tempo acerte ou erre, para não dar pista do código.
    bool CodigoConfere(string? tentativa)
    {
        if (string.IsNullOrWhiteSpace(tentativa)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(tentativa.Trim().ToUpperInvariant()),
            Encoding.UTF8.GetBytes(codigo.ToUpperInvariant()));
    }

    static Dictionary<string, string> LerConsulta(string consulta)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string par in consulta.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int igual = par.IndexOf('=');
            try
            {
                if (igual < 0) d[Uri.UnescapeDataString(par)] = "";
                else d[Uri.UnescapeDataString(par[..igual])] = Uri.UnescapeDataString(par[(igual + 1)..]);
            }
            catch { /* parâmetro malformado: ignora */ }
        }
        return d;
    }

    static void Enviar(NetworkStream fluxo, int status, string tipo, string corpo, string? arquivo = null)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(corpo);
        string motivo = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            405 => "Method Not Allowed",
            _ => "Erro"
        };

        string cabecalho =
            $"HTTP/1.1 {status} {motivo}\r\n" +
            $"Content-Type: {tipo}\r\n" +
            $"Content-Length: {bytes.Length}\r\n" +
            (arquivo != null ? $"Content-Disposition: attachment; filename=\"{arquivo}\"\r\n" : "") +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n";

        byte[] cabecalhoBytes = Encoding.ASCII.GetBytes(cabecalho);
        fluxo.Write(cabecalhoBytes, 0, cabecalhoBytes.Length);
        fluxo.Write(bytes, 0, bytes.Length);
        fluxo.Flush();
    }

    // ---------- conteúdo fixo ----------

    const string Manifesto =
        """
        {
          "name": "Time",
          "short_name": "Time",
          "description": "PC usage time",
          "start_url": "/",
          "scope": "/",
          "display": "standalone",
          "background_color": "#08090b",
          "theme_color": "#08090b",
          "icons": [
            { "src": "/icone.svg", "sizes": "any", "type": "image/svg+xml", "purpose": "any" }
          ]
        }
        """;

    const string Icone =
        """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 192 192">
          <rect width="192" height="192" fill="#08090b"/>
          <circle cx="96" cy="100" r="52" fill="none" stroke="#34d399" stroke-width="10"/>
          <path d="M96 72v30l22 13" fill="none" stroke="#e9ecef" stroke-width="10"
                stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
        """;

    const string Pagina =
        """
        <!doctype html>
        <html lang="pt-BR">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
        <meta name="theme-color" content="#08090b">
        <meta name="apple-mobile-web-app-capable" content="yes">
        <meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
        <meta name="apple-mobile-web-app-title" content="Time">
        <link rel="manifest" href="/manifest.webmanifest">
        <link rel="icon" href="/icone.svg" type="image/svg+xml">
        <title>Time</title>
        <style>
        :root{
          --fundo:#08090b; --texto:#e9ecef; --suave:#8b929c; --fraco:#5a616b;
          --linha:#181b21; --linha-forte:#2a2f37;
          --acento:#34d399; --alerta:#f0b429; --ruim:#ef5350;
          --campo:#0c0e12; --trilho:#1b2028; --barra:#2c6653; --grade:#14171d; --sobre-acento:#06120d;
        }
        :root[data-tema="claro"]{
          --fundo:#f6f6f3; --texto:#16181c; --suave:#5b626b; --fraco:#8b9199;
          --linha:#e7e7e3; --linha-forte:#d2d3cf;
          --acento:#0e9466; --alerta:#c98a00; --ruim:#d33c3a;
          --campo:#ffffff; --trilho:#e2e4e0; --barra:#8fcdb2; --grade:#ecece8; --sobre-acento:#ffffff;
        }
        *{box-sizing:border-box;border-radius:0}
        html{background:var(--fundo)}
        body{margin:0 auto;max-width:560px;padding:0 20px calc(28px + env(safe-area-inset-bottom));
          background:var(--fundo);color:var(--texto);
          font:15px/1.45 -apple-system,"Segoe UI",Inter,Roboto,Helvetica,Arial,sans-serif;
          -webkit-font-smoothing:antialiased}
        header{display:flex;align-items:center;justify-content:space-between;gap:12px;
          padding:20px 0 14px;border-bottom:1px solid var(--linha-forte)}
        .marca{font-size:11px;font-weight:700;letter-spacing:.34em;text-transform:uppercase;white-space:nowrap}
        .marca b{display:inline-block;width:5px;height:5px;background:var(--acento);margin-left:9px;vertical-align:2px}
        #estado{display:flex;align-items:center;gap:7px;font-size:10px;font-weight:600;
          letter-spacing:.16em;text-transform:uppercase;color:var(--suave);text-align:right;
          min-width:0;overflow:hidden;white-space:nowrap}
        #estado::before{content:"";width:6px;height:6px;background:var(--suave);flex:0 0 auto}
        #estado.ativo::before{background:var(--acento)}
        section{padding:22px 0;border-bottom:1px solid var(--linha)}
        .topo{display:flex;align-items:baseline;justify-content:space-between;gap:12px;margin-bottom:14px}
        .rotulo{font-size:10px;font-weight:600;letter-spacing:.24em;text-transform:uppercase;color:var(--fraco)}
        .dado{font-size:10px;letter-spacing:.1em;text-transform:uppercase;color:var(--suave);font-variant-numeric:tabular-nums}
        .numero{font-size:44px;line-height:1;font-weight:600;letter-spacing:-.025em;font-variant-numeric:tabular-nums}
        .numero .u{font-size:.42em;font-weight:500;letter-spacing:.02em;color:var(--suave);margin:0 1px}
        .par{display:flex;align-items:baseline;justify-content:space-between;gap:14px;
          font-size:12px;color:var(--suave);font-variant-numeric:tabular-nums}
        ul{list-style:none;margin:0;padding:0}
        li{padding:12px 0;border-top:1px solid var(--linha)}
        li:first-child{border-top:0;padding-top:0}
        .nome{display:flex;align-items:baseline;justify-content:space-between;gap:14px}
        .nome span:first-child{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
        .nome span:last-child{font-size:13px;font-variant-numeric:tabular-nums;color:var(--texto);flex:0 0 auto}
        .pc-linha{display:flex;align-items:baseline;justify-content:space-between;gap:14px;width:100%;
          margin:0;padding:0;background:none;border:0;color:inherit;font:inherit;text-align:left;cursor:pointer}
        .pc-nome{display:flex;align-items:center;gap:8px;min-width:0}
        .pc-rotulo{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
        .pc-dot{width:6px;height:6px;background:var(--acento);flex:0 0 auto}
        .pc-dot.off{background:var(--fraco)}
        .pc-tempo{font-size:13px;font-variant-numeric:tabular-nums;flex:0 0 auto;margin-left:auto}
        .pc-seta{color:var(--fraco);font-size:14px;line-height:1;flex:0 0 auto}
        .pc-off .pc-linha{cursor:default}
        .pc-off .pc-nome,.pc-off .pc-tempo{color:var(--fraco)}
        #voltar{display:flex;align-items:baseline;justify-content:space-between;gap:12px;padding:16px 0 0}
        #voltar[hidden]{display:none}
        #voltar button{background:none;border:0;padding:0;color:var(--suave);font-size:10px;font-weight:600;
          letter-spacing:.2em;text-transform:uppercase;font-family:inherit;cursor:pointer}
        #voltarNome{font-size:10px;letter-spacing:.1em;text-transform:uppercase;color:var(--fraco);
          font-variant-numeric:tabular-nums;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
        .barra{height:2px;background:var(--trilho);margin-top:8px}
        .barra i{display:block;height:100%;background:var(--barra);width:0}
        .vazio{padding:14px 0;color:var(--fraco);font-size:13px}
        .grafico svg{display:block;width:100%;height:auto}
        .grade{stroke:var(--grade);stroke-width:1;vector-effect:non-scaling-stroke}
        .media{stroke:var(--linha-forte);stroke-width:1;stroke-dasharray:3 4;vector-effect:non-scaling-stroke}
        .area{fill:url(#degrade)}
        .linha{fill:none;stroke:url(#linhaCor);stroke-width:1.6;stroke-linejoin:round;stroke-linecap:round;vector-effect:non-scaling-stroke}
        .ponto{fill:url(#linhaCor)}
        .marca-dia{stroke:var(--linha-forte);stroke-width:1;stroke-dasharray:3 4;vector-effect:non-scaling-stroke}
        .ponto-marca{fill:var(--texto);stroke:var(--fundo);stroke-width:2}
        .dia-linha{display:flex;align-items:baseline;justify-content:space-between;gap:14px;width:100%;
          margin:0;padding:0;background:none;border:0;color:inherit;font:inherit;text-align:left;cursor:pointer}
        .dia-data{font-size:11px;letter-spacing:.1em;text-transform:uppercase;color:var(--suave);
          font-variant-numeric:tabular-nums;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
        .dia-tempo{display:flex;align-items:baseline;gap:9px;font-size:13px;font-variant-numeric:tabular-nums;flex:0 0 auto}
        .dia-seta{color:var(--fraco);font-size:14px;line-height:1}
        .eixo{display:flex;justify-content:space-between;margin-top:10px;color:var(--fraco);
          font-size:10px;letter-spacing:.1em;font-variant-numeric:tabular-nums}
        details#gerenciar{margin-top:0}
        summary{list-style:none;cursor:pointer;padding:16px 0;display:flex;align-items:center;
          justify-content:space-between;gap:12px;font-size:10px;font-weight:600;
          letter-spacing:.24em;text-transform:uppercase;color:var(--fraco)}
        summary::-webkit-details-marker{display:none}
        summary::after{content:"+";font-size:15px;letter-spacing:0;line-height:1;color:var(--suave)}
        details[open] summary::after{content:"\2212"}
        #pcsEditar li{padding:12px 0}
        .pc-remover{background:none;border:0;color:var(--fraco);font-size:16px;line-height:1;
          font-family:inherit;cursor:pointer;padding:0 0 0 12px}
        .form-pc{display:grid;gap:8px;padding:2px 0 0}
        .form-pc input{width:100%;padding:11px 12px;background:var(--campo);border:1px solid var(--linha-forte);
          color:var(--texto);font-size:13px;letter-spacing:.02em;font-family:inherit}
        .form-pc input::placeholder{color:var(--fraco)}
        .form-pc input:focus{outline:0;border-color:var(--acento)}
        .form-pc button{margin-top:2px;padding:12px;border:0;background:var(--acento);color:var(--sobre-acento);
          font-size:10px;font-weight:700;letter-spacing:.22em;text-transform:uppercase;
          font-family:inherit;cursor:pointer}
        .aviso{margin:10px 0 0;font-size:12px;color:var(--ruim)}
        .aviso:empty{display:none}
        .aviso.ok{color:var(--acento)}
        .rotulo-lista{margin:18px 0 2px}
        .rotulo-lista:empty{display:none}
        #redeBloco[hidden]{display:none}
        #pcsRede li{padding:12px 0}
        .rede-topo{display:flex;align-items:baseline;justify-content:space-between;gap:12px;font-size:13px}
        .rede-nome{flex:1;min-width:0;background:none;border:0;padding:0;color:var(--texto);
          font-size:13px;font-family:inherit}
        .rede-nome:focus{outline:0;border-bottom:1px solid var(--acento)}
        .rede-ip{font-size:10px;letter-spacing:.08em;color:var(--fraco);font-variant-numeric:tabular-nums;
          flex:0 0 auto}
        .rede-acao{display:flex;gap:8px;margin-top:9px}
        .rede-acao input{flex:1;min-width:0;padding:9px 10px;background:var(--campo);border:1px solid var(--linha-forte);
          color:var(--texto);font-size:12px;letter-spacing:.14em;text-transform:uppercase;font-family:inherit}
        .rede-acao input::placeholder{color:var(--fraco);letter-spacing:.02em;text-transform:none}
        .rede-acao input:focus{outline:0;border-color:var(--acento)}
        .rede-acao button{padding:9px 12px;border:1px solid var(--acento);background:none;color:var(--acento);
          font-size:10px;font-weight:700;letter-spacing:.16em;text-transform:uppercase;
          font-family:inherit;cursor:pointer;flex:0 0 auto}
        .rede-acao button:disabled{opacity:.45;cursor:default}
        #rede{flex:0 0 auto;text-align:center}
        footer span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
        footer{padding:20px 0 0;display:flex;justify-content:space-between;gap:12px;
          color:var(--fraco);font-size:10px;letter-spacing:.12em;text-transform:uppercase}
        #codigo{display:none}
        #codigo p{margin:0 0 16px;color:var(--suave);font-size:13px;max-width:38ch}
        #codigo input{width:100%;padding:14px 12px;background:var(--campo);border:1px solid var(--linha-forte);
          color:var(--texto);font-size:16px;letter-spacing:.22em;text-transform:uppercase;font-family:inherit}
        #codigo input:focus{outline:0;border-color:var(--acento)}
        #codigo button{width:100%;margin-top:12px;padding:14px;border:0;background:var(--acento);
          color:var(--sobre-acento);font-size:11px;font-weight:700;letter-spacing:.22em;text-transform:uppercase;
          font-family:inherit;cursor:pointer}
        .periodos{display:grid;grid-template-columns:1fr 1fr;gap:18px}
        .periodo .rotulo{display:block;margin-bottom:8px}
        .periodo .valor{font-size:26px;line-height:1.1;font-weight:600;letter-spacing:-.02em;font-variant-numeric:tabular-nums}
        .periodo .valor .u{font-size:.5em;font-weight:500;color:var(--suave);margin:0 1px}
        .periodo .sub{margin-top:6px;font-size:11px;color:var(--suave);font-variant-numeric:tabular-nums}
        .horas svg{display:block;width:100%;height:auto}
        .hbar{fill:var(--barra)}
        .hbar.pico{fill:var(--acento)}
        .hbase{stroke:var(--trilho);stroke-width:1;vector-effect:non-scaling-stroke}
        .baixar{display:flex;gap:8px}
        .baixar a{flex:1;min-width:0;padding:11px 6px;border:1px solid var(--linha-forte);color:var(--texto);
          text-align:center;text-decoration:none;font-size:10px;font-weight:700;letter-spacing:.16em;text-transform:uppercase}
        .nota{margin:10px 0 0;font-size:11px;color:var(--fraco)}
        .cab-dir{display:flex;align-items:center;gap:14px;min-width:0}
        #tema{background:none;border:0;padding:0;color:var(--suave);font-size:14px;line-height:1;cursor:pointer;flex:0 0 auto}
        .app-linha{display:block;width:100%;margin:0;padding:0;background:none;border:0;color:inherit;
          font:inherit;text-align:left;cursor:pointer}
        #appCab[hidden]{display:none}
        .app-nome{font-size:22px;line-height:1.2;font-weight:600;letter-spacing:-.01em}
        #appGrupo{margin:4px 0 18px}
        .comp{margin-top:4px;font-size:11px;color:var(--suave);font-variant-numeric:tabular-nums}
        .comp:empty{display:none}
        button{-webkit-tap-highlight-color:transparent}
        /* Destaque ao passar o mouse só onde existe mouse: no celular ele "grudava" após o toque. */
        @media (hover:hover){
          #voltar button:hover{color:var(--texto)}
          .pc-remover:hover{color:var(--ruim)}
          .rede-acao button:hover{background:var(--acento);color:var(--sobre-acento)}
          .baixar a:hover{border-color:var(--acento);color:var(--acento)}
          #tema:hover{color:var(--texto)}
        }
        </style>
        </head>
        <body>
        <header>
          <div class="marca">Time<b></b></div>
          <div class="cab-dir"><div id="estado"></div><button id="tema" type="button"></button></div>
        </header>

        <section id="codigo">
          <div class="topo"><span class="rotulo" data-t="acesso">Acesso</span></div>
          <p data-t="digiteCodigo">Digite o código que aparece na janela do Time.</p>
          <input id="entrada" placeholder="ABCD-EFGH" autocapitalize="characters"
                 autocomplete="off" spellcheck="false" inputmode="text">
          <button id="entrar" data-t="entrar">Entrar</button>
        </section>

        <main id="painel" hidden>
          <div id="voltar" hidden>
            <button id="btnVoltar" type="button"></button>
            <span id="voltarNome"></span>
          </div>

          <section>
            <div id="appCab" hidden>
              <div class="app-nome" id="appNome"></div>
              <div class="dado" id="appGrupo"></div>
            </div>
            <div class="topo"><span class="rotulo" id="rotuloHoje"></span><span class="dado" id="data">—</span></div>
            <div class="numero" id="hoje">--</div>
            <div class="par"><span id="periodo">—</span><span id="resumoPcs"></span></div>
          </section>

          <section id="secaoPcs" hidden>
            <div class="topo"><span class="rotulo" data-t="porPc">Por PC</span><span class="dado" id="pcsOnline"></span></div>
            <ul id="pcs"></ul>
          </section>

          <section id="secaoGrupos" hidden>
            <div class="topo"><span class="rotulo" data-t="porGrupo">Por grupo</span><span class="dado" id="totalGrupos"></span></div>
            <ul id="grupos"></ul>
          </section>

          <section id="secaoApps">
            <div class="topo"><span class="rotulo" data-t="porPrograma">Por programa</span><span class="dado" id="totalItens"></span></div>
            <ul id="apps"></ul>
          </section>

          <section id="secaoHoras">
            <div class="topo"><span class="rotulo" data-t="porHora">Por hora</span><span class="dado" id="pico"></span></div>
            <div class="horas" id="horas"></div>
            <div class="eixo" id="eixoHoras"></div>
          </section>

          <section>
            <div class="topo"><span class="rotulo" data-t="ultimos14">Últimos 14 dias</span><span class="dado" id="media">—</span></div>
            <div class="grafico" id="grafico"></div>
            <div class="eixo" id="eixo"></div>
          </section>

          <section id="secaoPeriodos">
            <div class="periodos">
              <div class="periodo">
                <span class="rotulo" data-t="semana">Esta semana</span>
                <div class="valor" id="semanaTotal">--</div>
                <div class="sub" id="semanaSub"></div>
                <div class="comp" id="semanaComp"></div>
              </div>
              <div class="periodo">
                <span class="rotulo" data-t="mes">Este mês</span>
                <div class="valor" id="mesTotal">--</div>
                <div class="sub" id="mesSub"></div>
                <div class="comp" id="mesComp"></div>
              </div>
            </div>
          </section>

          <section id="secaoDias" hidden>
            <div class="topo"><span class="rotulo" data-t="diasAnteriores">Dias anteriores</span><span class="dado" id="diasConta"></span></div>
            <ul id="dias"></ul>
          </section>

          <section id="secaoBaixar">
            <div class="topo"><span class="rotulo" data-t="relatorio">Relatório</span></div>
            <div class="baixar">
              <a id="csvDias" href="#" data-t="baixarDias">Dias</a>
              <a id="csvApps" href="#" data-t="baixarApps">Programas</a>
              <a id="csvHoras" href="#" data-t="baixarHoras">Horas</a>
            </div>
            <p class="nota" id="notaBaixar"></p>
          </section>

          <details id="gerenciar">
            <summary data-t="gerenciar">Adicionar ou remover PCs</summary>

            <div id="redeBloco" hidden>
              <p class="rotulo rotulo-lista" data-t="naRede">Na rede agora</p>
              <ul id="pcsRede"></ul>
            </div>

            <p class="rotulo rotulo-lista" id="listaTitulo"></p>
            <ul id="pcsEditar"></ul>

            <p class="rotulo rotulo-lista" id="linkTitulo"></p>
            <form id="formPc" class="form-pc" autocomplete="off">
              <input id="pcNome" data-tp="nomeEx" placeholder="Nome (ex.: Notebook)" maxlength="40">
              <input id="pcLink" data-tp="linkOutro" placeholder="Link do outro PC, com ?k=" maxlength="200"
                     spellcheck="false" inputmode="url">
              <button type="submit" data-t="adicionar">Adicionar</button>
            </form>
            <p id="pcAviso" class="aviso"></p>
          </details>

          <footer><span id="rodape">—</span><span id="rede"></span><span id="hora">—</span></footer>
        </main>

        <script>
        // Idioma: português quando o navegador está em português; inglês nos demais.
        var PT = /^pt/i.test(navigator.language || '');
        var LOCAL = PT ? 'pt-BR' : 'en-US';

        var TEXTOS = {
          pt: {
            acesso: 'Acesso', digiteCodigo: 'Digite o código que aparece na janela do Time.', entrar: 'Entrar',
            todosPcs: '← Todos os PCs', voltar: '← Voltar', hoje: 'Hoje', dia: 'Dia',
            porPc: 'Por PC', porPrograma: 'Por programa', porHora: 'Por hora', ultimos14: 'Últimos 14 dias',
            semana: 'Esta semana', mes: 'Este mês', diasAnteriores: 'Dias anteriores',
            relatorio: 'Relatório', baixarDias: 'Dias', baixarApps: 'Programas', baixarHoras: 'Horas',
            notaTodos: 'Planilhas CSV com o histórico de todos os PCs, para abrir no Excel.',
            notaUm: 'Planilhas CSV com o histórico deste PC, para abrir no Excel.',
            gerenciar: 'Adicionar ou remover PCs', naRede: 'Na rede agora',
            nomeEx: 'Nome (ex.: Notebook)', linkOutro: 'Link do outro PC, com ?k=', adicionar: 'Adicionar',
            verUsando: 'Ver o que {0} está usando', este: ' · este', offline: 'offline', nPcs: '{0} PCs',
            online: '{0} de {1} online', naLista: 'Na lista', ouCole: 'Ou cole o link do outro PC',
            codOutro: 'código do outro PC', codDe: 'Código de {0}', nomePc: 'Nome do PC', remover: 'Remover {0}',
            nNaRede: '{0} na rede', nenhumNaRede: 'nenhum na rede',
            digiteCodDe: 'Digite o código que aparece na janela do {0}.', adicionado: '{0} adicionado.',
            pcAdicionado: 'PC adicionado.', pcRemovido: 'PC removido.', coleLink: 'Cole o link do outro PC.',
            semConexaoTime: 'Não foi possível falar com o Time.', falha: 'Falha {0}.',
            top: 'top {0} de {1}', item1: '1 item', itens: '{0} itens', semDados: 'Sem dados ainda',
            media: 'média {0}', mediaDia: 'média {0} por dia', semDadosCurto: 'sem dados',
            graficoAria: 'Tempo de uso por dia', horasAria: 'Tempo de uso por hora', verDia: 'Ver o dia {0}',
            dia1: '1 dia', diasN: '{0} dias', diaUso1: '1 dia com uso', diasUso: '{0} dias com uso',
            periodo: 'Início {0}  ·  Última {1}', semAtividade: 'Sem atividade', semAtividadeHoje: 'Sem atividade hoje',
            historico: 'Histórico', contando: 'Contando · {0}', pausado: 'Pausado',
            codigoInvalido: 'código inválido', semConexao: 'sem conexão',
            pico: 'pico {0}', semHoras: 'Sem dados por hora ainda',
            porGrupo: 'Por grupo', semGrupo: 'Sem grupo', grupo1: '1 grupo', gruposN: '{0} grupos',
            verApp: 'Ver o histórico de {0}', usadoDias: 'Usado em {0} dos últimos 14 dias',
            semUso14: 'Sem uso nos últimos 14 dias',
            vsSemana: '{0} que a semana passada', vsMes: '{0} que o mês passado',
            temaClaro: 'Tema claro', temaEscuro: 'Tema escuro',
            e_codigo: 'Código de acesso inválido.',
            e_nome_reservado: 'Esse nome é reservado. Escolha outro.',
            e_endereco_invalido: 'Endereço do PC inválido.',
            e_codigo_vazio: 'Digite o código que aparece na janela do outro PC.',
            e_saiu_da_rede: 'Esse PC saiu da rede. Tente de novo em alguns segundos.',
            e_sem_resposta: 'Não consegui confirmar com esse PC. Veja se o painel dele está ligado e se o Windows liberou o Time no firewall.',
            e_codigo_errado: 'O código não confere com esse PC.',
            e_link_invalido: 'Link inválido. Cole o link que aparece na janela do outro PC.',
            e_link_sem_codigo: 'O link precisa terminar com ?k=CODIGO.',
            e_este_pc: 'Esse é este PC. Use o link do outro computador.',
            e_limite: 'Limite de 20 PCs.',
            e_nome_repetido: 'Já existe um PC com esse nome.',
            e_ja_na_lista: 'Esse PC já está na lista.',
            e_nao_encontrado: 'PC não encontrado.'
          },
          en: {
            acesso: 'Access', digiteCodigo: 'Type the code shown in the Time window.', entrar: 'Enter',
            todosPcs: '← All PCs', voltar: '← Back', hoje: 'Today', dia: 'Day',
            porPc: 'Per PC', porPrograma: 'Per app', porHora: 'By hour', ultimos14: 'Last 14 days',
            semana: 'This week', mes: 'This month', diasAnteriores: 'Previous days',
            relatorio: 'Report', baixarDias: 'Days', baixarApps: 'Apps', baixarHoras: 'Hours',
            notaTodos: 'CSV spreadsheets with the history of every PC, to open in Excel.',
            notaUm: 'CSV spreadsheets with this PC\'s history, to open in Excel.',
            gerenciar: 'Add or remove PCs', naRede: 'On the network now',
            nomeEx: 'Name (e.g. Laptop)', linkOutro: 'Other PC\'s link, with ?k=', adicionar: 'Add',
            verUsando: 'See what {0} is using', este: ' · this PC', offline: 'offline', nPcs: '{0} PCs',
            online: '{0} of {1} online', naLista: 'In the list', ouCole: 'Or paste the other PC\'s link',
            codOutro: 'other PC\'s code', codDe: 'Code for {0}', nomePc: 'PC name', remover: 'Remove {0}',
            nNaRede: '{0} on the network', nenhumNaRede: 'none on the network',
            digiteCodDe: 'Type the code shown in the Time window on {0}.', adicionado: '{0} added.',
            pcAdicionado: 'PC added.', pcRemovido: 'PC removed.', coleLink: 'Paste the other PC\'s link.',
            semConexaoTime: 'Could not reach Time.', falha: 'Error {0}.',
            top: 'top {0} of {1}', item1: '1 item', itens: '{0} items', semDados: 'No data yet',
            media: 'average {0}', mediaDia: 'average {0} per day', semDadosCurto: 'no data',
            graficoAria: 'Usage time per day', horasAria: 'Usage time per hour', verDia: 'See {0}',
            dia1: '1 day', diasN: '{0} days', diaUso1: '1 day with use', diasUso: '{0} days with use',
            periodo: 'Start {0}  ·  Last {1}', semAtividade: 'No activity', semAtividadeHoje: 'No activity today',
            historico: 'History', contando: 'Counting · {0}', pausado: 'Paused',
            codigoInvalido: 'invalid code', semConexao: 'offline',
            pico: 'peak {0}', semHoras: 'No hourly data yet',
            porGrupo: 'By group', semGrupo: 'No group', grupo1: '1 group', gruposN: '{0} groups',
            verApp: 'See the history of {0}', usadoDias: 'Used on {0} of the last 14 days',
            semUso14: 'Not used in the last 14 days',
            vsSemana: '{0} vs last week', vsMes: '{0} vs last month',
            temaClaro: 'Light theme', temaEscuro: 'Dark theme',
            e_codigo: 'Invalid access code.',
            e_nome_reservado: 'That name is reserved. Choose another one.',
            e_endereco_invalido: 'Invalid PC address.',
            e_codigo_vazio: 'Type the code shown in the other PC\'s Time window.',
            e_saiu_da_rede: 'That PC left the network. Try again in a few seconds.',
            e_sem_resposta: 'Could not confirm with that PC. Check that its dashboard is on and that Windows allowed Time through the firewall.',
            e_codigo_errado: 'The code doesn\'t match that PC.',
            e_link_invalido: 'Invalid link. Paste the link shown in the other PC\'s Time window.',
            e_link_sem_codigo: 'The link must end with ?k=CODE.',
            e_este_pc: 'That is this PC. Use the other computer\'s link.',
            e_limite: 'Limit of 20 PCs.',
            e_nome_repetido: 'There is already a PC with that name.',
            e_ja_na_lista: 'That PC is already in the list.',
            e_nao_encontrado: 'PC not found.'
          }
        };
        var TX = PT ? TEXTOS.pt : TEXTOS.en;

        // t('chave', a, b): texto no idioma da página, com {0}, {1} trocados pelos valores.
        function t(chave) {
          var texto = TX[chave] !== undefined ? TX[chave] : chave;
          for (var i = 1; i < arguments.length; i++) texto = texto.split('{' + (i - 1) + '}').join(arguments[i]);
          return texto;
        }

        document.documentElement.lang = LOCAL;
        document.querySelectorAll('[data-t]').forEach(function (el) { el.textContent = t(el.getAttribute('data-t')); });
        document.querySelectorAll('[data-tp]').forEach(function (el) { el.placeholder = t(el.getAttribute('data-tp')); });

        // Tema: segue o sistema até a pessoa escolher no botão do topo (fica guardado neste aparelho).
        var MIDIA_CLARA = window.matchMedia ? window.matchMedia('(prefers-color-scheme: light)') : null;

        function temaSalvo() {
          try { return localStorage.getItem('time_tema'); } catch (e) { return null; }
        }

        function aplicarTema() {
          var salvo = temaSalvo();
          var claro = salvo ? salvo === 'claro' : !!(MIDIA_CLARA && MIDIA_CLARA.matches);
          document.documentElement.setAttribute('data-tema', claro ? 'claro' : 'escuro');
          var meta = document.querySelector('meta[name="theme-color"]');
          if (meta) meta.setAttribute('content', claro ? '#f6f6f3' : '#08090b');
          var botao = document.getElementById('tema');
          botao.textContent = claro ? '\u263e' : '\u2600';   // lua no claro, sol no escuro
          botao.title = claro ? t('temaEscuro') : t('temaClaro');
          botao.setAttribute('aria-label', botao.title);
        }

        document.getElementById('tema').addEventListener('click', function () {
          var claro = document.documentElement.getAttribute('data-tema') === 'claro';
          try { localStorage.setItem('time_tema', claro ? 'escuro' : 'claro'); } catch (e) { /* sem armazenamento */ }
          aplicarTema();
        });
        if (MIDIA_CLARA && MIDIA_CLARA.addEventListener) MIDIA_CLARA.addEventListener('change', aplicarTema);
        aplicarTema();

        var caixa = document.getElementById('codigo');
        var painel = document.getElementById('painel');
        var estado = document.getElementById('estado');
        var codigo = new URLSearchParams(location.search).get('k') ||
                     localStorage.getItem('time_codigo') || '';

        // null = visão de todos os PCs; id = detalhe de um PC só.
        var pcAtual = new URLSearchParams(location.search).get('pc') || null;

        // null = hoje; "AAAA-MM-DD" = um dia do histórico aberto.
        var diaAtual = new URLSearchParams(location.search).get('dia') || null;

        // null = visão normal; nome = detalhe de um programa/site.
        var appAtual = new URLSearchParams(location.search).get('app') || null;

        // Programa -> grupo, conforme o categorias.txt do PC principal.
        var mapaGrupos = {};

        // PCs que estão anunciando na rede agora (a lista só aparece aqui no painel).
        var descobertos = [];
        var totalPcs = 0;

        // Dias pedidos ao PC: cobre este mês e o anterior (para comparar), a semana e o gráfico.
        var DIAS_HISTORICO = 70;

        function abrirPc(id) {
          pcAtual = id;
          diaAtual = null;
          appAtual = null;
          trocarUrl(id);
          atualizar();
        }

        function abrirDia(data) {
          diaAtual = data;
          appAtual = null;
          trocarUrl(pcAtual);
          atualizar();
        }

        function abrirApp(nome) {
          appAtual = nome;
          trocarUrl(pcAtual);
          window.scrollTo(0, 0);
          atualizar();
        }

        function voltarTodos() {
          pcAtual = null;
          diaAtual = null;
          appAtual = null;
          trocarUrl(null);
          atualizar();
        }

        // Deixa o endereço com ?pc=... e ?dia=... para recarregar já na mesma tela.
        function trocarUrl(id) {
          try {
            var endereco = new URL(location.href);
            if (id) endereco.searchParams.set('pc', id);
            else endereco.searchParams.delete('pc');
            if (diaAtual) endereco.searchParams.set('dia', diaAtual);
            else endereco.searchParams.delete('dia');
            if (appAtual) endereco.searchParams.set('app', appAtual);
            else endereco.searchParams.delete('app');
            history.replaceState(null, '', endereco.toString());
          } catch (e) { /* navegador antigo: segue sem mudar a URL */ }
        }

        function seg(s) {
          s = Math.max(0, Math.round(s || 0));
          var h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60);
          if (h > 0) return h + 'h ' + String(m).padStart(2, '0') + 'min';
          if (m > 0) return m + 'min';
          return s + 's';
        }

        // Mesmo formato de seg(), com as unidades menores para o número grande.
        function numero(s) {
          s = Math.max(0, Math.round(s || 0));
          var h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60);
          if (h > 0) return h + '<span class="u">h</span> ' + String(m).padStart(2, '0') + '<span class="u">min</span>';
          if (m > 0) return m + '<span class="u">min</span>';
          return s + '<span class="u">s</span>';
        }

        function iso(d) {
          return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' +
                 String(d.getDate()).padStart(2, '0');
        }

        function hojeIso() { return iso(new Date()); }

        // 14 -> "14h" (português) ou "2 PM" (inglês).
        function rotuloHora(h) {
          if (PT) return h + 'h';
          return (h % 12 || 12) + (h < 12 ? ' AM' : ' PM');
        }

        function salvarCodigo() {
          var valor = document.getElementById('entrada').value.trim().toUpperCase();
          if (!valor) return;
          codigo = valor;
          localStorage.setItem('time_codigo', valor);
          caixa.style.display = 'none';
          atualizar();
        }

        async function buscar(rota) {
          var url = rota + (rota.indexOf('?') >= 0 ? '&' : '?') + 'k=' + encodeURIComponent(codigo);
          var r = await fetch(url, { cache: 'no-store' });
          if (r.status === 401) throw new Error('codigo');
          if (!r.ok) throw new Error('falha ' + r.status);
          return r.json();
        }

        function desenharPcs(pcs) {
          pcs = pcs || [];
          totalPcs = pcs.length;
          var secao = document.getElementById('secaoPcs');
          var lista = document.getElementById('pcs');
          var online = pcs.filter(function (p) { return p.online; }).length;

          document.getElementById('resumoPcs').textContent = pcs.length > 1 ? t('nPcs', pcs.length) : '';
          secao.hidden = pcs.length < 2;

          if (pcs.length > 1) {
            document.getElementById('pcsOnline').textContent = t('online', online, pcs.length);
            lista.innerHTML = '';

            pcs.forEach(function (p) {
              var li = document.createElement('li');
              if (!p.online) li.className = 'pc-off';

              var linha = document.createElement(p.online ? 'button' : 'div');
              linha.className = 'pc-linha';
              if (p.online) {
                linha.type = 'button';
                linha.title = t('verUsando', p.nome);
                linha.addEventListener('click', function () { abrirPc(p.id); });
              }

              var esq = document.createElement('span');
              esq.className = 'pc-nome';
              var ponto = document.createElement('i');
              ponto.className = 'pc-dot' + (p.online ? '' : ' off');
              var rotulo = document.createElement('span');
              rotulo.className = 'pc-rotulo';
              rotulo.textContent = p.nome + (p.este ? t('este') : '');
              esq.appendChild(ponto);
              esq.appendChild(rotulo);

              var dir = document.createElement('span');
              dir.className = 'pc-tempo';
              dir.textContent = p.online ? seg(p.segundos) : t('offline');

              linha.appendChild(esq);
              linha.appendChild(dir);

              if (p.online) {
                var seta = document.createElement('i');
                seta.className = 'pc-seta';
                seta.textContent = '›';
                linha.appendChild(seta);
              }

              li.appendChild(linha);
              lista.appendChild(li);
            });
          }

          desenharEditar(pcs);
        }

        // Lista de baixo (dentro do "Adicionar ou remover"): só os outros PCs.
        function desenharEditar(pcs) {
          var lista = document.getElementById('pcsEditar');
          lista.innerHTML = '';

          var outros = pcs.filter(function (p) { return !p.este; });
          document.getElementById('listaTitulo').textContent = outros.length ? t('naLista') : '';

          outros.forEach(function (p) {
            var li = document.createElement('li');
            var topo = document.createElement('div');
            topo.className = 'nome';

            var nome = document.createElement('span');
            nome.textContent = p.nome;

            var botao = document.createElement('button');
            botao.type = 'button';
            botao.className = 'pc-remover';
            botao.textContent = '×';
            botao.title = t('remover', p.nome);
            botao.setAttribute('aria-label', t('remover', p.nome));
            botao.addEventListener('click', function () { removerPc(p.id); });

            topo.appendChild(nome);
            topo.appendChild(botao);
            li.appendChild(topo);
            lista.appendChild(li);
          });
        }

        // PCs que apareceram na rede e ainda não estão na lista (só no painel).
        function desenharRede(lista) {
          descobertos = lista || [];
          var ul = document.getElementById('pcsRede');
          var bloco = document.getElementById('redeBloco');
          var novos = descobertos.filter(function (d) { return !d.adicionado; });

          ul.innerHTML = '';
          bloco.hidden = novos.length === 0;
          document.getElementById('linkTitulo').textContent = novos.length ? t('ouCole') : '';

          novos.forEach(function (d) {
            var li = document.createElement('li');

            var topo = document.createElement('div');
            topo.className = 'rede-topo';

            // O nome já vem preenchido com o da máquina, mas pode ser trocado
            // por um rótulo seu antes de adicionar.
            var nome = document.createElement('input');
            nome.className = 'rede-nome';
            nome.value = d.nome;
            nome.maxLength = 40;
            nome.autocomplete = 'off';
            nome.spellcheck = false;
            nome.setAttribute('aria-label', t('nomePc'));

            var ip = document.createElement('span');
            ip.className = 'rede-ip';
            ip.textContent = d.ip;
            topo.appendChild(nome);
            topo.appendChild(ip);

            var acao = document.createElement('div');
            acao.className = 'rede-acao';
            var campo = document.createElement('input');
            campo.placeholder = t('codOutro');
            campo.maxLength = 9;
            campo.autocomplete = 'off';
            campo.spellcheck = false;
            campo.setAttribute('aria-label', t('codDe', d.nome));

            var botao = document.createElement('button');
            botao.type = 'button';
            botao.textContent = t('adicionar');
            botao.addEventListener('click', function () {
              adicionarDescoberto(d, nome, campo, botao);
            });
            campo.addEventListener('keydown', function (e) {
              if (e.key === 'Enter') {
                e.preventDefault();
                adicionarDescoberto(d, nome, campo, botao);
              }
            });

            acao.appendChild(campo);
            acao.appendChild(botao);
            li.appendChild(topo);
            li.appendChild(acao);
            ul.appendChild(li);
          });

          atualizarRede();
        }

        function atualizarRede() {
          var el = document.getElementById('rede');
          if (descobertos.length) el.textContent = t('nNaRede', descobertos.length);
          else if (totalPcs > 1) el.textContent = t('nenhumNaRede');
          else el.textContent = '';
        }

        async function adicionarDescoberto(d, campoNome, campoCodigo, botao) {
          var aviso = document.getElementById('pcAviso');
          var valor = campoCodigo.value.trim().toUpperCase();
          if (valor.length < 6) {
            aviso.className = 'aviso';
            aviso.textContent = t('digiteCodDe', d.nome);
            campoCodigo.focus();
            return;
          }

          var nome = (campoNome.value || '').trim() || d.nome;

          botao.disabled = true;
          var ok = await enviarPc('/api/pcs',
            'nome=' + encodeURIComponent(nome) +
            '&codigo=' + encodeURIComponent(valor) +
            '&origem=' + encodeURIComponent(d.endereco));
          botao.disabled = false;

          if (ok) {
            aviso.className = 'aviso ok';
            aviso.textContent = t('adicionado', nome);
            await atualizar();
          }
        }

        // O servidor responde {"erro":"codigo_do_erro"}; o texto sai no idioma da página.
        async function enviarPc(rota, dados) {
          var aviso = document.getElementById('pcAviso');
          aviso.className = 'aviso';
          aviso.textContent = '';
          try {
            var url = rota + '?k=' + encodeURIComponent(codigo) + '&' + dados;
            var r = await fetch(url, { method: 'POST', cache: 'no-store' });
            var corpo = {};
            try { corpo = await r.json(); } catch (e) { /* resposta sem JSON */ }
            if (!r.ok || corpo.erro) {
              aviso.textContent = corpo.erro ? t('e_' + corpo.erro) : t('falha', r.status);
              return false;
            }
            return true;
          } catch (e) {
            aviso.textContent = t('semConexaoTime');
            return false;
          }
        }

        async function adicionarPc(evento) {
          evento.preventDefault();
          var nome = document.getElementById('pcNome').value.trim();
          var link = document.getElementById('pcLink').value.trim();
          var aviso = document.getElementById('pcAviso');

          if (!link) {
            aviso.className = 'aviso';
            aviso.textContent = t('coleLink');
            return;
          }

          if (await enviarPc('/api/pcs', 'nome=' + encodeURIComponent(nome) + '&link=' + encodeURIComponent(link))) {
            document.getElementById('pcNome').value = '';
            document.getElementById('pcLink').value = '';
            aviso.className = 'aviso ok';
            aviso.textContent = t('pcAdicionado');
            await atualizar();
          }
        }

        async function removerPc(id) {
          var aviso = document.getElementById('pcAviso');
          if (await enviarPc('/api/pcs/remover', 'id=' + encodeURIComponent(id))) {
            aviso.className = 'aviso ok';
            aviso.textContent = t('pcRemovido');
            await atualizar();
          }
        }

        // Uma linha da lista: nome, tempo e a barra do tamanho relativo ao maior.
        // Com aoTocar, a linha vira botão (abre o detalhe do programa).
        function linhaComBarra(nome, segundos, maior, aoTocar, dica) {
          var li = document.createElement('li');
          var caixaLinha = li;
          if (aoTocar) {
            caixaLinha = document.createElement('button');
            caixaLinha.type = 'button';
            caixaLinha.className = 'app-linha';
            caixaLinha.title = dica || '';
            caixaLinha.addEventListener('click', aoTocar);
            li.appendChild(caixaLinha);
          }

          var topo = document.createElement('div');
          topo.className = 'nome';
          var rotulo = document.createElement('span');
          rotulo.textContent = nome;
          var tempo = document.createElement('span');
          tempo.textContent = seg(segundos);
          topo.appendChild(rotulo);
          topo.appendChild(tempo);

          var barra = document.createElement('div');
          barra.className = 'barra';
          var fill = document.createElement('i');
          fill.style.width = Math.max(2, Math.round((segundos / maior) * 100)) + '%';
          barra.appendChild(fill);

          caixaLinha.appendChild(topo);
          caixaLinha.appendChild(barra);
          return li;
        }

        function desenharApps(apps, todos) {
          var lista = document.getElementById('apps');
          var total = document.getElementById('totalItens');
          lista.innerHTML = '';
          apps = (apps || []).filter(function (a) { return a.segundos > 0; });
          var limite = todos ? apps.length : 8;
          total.textContent = apps.length
            ? (apps.length > limite ? t('top', limite, apps.length) : (apps.length === 1 ? t('item1') : t('itens', apps.length)))
            : '';

          if (!apps.length) {
            var vazio = document.createElement('li');
            vazio.className = 'vazio';
            vazio.textContent = t('semDados');
            lista.appendChild(vazio);
            return;
          }

          var maior = apps[0].segundos;
          apps.slice(0, limite).forEach(function (a) {
            lista.appendChild(linhaComBarra(a.nome, a.segundos, maior,
              function () { abrirApp(a.nome); }, t('verApp', a.nome)));
          });
        }

        // Soma o tempo dos programas por grupo (os grupos vêm do categorias.txt).
        // A seção só aparece quando algum programa da lista tem grupo.
        function desenharGrupos(apps) {
          var secao = document.getElementById('secaoGrupos');
          var lista = document.getElementById('grupos');
          var somas = {}, temGrupo = false;

          (apps || []).forEach(function (a) {
            if (!(a.segundos > 0)) return;
            var grupo = mapaGrupos[a.nome] || '';
            if (grupo) temGrupo = true;
            somas[grupo] = (somas[grupo] || 0) + a.segundos;
          });

          secao.hidden = !temGrupo;
          lista.innerHTML = '';
          if (!temGrupo) return;

          var itens = Object.keys(somas)
            .map(function (g) { return { nome: g || t('semGrupo'), segundos: somas[g] }; })
            .sort(function (a, b) { return b.segundos - a.segundos; });

          document.getElementById('totalGrupos').textContent =
            itens.length === 1 ? t('grupo1') : t('gruposN', itens.length);

          var maior = itens[0].segundos;
          itens.forEach(function (g) { lista.appendChild(linhaComBarra(g.nome, g.segundos, maior)); });
        }

        // Uso por hora: 24 barras (0h a 23h). A hora de maior uso fica destacada.
        function desenharHoras(horas) {
          var alvo = document.getElementById('horas');
          var eixo = document.getElementById('eixoHoras');
          var pico = document.getElementById('pico');

          var valores = [];
          for (var h = 0; h < 24; h++) valores.push(Math.max(0, (horas && horas[h]) || 0));
          var maior = Math.max.apply(null, valores);

          if (maior <= 0) {
            alvo.innerHTML = '<div class="vazio">' + t('semHoras') + '</div>';
            eixo.innerHTML = '';
            pico.textContent = '';
            return;
          }

          var hPico = valores.indexOf(maior);
          pico.textContent = t('pico', rotuloHora(hPico));

          var W = 600, H = 110, base = H - 2, largura = W / 24, folga = 3;
          var barras = '';
          valores.forEach(function (v, i) {
            var altura = v > 0 ? Math.max(2, (v / maior) * (H - 8)) : 0;
            var x = (i * largura + folga / 2).toFixed(1);
            var y = (base - altura).toFixed(1);
            barras += '<rect class="hbar' + (i === hPico ? ' pico' : '') + '" x="' + x + '" y="' + y +
                      '" width="' + (largura - folga).toFixed(1) + '" height="' + altura.toFixed(1) + '">' +
                      '<title>' + rotuloHora(i) + ': ' + seg(v) + '</title></rect>';
          });

          alvo.innerHTML =
            '<svg viewBox="0 0 ' + W + ' ' + H + '" role="img" aria-label="' + t('horasAria') + '">' +
              '<line class="hbase" x1="0" y1="' + base + '" x2="' + W + '" y2="' + base + '"/>' +
              barras +
            '</svg>';

          eixo.innerHTML = [0, 6, 12, 18, 23].map(function (i) {
            return '<span>' + rotuloHora(i) + '</span>';
          }).join('');
        }

        function desenharGrafico(hist, marcado) {
          var alvo = document.getElementById('grafico');
          var eixo = document.getElementById('eixo');
          var mediaEl = document.getElementById('media');

          var porDia = {};
          (hist || []).forEach(function (d) { porDia[d.data] = d.segundos; });

          // Janela fixa: os últimos 14 dias, com hoje no fim.
          var hoje = new Date();
          var base = new Date(hoje.getFullYear(), hoje.getMonth(), hoje.getDate());

          var datas = [];
          for (var i = 13; i >= 0; i--) {
            var d = new Date(base.getFullYear(), base.getMonth(), base.getDate() - i);
            var chave = iso(d);
            datas.push({
              iso: chave,
              segundos: porDia[chave] || 0,
              temDado: porDia[chave] !== undefined,
              rotulo: PT
                ? String(d.getDate()).padStart(2, '0') + '/' + String(d.getMonth() + 1).padStart(2, '0')
                : String(d.getMonth() + 1).padStart(2, '0') + '/' + String(d.getDate()).padStart(2, '0')
            });
          }

          var valores = datas.map(function (d) { return d.segundos; });
          var topoValor = Math.max(1, Math.max.apply(null, valores));
          var maximo = topoValor * 1.08;

          // Média só dos dias medidos: dias anteriores à instalação não contam como zero.
          var comDado = datas.filter(function (d) { return d.temDado; });
          var soma = comDado.reduce(function (a, d) { return a + d.segundos; }, 0);
          var media = comDado.length ? soma / comDado.length : 0;
          mediaEl.textContent = comDado.length ? t('media', seg(media)) : t('semDadosCurto');

          var W = 600, H = 150, padL = 2, padR = 2, padT = 12, padB = 8;
          var passo = datas.length > 1 ? (W - padL - padR) / (datas.length - 1) : 0;
          function px(i) { return padL + i * passo; }
          function py(v) { return H - padB - (v / maximo) * (H - padT - padB); }

          var pontos = datas.map(function (d, i) { return [px(i), py(d.segundos)]; });
          var linha = pontos.map(function (p, i) {
            return (i ? 'L' : 'M') + p[0].toFixed(1) + ' ' + p[1].toFixed(1);
          }).join(' ');
          var area = linha + ' L' + px(pontos.length - 1).toFixed(1) + ' ' + (H - padB) +
                     ' L' + px(0).toFixed(1) + ' ' + (H - padB) + ' Z';
          var ultimo = pontos[pontos.length - 1];

          // Verde na base, âmbar no meio, vermelho no topo: a cor acompanha a altura.
          var baseY = (H - padB).toFixed(1);
          var topoY = py(topoValor).toFixed(1);

          var grade = '';
          [0.25, 0.5, 0.75].forEach(function (f) {
            var y = (H - padB - f * (H - padT - padB)).toFixed(1);
            grade += '<line class="grade" x1="0" y1="' + y + '" x2="' + W + '" y2="' + y + '"/>';
          });
          grade += '<line class="grade" x1="0" y1="' + (H - padB) + '" x2="' + W + '" y2="' + (H - padB) + '"/>';

          var mediaY = py(media).toFixed(1);
          var linhaMedia = comDado.length >= 2
            ? '<line class="media" x1="0" y1="' + mediaY + '" x2="' + W + '" y2="' + mediaY + '"/>'
            : '';

          // Dia aberto no detalhe: marca a coluna e o ponto daquele dia.
          var marca = '';
          var indice = -1;
          if (marcado) {
            datas.forEach(function (d, i) { if (d.iso === marcado) indice = i; });
            if (indice >= 0) {
              var pm = pontos[indice];
              marca =
                '<line class="marca-dia" x1="' + pm[0].toFixed(1) + '" y1="' + padT + '" x2="' +
                  pm[0].toFixed(1) + '" y2="' + (H - padB) + '"/>' +
                '<circle class="ponto-marca" cx="' + pm[0].toFixed(1) + '" cy="' + pm[1].toFixed(1) + '" r="4.5"/>';
            }
          }

          var pontoHoje = indice < 0
            ? '<circle class="ponto" cx="' + ultimo[0].toFixed(1) + '" cy="' + ultimo[1].toFixed(1) + '" r="5"/>'
            : '';

          alvo.innerHTML =
            '<svg viewBox="0 0 ' + W + ' ' + H + '" role="img" aria-label="' + t('graficoAria') + '">' +
              '<defs>' +
                '<linearGradient id="degrade" x1="0" y1="0" x2="0" y2="1">' +
                  '<stop offset="0" stop-color="#ef5350" stop-opacity="0.20"/>' +
                  '<stop offset="0.55" stop-color="#f0b429" stop-opacity="0.10"/>' +
                  '<stop offset="1" stop-color="#34d399" stop-opacity="0"/>' +
                '</linearGradient>' +
                '<linearGradient id="linhaCor" gradientUnits="userSpaceOnUse" ' +
                  'x1="0" y1="' + baseY + '" x2="0" y2="' + topoY + '">' +
                  '<stop offset="0" stop-color="#34d399"/>' +
                  '<stop offset="0.55" stop-color="#f0b429"/>' +
                  '<stop offset="1" stop-color="#ef5350"/>' +
                '</linearGradient>' +
              '</defs>' +
              grade +
              linhaMedia +
              '<path class="area" d="' + area + '"/>' +
              '<path class="linha" d="' + linha + '"/>' +
              pontoHoje +
              marca +
            '</svg>';

          var marcas = datas.length <= 4
            ? datas.map(function (_, i) { return i; })
            : [0, Math.round((datas.length - 1) / 3), Math.round(2 * (datas.length - 1) / 3), datas.length - 1];
          eixo.innerHTML = marcas.map(function (i) {
            return '<span>' + datas[i].rotulo + '</span>';
          }).join('');
        }

        // Semana (de segunda até hoje) e mês corrente: total e média dos dias com uso,
        // comparados com o mesmo trecho da semana e do mês anteriores.
        function desenharPeriodos(hist) {
          var hoje = new Date();
          var base = new Date(hoje.getFullYear(), hoje.getMonth(), hoje.getDate());
          var desdeSegunda = (base.getDay() + 6) % 7;
          var inicioSemana = new Date(base.getFullYear(), base.getMonth(), base.getDate() - desdeSegunda);
          var inicioMes = new Date(base.getFullYear(), base.getMonth(), 1);

          // Mês passado até o mesmo dia (ou o último dia dele, se for mais curto).
          var diasMesPassado = new Date(base.getFullYear(), base.getMonth(), 0).getDate();
          var inicioMesPassado = new Date(base.getFullYear(), base.getMonth() - 1, 1);
          var fimMesPassado = new Date(base.getFullYear(), base.getMonth() - 1, Math.min(base.getDate(), diasMesPassado));

          function diasEntre(inicio, fim) {
            var a = iso(inicio), b = iso(fim);
            return (hist || []).filter(function (d) { return d.data >= a && d.data <= b && d.segundos > 0; });
          }
          function soma(dias) { return dias.reduce(function (a, d) { return a + d.segundos; }, 0); }

          function comparacao(atual, anterior, chave) {
            if (!(atual > 0) || !(anterior > 0)) return '';
            var pct = Math.round((atual - anterior) / anterior * 100);
            var seta = pct > 0 ? '\u25b2 ' : (pct < 0 ? '\u25bc ' : '= ');
            return t(chave, seta + Math.abs(pct) + '%');
          }

          function resumo(dias, anterior, idTotal, idSub, idComp, chaveComp) {
            var total = soma(dias);
            document.getElementById(idTotal).innerHTML = dias.length ? numero(total) : '--';
            document.getElementById(idSub).textContent = dias.length
              ? t('mediaDia', seg(total / dias.length)) + '  ·  ' + (dias.length === 1 ? t('diaUso1') : t('diasUso', dias.length))
              : t('semDadosCurto');
            document.getElementById(idComp).textContent = comparacao(total, anterior, chaveComp);
          }

          var semanaPassada = soma(diasEntre(
            new Date(inicioSemana.getFullYear(), inicioSemana.getMonth(), inicioSemana.getDate() - 7),
            new Date(base.getFullYear(), base.getMonth(), base.getDate() - 7)));
          var mesPassado = soma(diasEntre(inicioMesPassado, fimMesPassado));

          resumo(diasEntre(inicioSemana, base), semanaPassada, 'semanaTotal', 'semanaSub', 'semanaComp', 'vsSemana');
          resumo(diasEntre(inicioMes, base), mesPassado, 'mesTotal', 'mesSub', 'mesComp', 'vsMes');
        }

        // "dom., 25 de out." -> "DOM, 25 OUT"; em inglês, "SUN, OCT 25".
        function rotuloDia(dataIso) {
          var d = new Date(dataIso + 'T12:00:00');
          return d.toLocaleDateString(LOCAL, { weekday: 'short', day: '2-digit', month: 'short' })
            .replace(/\./g, '').replace(/ de /g, ' ').toUpperCase();
        }

        // Abaixo do gráfico: só o resumo dos dias anteriores (até 14); clicar abre o dia.
        function desenharDias(hist, vendoDia) {
          var secao = document.getElementById('secaoDias');
          var lista = document.getElementById('dias');
          lista.innerHTML = '';

          var hojeChave = hojeIso();
          var outros = (hist || [])
            .filter(function (d) { return d.data !== hojeChave && d.segundos > 0; })
            .sort(function (a, b) { return a.data < b.data ? 1 : -1; })
            .slice(0, 14);

          secao.hidden = vendoDia || outros.length === 0;
          if (secao.hidden) return;

          document.getElementById('diasConta').textContent =
            outros.length === 1 ? t('dia1') : t('diasN', outros.length);

          outros.forEach(function (d) {
            var li = document.createElement('li');
            var botao = document.createElement('button');
            botao.type = 'button';
            botao.className = 'dia-linha';
            botao.title = t('verDia', rotuloDia(d.data));

            var nome = document.createElement('span');
            nome.className = 'dia-data';
            nome.textContent = rotuloDia(d.data);

            var tempo = document.createElement('span');
            tempo.className = 'dia-tempo';
            var valor = document.createElement('span');
            valor.textContent = seg(d.segundos);
            var seta = document.createElement('i');
            seta.className = 'dia-seta';
            seta.textContent = '›';
            tempo.appendChild(valor);
            tempo.appendChild(seta);

            botao.appendChild(nome);
            botao.appendChild(tempo);
            botao.addEventListener('click', function () { abrirDia(d.data); });
            li.appendChild(botao);
            lista.appendChild(li);
          });
        }

        // Planilhas CSV: na visão de todos, junta o histórico de todos os PCs;
        // no detalhe de um PC, só daquele PC.
        function desenharBaixar() {
          function link(tipo) {
            return '/api/csv?tipo=' + tipo + '&k=' + encodeURIComponent(codigo) +
                   (pcAtual ? '&id=' + encodeURIComponent(pcAtual) : '');
          }
          document.getElementById('csvDias').href = link('dias');
          document.getElementById('csvApps').href = link('apps');
          document.getElementById('csvHoras').href = link('horas');
          document.getElementById('notaBaixar').textContent =
            (pcAtual || totalPcs < 2) ? t('notaUm') : t('notaTodos');
        }

        function desenhar(res, hist, dia, appDados) {
          var detalhe = !!pcAtual;
          var vendoApp = !!appDados;
          var vendoDia = !!dia && !vendoApp;

          document.getElementById('voltar').hidden = !(detalhe || vendoDia || vendoApp);
          document.getElementById('btnVoltar').textContent = (vendoDia || vendoApp) ? t('voltar') : t('todosPcs');
          // A data do dia aberto já aparece na linha "Dia"; aqui fica só o nome do PC, se houver.
          document.getElementById('voltarNome').textContent = detalhe ? res.maquina : '';
          document.getElementById('gerenciar').hidden = detalhe || vendoDia || vendoApp;
          document.getElementById('secaoPeriodos').hidden = vendoDia;
          document.getElementById('secaoBaixar').hidden = vendoDia || vendoApp;
          document.getElementById('secaoApps').hidden = vendoApp;
          document.getElementById('secaoHoras').hidden = vendoApp;
          document.getElementById('appCab').hidden = !vendoApp;

          if (vendoApp) {
            // Detalhe de um programa: hoje, os últimos 14 dias, semana, mês e os dias com uso.
            var appHist = (appDados.dias || []).filter(function (d) { return d.segundos > 0; });
            var hojeChave = hojeIso();
            var deHoje = appHist.filter(function (d) { return d.data === hojeChave; })[0];
            var hoje = new Date();
            var inicio14 = iso(new Date(hoje.getFullYear(), hoje.getMonth(), hoje.getDate() - 13));
            var usados = appHist.filter(function (d) { return d.data >= inicio14; }).length;

            document.getElementById('appNome').textContent = appDados.nome;
            document.getElementById('appGrupo').textContent = appDados.grupo || '';
            document.getElementById('hoje').innerHTML = numero(deHoje ? deHoje.segundos : 0);
            document.getElementById('rotuloHoje').textContent = t('hoje');
            document.getElementById('data').textContent = rotuloDia(res.data);
            document.getElementById('periodo').textContent = usados ? t('usadoDias', usados) : t('semUso14');
            document.getElementById('secaoPcs').hidden = true;
            document.getElementById('secaoGrupos').hidden = true;
            document.getElementById('resumoPcs').textContent = '';

            desenharGrafico(appHist, null);
            desenharPeriodos(appHist);
            desenharDias(appHist, false);
          } else {
            var dados = vendoDia ? dia : res;

            document.getElementById('hoje').innerHTML = numero(dados.segundos);
            document.getElementById('rotuloHoje').textContent = vendoDia ? t('dia') : t('hoje');
            document.getElementById('data').textContent = rotuloDia(vendoDia ? dia.data : res.data);

            document.getElementById('periodo').textContent =
              dados.inicio !== '--:--' ? t('periodo', dados.inicio, dados.ultima)
                                       : (vendoDia ? t('semAtividade') : t('semAtividadeHoje'));

            if (vendoDia) {
              document.getElementById('secaoPcs').hidden = true;
              document.getElementById('resumoPcs').textContent = '';
            } else {
              desenharPcs(res.pcs);
            }

            desenharGrupos(dados.apps);
            desenharApps(dados.apps, vendoDia);
            desenharHoras(dados.horas);
            desenharGrafico(hist, vendoDia ? dia.data : null);
            desenharPeriodos(hist);
            desenharDias(hist, vendoDia);
            desenharBaixar();
          }

          if (vendoDia) {
            estado.textContent = t('historico');
            estado.className = '';
          } else {
            estado.textContent = res.contando ? t('contando', res.atual || '—') : t('pausado');
            estado.className = res.contando ? 'ativo' : '';
          }

          document.getElementById('rodape').textContent =
            res.usuario ? res.maquina + ' · ' + res.usuario : res.maquina;
          document.getElementById('hora').textContent =
            new Date().toLocaleTimeString(LOCAL, { hour: '2-digit', minute: '2-digit' });
        }

        async function atualizar() {
          if (!codigo) {
            caixa.style.display = 'block';
            painel.hidden = true;
            return;
          }
          try {
            var resumo = null, hist = null;

            if (pcAtual) {
              var um = await buscar('/api/pc?id=' + encodeURIComponent(pcAtual) + '&n=' + DIAS_HISTORICO);
              if (um.erro) {
                pcAtual = null;   // PC saiu da rede: volta para a visão de todos
                diaAtual = null;
                trocarUrl(null);
              } else {
                resumo = um.resumo;
                hist = um.dias;
              }
            }

            if (!resumo) {
              var dois = await Promise.all([buscar('/api/resumo'), buscar('/api/dias?n=' + DIAS_HISTORICO)]);
              resumo = dois[0];
              hist = dois[1];
            }

            // Detalhe de um programa: o histórico dele (somado ou de um PC só).
            var appDados = null;
            if (appAtual) {
              try {
                appDados = await buscar('/api/app?nome=' + encodeURIComponent(appAtual) + '&n=' + DIAS_HISTORICO +
                                        (pcAtual ? '&id=' + encodeURIComponent(pcAtual) : ''));
              } catch (e) { appDados = null; }

              if (!appDados || appDados.erro) {
                appDados = null;
                appAtual = null;
                trocarUrl(pcAtual);
              }
            }

            // Dia aberto no detalhe: busca só quando há um dia escolhido.
            var dia = null;
            if (diaAtual && !appDados) {
              try {
                dia = await buscar('/api/dia?data=' + encodeURIComponent(diaAtual) +
                                   (pcAtual ? '&id=' + encodeURIComponent(pcAtual) : ''));
              } catch (e) { dia = null; }

              if (!dia || dia.erro) {
                dia = null;
                diaAtual = null;
              }
            }

            var rede = await buscar('/api/descobertos');
            try { mapaGrupos = (await buscar('/api/grupos')).mapa || {}; } catch (e) { mapaGrupos = {}; }

            caixa.style.display = 'none';
            painel.hidden = false;
            desenhar(resumo, hist, dia, appDados);
            desenharRede(rede);
          } catch (e) {
            if (e.message === 'codigo') {
              localStorage.removeItem('time_codigo');
              codigo = '';
              caixa.style.display = 'block';
              painel.hidden = true;
              estado.textContent = t('codigoInvalido');
            } else {
              estado.textContent = t('semConexao');
              estado.className = '';
            }
          }
        }

        document.getElementById('entrar').addEventListener('click', salvarCodigo);
        document.getElementById('entrada').addEventListener('keydown', function (e) {
          if (e.key === 'Enter') salvarCodigo();
        });
        document.getElementById('formPc').addEventListener('submit', adicionarPc);
        document.getElementById('btnVoltar').addEventListener('click', function () {
          if (appAtual) { appAtual = null; trocarUrl(pcAtual); atualizar(); }
          else if (diaAtual) { diaAtual = null; trocarUrl(pcAtual); atualizar(); }
          else voltarTodos();
        });

        if (codigo) localStorage.setItem('time_codigo', codigo);
        else caixa.style.display = 'block';

        atualizar();
        setInterval(atualizar, 30000);
        document.addEventListener('visibilitychange', function () {
          if (!document.hidden) atualizar();
        });
        </script>
        </body>
        </html>
        """;
}
