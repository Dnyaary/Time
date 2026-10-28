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

// Painel do celular: config.txt, PCs cadastrados (pcs.txt), liga/desliga do servidor e firewall.

partial class Contador
{
    // ---------- Painel do celular ----------
    // Um servidor HTTP pequeno (Api.cs) mostra os mesmos dados na rede local.
    // Fica desligado por padrão; a caixa "Acessar pelo celular" liga e grava em config.txt.

    const string NOME_REGRA = "Time - painel no celular";
    const string NOME_REGRA_DESCOBERTA = "Time - descoberta na rede";
    const string NOME_REGRA_PROGRAMA = "Time - programa";

    // Versão do formato das regras de firewall. Subir este número faz o programa
    // recriar as regras na próxima vez que a janela abrir.
    const int REGRA_VERSAO = 1;

    void CarregarConfig()
    {
        portaApi = ServidorApi.PORTA_PADRAO;
        codigoApi = CodigoNovo();
        apiLigada = false;
        firewallPorta = 0;
        firewallUdp = 0;
        regraVersao = 0;

        try
        {
            if (File.Exists(arquivoConfig))
            {
                foreach (string bruta in File.ReadAllLines(arquivoConfig))
                {
                    string linha = bruta.Trim();
                    if (linha.Length == 0 || linha.StartsWith('#')) continue;

                    int igual = linha.IndexOf('=');
                    if (igual <= 0) continue;
                    string chave = linha[..igual].Trim().ToLowerInvariant();
                    string valor = linha[(igual + 1)..].Trim();

                    switch (chave)
                    {
                        case "porta":
                            if (int.TryParse(valor, out int porta) && porta >= 1024 && porta <= 65535)
                                portaApi = porta;
                            break;
                        case "codigo":
                            if (valor.Length >= 6) codigoApi = valor.ToUpperInvariant();
                            break;
                        case "api":
                            apiLigada = valor is "1" or "true" or "sim";
                            break;
                        case "firewall":
                            int.TryParse(valor, out firewallPorta);
                            break;
                        case "firewalludp":
                            int.TryParse(valor, out firewallUdp);
                            break;
                        case "regraversao":
                            int.TryParse(valor, out regraVersao);
                            break;
                    }
                }
            }
        }
        catch { /* arquivo com problema: segue com o padrão */ }

        SalvarConfig();   // cria o arquivo na primeira execução e mantém o formato
        CarregarPcs();
    }

    void SalvarConfig()
    {
        try
        {
            var linhas = new List<string>
            {
                L.T("# Time — configurações", "# Time — settings"),
                L.T("# Feche e abra o Time depois de editar este arquivo.", "# Close and reopen Time after editing this file."),
                "#",
                L.T("# porta      porta do painel no celular (1024 a 65535)", "# porta      phone dashboard port (1024 to 65535)"),
                L.T("# codigo     senha do painel; troque para bloquear os aparelhos já pareados", "# codigo     dashboard password; change it to lock out devices already paired"),
                L.T("# api        1 = painel ligado ao abrir o programa; 0 = desligado", "# api        1 = dashboard on when the program starts; 0 = off"),
                L.T("# firewall   porta TCP que o Windows já liberou (0 = nenhuma)", "# firewall   TCP port Windows has already opened (0 = none)"),
                L.T("# firewalludp porta UDP da descoberta já liberada (0 = nenhuma)", "# firewalludp discovery UDP port already opened (0 = none)"),
                L.T("# regraversao versão das regras de firewall (não precisa mexer)", "# regraversao firewall rules version (no need to change)"),
                "",
                $"porta={portaApi}",
                $"codigo={codigoApi}",
                $"api={(apiLigada ? 1 : 0)}",
                $"firewall={firewallPorta}",
                $"firewalludp={firewallUdp}",
                $"regraversao={regraVersao}",
                ""
            };
            File.WriteAllLines(arquivoConfig, linhas, new UTF8Encoding(true));
        }
        catch { /* sem escrita na pasta: segue com o que está em memória */ }
    }

    // pcs.txt: os outros PCs que este painel também mostra.
    // Formato: nome; codigo; maquina; ip:porta
    //   nome    rótulo que aparece no painel (você escolhe)
    //   codigo  código de acesso do outro PC (o mesmo do link)
    //   maquina nome que o outro PC anuncia na rede (identidade da descoberta)
    //   ip:porta  último endereço conhecido — a descoberta atualiza sozinha
    void CarregarPcs()
    {
        var nova = new List<Remoto>();

        try
        {
            if (!File.Exists(arquivoPcs))
            {
                File.WriteAllLines(arquivoPcs, new[]
                {
                    L.T("# Time — outros PCs", "# Time — other PCs"),
                    L.T("# Um por linha: nome; codigo; maquina; ip:porta", "# One per line: name; code; machine; ip:port"),
                    L.T("# Exemplo:", "# Example:"),
                    L.T("# Escritório; ABCD-EFGH; DESKTOP-ESC; 192.168.0.50:8787", "# Office; ABCD-EFGH; DESKTOP-OFFICE; 192.168.0.50:8787"),
                    L.T("# Notebook; IJKL-MNOP; DESKTOP-NOTE; 192.168.0.51:8787", "# Laptop; IJKL-MNOP; DESKTOP-NOTE; 192.168.0.51:8787"),
                    L.T("# A descoberta na rede acha o IP sozinho; o endereço é só reserva.", "# Network discovery finds the IP by itself; the address is only a fallback."),
                    ""
                }, new UTF8Encoding(true));
                return;
            }

            foreach (string bruta in File.ReadAllLines(arquivoPcs))
            {
                string linha = bruta.Trim();
                if (linha.Length == 0 || linha.StartsWith('#')) continue;

                string[] campos = linha.Split(';');
                if (campos.Length < 2) continue;

                string nome = campos[0].Trim();
                string segundo = campos[1].Trim();
                if (segundo.Length == 0) continue;

                string codigo = segundo.ToUpperInvariant();
                if (codigo.Length < 6) continue;

                string maquina = campos.Length > 2 ? campos[2].Trim() : "";
                string texto = campos.Length > 3 ? campos[3].Trim() : "";
                if (!EnderecoValido(texto, out Uri? endereco)) continue;
                if (EhEstePc(endereco!)) continue;

                nova.Add(new Remoto(nome.Length > 0 ? nome : (maquina.Length > 0 ? maquina : endereco!.Host),
                                    codigo, maquina, endereco!));
            }
        }
        catch { /* arquivo com problema: o painel mostra só este PC */ }

        AplicarPcs(nova);
    }

    // "192.168.0.51:8787" ou "http://192.168.0.51:8787/?k=..." viram o mesmo Uri.
    static bool EnderecoValido(string texto, out Uri? endereco)
    {
        endereco = null;
        if (string.IsNullOrWhiteSpace(texto)) return false;

        string alvo = texto.Contains("://", StringComparison.Ordinal) ? texto : "http://" + texto;
        if (!Uri.TryCreate(alvo, UriKind.Absolute, out Uri? uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp) return false;
        if (uri.Host.Length == 0) return false;

        endereco = uri;
        return true;
    }

    static string CodigoDoLink(Uri endereco)
    {
        foreach (string par in endereco.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int igual = par.IndexOf('=');
            if (igual > 0 && par[..igual].Equals("k", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(par[(igual + 1)..]);
        }
        return "";
    }

    // O id do PC no painel é o nome escolhido: ele não muda quando o IP muda.
    static string IdDoRemoto(Remoto remoto) => remoto.Nome;

    // Cadastrar este mesmo PC dobraria a soma e repetiria a linha na lista.
    bool EhEstePc(Uri endereco)
    {
        if (endereco.Port != portaApi) return false;

        string host = endereco.Host.ToLowerInvariant();
        if (host is "localhost" or "127.0.0.1" or "::1") return true;
        if (host == Environment.MachineName.ToLowerInvariant()) return true;

        return EhIpDestePc(host);
    }

    // Troca a lista em uso e zera o cache para a próxima consulta já valer.
    void AplicarPcs(List<Remoto> lista)
    {
        lock (travaRemotos)
        {
            remotos.Clear();
            remotos.AddRange(lista);
            cacheRemotos = null;
        }
    }

    readonly object travaArquivoPcs = new();

    void SalvarPcs(List<Remoto> lista)
    {
        lock (travaArquivoPcs)
        {
            try
            {
                var linhas = new List<string>
                {
                    L.T("# Time — outros PCs", "# Time — other PCs"),
                    L.T("# Um por linha: nome; codigo; maquina; ip:porta", "# One per line: name; code; machine; ip:port"),
                    L.T("# A descoberta na rede acha o IP sozinho; o endereço é só reserva.", "# Network discovery finds the IP by itself; the address is only a fallback."),
                };

                foreach (var r in lista)
                    linhas.Add($"{r.Nome}; {r.Codigo}; {r.Maquina}; {r.Endereco.Host}:{r.Endereco.Port}");

                linhas.Add("");
                File.WriteAllLines(arquivoPcs, linhas, new UTF8Encoding(true));
            }
            catch { /* sem escrita: a lista em memória vale para esta sessão */ }
        }
    }

    // Chamado pelo painel (POST /api/pcs). Devolve {"ok":true} ou {"erro":"codigo_do_erro"};
    // o painel mostra o texto do erro no idioma do celular.
    // Dois caminhos:
    //   origem  "ip:porta" de um PC descoberto na rede + o código digitado uma vez
    //   link    o endereço completo copiado na janela do outro PC
    string AdicionarPc(string nome, string link, string codigo, string origem)
    {
        nome = (nome ?? "").Trim();
        link = (link ?? "").Trim();
        codigo = (codigo ?? "").Trim().ToUpperInvariant();
        origem = (origem ?? "").Trim();
        if (nome.Length > 40) nome = nome[..40];
        if (codigo.Length > 40) codigo = codigo[..40];

        if (nome.Equals(PC_LOCAL, StringComparison.OrdinalIgnoreCase))
            return "{\"erro\":\"nome_reservado\"}";

        Uri? endereco = null;
        string maquina = "";

        if (origem.Length > 0)
        {
            if (!EnderecoValido(origem, out endereco))
                return "{\"erro\":\"endereco_invalido\"}";

            if (codigo.Length < 6)
                return "{\"erro\":\"codigo_vazio\"}";

            var vizinho = Descoberta.ProcurarPorEndereco(endereco!.Host, endereco.Port);
            if (vizinho == null)
                return "{\"erro\":\"saiu_da_rede\"}";

            var conferiu = Descoberta.Verificar(vizinho.Ip, codigo);
            if (conferiu == Descoberta.Verificacao.SemResposta)
                return "{\"erro\":\"sem_resposta\"}";
            if (conferiu != Descoberta.Verificacao.Ok)
                return "{\"erro\":\"codigo_errado\"}";

            maquina = vizinho.Nome;
            if (nome.Length == 0) nome = maquina;
        }
        else
        {
            if (!Uri.TryCreate(link, UriKind.Absolute, out endereco) || endereco.Scheme != Uri.UriSchemeHttp)
                return "{\"erro\":\"link_invalido\"}";

            codigo = CodigoDoLink(endereco);
            if (codigo.Length == 0)
                return "{\"erro\":\"link_sem_codigo\"}";

            if (nome.Length == 0) nome = endereco.Host;

            // Já descoberto na rede? Aproveita para guardar o nome da máquina.
            var vizinho = Descoberta.ProcurarPorEndereco(endereco.Host, endereco.Port);
            if (vizinho != null) maquina = vizinho.Nome;
        }

        // O pcs.txt separa os campos por ";" e os PCs por linha: tira ";" e
        // quebras de linha do que veio do painel ou da rede.
        nome = Limpar(nome);
        codigo = Limpar(codigo).ToUpperInvariant();
        maquina = Limpar(maquina);
        if (nome.Length == 0) nome = endereco!.Host;
        if (nome.Equals(PC_LOCAL, StringComparison.OrdinalIgnoreCase))
            return "{\"erro\":\"nome_reservado\"}";

        if (EhEstePc(endereco!))
            return "{\"erro\":\"este_pc\"}";

        List<Remoto> nova;
        lock (travaRemotos)
        {
            if (remotos.Count >= 20)
                return "{\"erro\":\"limite\"}";

            if (remotos.Any(r => r.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase)))
                return "{\"erro\":\"nome_repetido\"}";

            if (remotos.Any(r => r.Endereco.Host == endereco!.Host && r.Endereco.Port == endereco.Port))
                return "{\"erro\":\"ja_na_lista\"}";

            nova = new List<Remoto>(remotos)
            {
                new(nome, codigo, maquina, endereco!)
            };

            // Grava e aplica dentro da trava: dois cadastros ao mesmo tempo não se apagam.
            SalvarPcs(nova);
            AplicarPcs(nova);
        }

        return "{\"ok\":true}";
    }

    static string Limpar(string texto) =>
        new string((texto ?? "").Where(c => !char.IsControl(c) && c != ';').ToArray()).Trim();

    string RemoverPc(string id)
    {
        List<Remoto> nova;
        lock (travaRemotos)
        {
            nova = remotos.Where(r => IdDoRemoto(r) != id).ToList();
            if (nova.Count == remotos.Count)
                return "{\"erro\":\"nao_encontrado\"}";

            SalvarPcs(nova);
            AplicarPcs(nova);
        }

        return "{\"ok\":true}";
    }

    // 8 caracteres fáceis de ditar (sem 0/O e 1/I), exibidos como ABCD-EFGH.
    static string CodigoNovo()
    {
        const string alfabeto = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        byte[] bytes = RandomNumberGenerator.GetBytes(8);
        var sb = new StringBuilder(9);
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i == 4) sb.Append('-');
            sb.Append(alfabeto[bytes[i] % alfabeto.Length]);
        }
        return sb.ToString();
    }

    void AlternarApi()
    {
        if (ajustandoApi) return;
        ajustandoApi = true;
        try
        {
            if (janela.Celular.Checked)
            {
                if (AbrirApi())
                {
                    apiLigada = true;
                    tentouFirewall = true;
                    LiberarFirewallSePreciso();
                }
                else
                {
                    janela.Celular.Checked = false;
                    MessageBox.Show(janela,
                        L.T($"Não foi possível abrir a porta {portaApi}.\n\n" +
                            "Outro programa pode estar usando essa porta. Mude a porta no config.txt " +
                            "(na pasta dos dados) e abra o Time de novo.",
                            $"Could not open port {portaApi}.\n\n" +
                            "Another program may be using that port. Change the port in config.txt " +
                            "(in the data folder) and open Time again."),
                        "Time", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                FecharApi();
                apiLigada = false;
            }

            SalvarConfig();
            AtualizarLink();
        }
        finally { ajustandoApi = false; }
    }

    bool AbrirApi()
    {
        if (servidor != null) return true;
        try
        {
            servidor = new ServidorApi(portaApi, codigoApi, JsonResumo, JsonDias, JsonPc, JsonDia,
                                       JsonDescobertos, AdicionarPc, RemoverPc, Csv, JsonApp, JsonGrupos, JsonSemana);
            Descoberta.Iniciar(Environment.MachineName, portaApi, codigoApi);
            return true;
        }
        catch
        {
            servidor = null;
            Descoberta.Parar();
            return false;
        }
    }

    void FecharApi()
    {
        try { servidor?.Dispose(); } catch { }
        servidor = null;
        Descoberta.Parar();
    }

    void AtualizarLink()
    {
        bool ligado = servidor != null;
        var redes = ligado ? RedesLocais() : new List<(string Ip, string Placa)>();

        janela.Link.Visible = ligado;
        janela.CopiarLink.Visible = ligado;
        janela.Link.Text = ligado ? LinkPara(redes.Count > 0 ? redes[0].Ip : "localhost") : "";

        // PC em duas redes (cabo e Wi-Fi, ou duas redes na empresa): o celular pode
        // estar em qualquer uma, então aparece um link para cada.
        bool duas = redes.Count > 1;
        janela.TituloLink2.Text = duas
            ? L.T($"Na outra rede ({redes[1].Placa})", $"On the other network ({redes[1].Placa})")
            : "";
        janela.Link2.Text = duas ? LinkPara(redes[1].Ip) : "";
        janela.MostrarLink2(duas);
    }

    string LinkPara(string ip) => $"http://{ip}:{portaApi}/?k={codigoApi}";

    void CopiarLink(TextBox caixa, Button botao)
    {
        try
        {
            Clipboard.SetText(caixa.Text);
            botao.Text = L.T("Copiado!", "Copied!");
            var volta = new System.Windows.Forms.Timer { Interval = 1500 };
            volta.Tick += (_, _) =>
            {
                volta.Stop();
                volta.Dispose();
                botao.Text = L.T("Copiar", "Copy");
            };
            volta.Start();
        }
        catch { }
    }

    // Placas que não são uma rede de verdade: o celular nunca está nelas.
    static readonly string[] PLACAS_VIRTUAIS =
        { "Hyper-V", "vEthernet", "WSL", "VirtualBox", "VMware", "Loopback", "TAP-", "Bluetooth", "Npcap" };

    // Endereços IPv4 que o celular pode alcançar, do melhor para o pior. Vem primeiro
    // a placa com roteador (gateway) e endereço de rede privada — o Wi-Fi ou o cabo.
    static List<(string Ip, string Placa)> RedesLocais()
    {
        var achados = new List<(string Ip, string Placa, int Nota)>();
        try
        {
            foreach (var placa in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (placa.OperationalStatus != OperationalStatus.Up) continue;
                if (placa.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                if (PLACAS_VIRTUAIS.Any(v => placa.Name.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                                             placa.Description.Contains(v, StringComparison.OrdinalIgnoreCase))) continue;

                var propriedades = placa.GetIPProperties();
                bool temRoteador = propriedades.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

                foreach (var endereco in propriedades.UnicastAddresses)
                {
                    if (endereco.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = endereco.Address.ToString();
                    if (ip.StartsWith("169.254.")) continue;   // endereço automático: rede sem DHCP

                    int nota = (temRoteador ? 2 : 0) + (EhRedePrivada(endereco.Address) ? 1 : 0);
                    if (nota > 0) achados.Add((ip, placa.Name, nota));
                }
            }
        }
        catch { }

        // OrderBy é estável: com a mesma nota, fica a ordem do Windows.
        return achados.OrderByDescending(x => x.Nota).Select(x => (x.Ip, x.Placa)).ToList();
    }

    static bool EhRedePrivada(IPAddress ip)
    {
        byte[] b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }

    // Qualquer IPv4 deste PC, em qualquer placa (inclusive as virtuais).
    static bool EhIpDestePc(string host)
    {
        try
        {
            foreach (var placa in NetworkInterface.GetAllNetworkInterfaces())
                foreach (var endereco in placa.GetIPProperties().UnicastAddresses)
                    if (endereco.Address.ToString().Equals(host, StringComparison.OrdinalIgnoreCase))
                        return true;
        }
        catch { }
        return false;
    }

    // O Windows bloqueia conexões de fora por padrão. A regra é criada uma vez,
    // com UAC, e fica valendo para este .exe nesta porta.
    void LiberarFirewallSePreciso()
    {
        if (firewallPorta == portaApi && firewallUdp == Descoberta.PORTA && regraVersao == REGRA_VERSAO) return;

        bool liberou = false;
        try
        {
            // A regra por porta não depende do caminho do .exe, então mover o Time
            // (ou testar um build em outra pasta) não quebra o celular. A regra por
            // programa evita o aviso do Windows — e, quando esse aviso é cancelado,
            // o Windows cria uma regra de BLOQUEIO para o .exe, que ganha da
            // liberação e deixa o celular de fora. Por isso os bloqueios com o nome
            // do executável são apagados antes de liberar as portas.
            string exe = Environment.ProcessPath ?? Application.ExecutablePath;
            string nomeExe = Path.GetFileName(exe);
            string comando =
                $"netsh advfirewall firewall delete rule name=\"{NOME_REGRA}\" >nul 2>&1 & " +
                $"netsh advfirewall firewall delete rule name=\"{NOME_REGRA_DESCOBERTA}\" >nul 2>&1 & " +
                $"netsh advfirewall firewall delete rule name=\"{NOME_REGRA_PROGRAMA}\" >nul 2>&1 & " +
                $"netsh advfirewall firewall delete rule name=\"{nomeExe}\" >nul 2>&1 & " +
                $"netsh advfirewall firewall add rule name=\"{NOME_REGRA}\" dir=in action=allow " +
                $"protocol=TCP localport={portaApi} profile=any enable=yes & " +
                $"netsh advfirewall firewall add rule name=\"{NOME_REGRA_DESCOBERTA}\" dir=in action=allow " +
                $"protocol=UDP localport={Descoberta.PORTA} profile=any enable=yes & " +
                $"netsh advfirewall firewall add rule name=\"{NOME_REGRA_PROGRAMA}\" dir=in action=allow " +
                $"program=\"{exe}\" profile=any enable=yes";

            var psi = new ProcessStartInfo("cmd.exe", "/c " + comando)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(120000);

            liberou = RegraFirewallExiste(NOME_REGRA) && RegraFirewallExiste(NOME_REGRA_DESCOBERTA);
        }
        catch { liberou = false; }

        if (liberou)
        {
            firewallPorta = portaApi;
            firewallUdp = Descoberta.PORTA;
            regraVersao = REGRA_VERSAO;
            SalvarConfig();
            return;
        }

        if (!avisouFirewall)
        {
            avisouFirewall = true;
            MessageBox.Show(janela,
                L.T("O Windows não liberou a porta no firewall.\n\n" +
                    "No próprio PC o link funciona, mas o celular pode não conseguir abrir. " +
                    "Se o Windows perguntar, permita o Time em redes privadas.",
                    "Windows did not open the port in the firewall.\n\n" +
                    "The link works on this PC, but the phone may not be able to open it. " +
                    "If Windows asks, allow Time on private networks."),
                "Time", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    static bool RegraFirewallExiste(string nomeRegra)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", $"advfirewall firewall show rule name=\"{nomeRegra}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.StandardOutput.ReadToEnd();   // esvazia o buffer para não travar o processo
            p.WaitForExit(10000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
