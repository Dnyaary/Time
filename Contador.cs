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

// O coração do Time: a contagem do tempo, as janelas, iniciar com o Windows e as
// chamadas ao Windows. As outras partes da classe ficam nos arquivos Contador.*.cs.

partial class Contador : ApplicationContext
{
    const int LIMITE_SEGUNDOS = 30;
    static readonly string CABECALHO = L.T(
        "Data;Usuario;Maquina;Primeira atividade;Ultima atividade;Tempo ativo;Segundos;Limite inatividade (s);Parado (s)",
        "Date;User;Machine;First activity;Last activity;Active time;Seconds;Idle limit (s);Idle (s)");
    static readonly string CABECALHO_APPS = L.T(
        "Data;Usuario;Maquina;Item;Tempo;Segundos",
        "Date;User;Machine;Item;Time;Seconds");
    static readonly string CABECALHO_HORAS = L.T(
        "Data;Usuario;Maquina;Hora;Segundos",
        "Date;User;Machine;Hour;Seconds");
    static readonly string OUTROS = L.T("Outros", "Others");

    // Id do próprio PC na lista do painel; os outros usam "host:porta".
    const string PC_LOCAL = "este";

    // pasta/pastaFixa ficam declaradas junto de PastaDados(), no fim da classe.
    readonly string arquivo = Path.Combine(pasta, "atividade_diaria.csv");
    readonly string arquivoApps = Path.Combine(pasta, "apps_diario.csv");
    readonly string arquivoHoras = Path.Combine(pasta, "horas_diario.csv");
    readonly string arquivoRegras = Path.Combine(pasta, "categorias.txt");
    readonly string arquivoConfig = Path.Combine(pasta, "config.txt");
    readonly string arquivoPcs = Path.Combine(pasta, "pcs.txt");

    readonly Dictionary<string, RegistroDia> dias = new();                       // chave: AAAA-MM-DD
    readonly Dictionary<string, Dictionary<string, long>> apps = new();         // dia -> item -> ms
    readonly Dictionary<string, long[]> horas = new();                           // dia -> ms em cada hora (0 a 23)
    List<Regra> regras = new();
    Dictionary<string, string> grupos = new(StringComparer.OrdinalIgnoreCase);   // nome do item -> grupo
    Dictionary<string, string> focos = new(StringComparer.OrdinalIgnoreCase);    // grupo -> produtivo/distracao/neutro
    DateTime regrasModificadas;

    readonly object trava = new();
    readonly Janela janela = new();
    readonly JanelaMini mini = new();
    readonly System.Windows.Forms.Timer timerTela = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer timerAtalho = new() { Interval = 150 };   // desvia o atalho para a thread da interface
    readonly EventWaitHandle pedido = new(false, EventResetMode.AutoReset, Program.PEDIDO);
    readonly Thread threadContagem;
    volatile bool rodando = true;
    volatile bool ativo;
    volatile bool atalhoPendente;
    volatile bool infoPendente;
    volatile bool pedidoAbertura;
    volatile bool redeMudou;              // placa de rede ligou/desligou: refaz os links
    volatile string itemAgora = OUTROS;   // programa em foco, para o painel do celular
    bool ajustandoInicio;
    bool avisouPasta;

    // Arquivos que existiam mas não puderam ser lidos ao abrir. Eles NÃO são
    // regravados nesta sessão (senão o histórico seria trocado por um arquivo
    // quase vazio); os dados novos vão para um arquivo ao lado, *.novo.csv.
    bool protegerDias, protegerApps, protegerHoras;

    // Painel do celular
    ServidorApi? servidor;
    int portaApi = ServidorApi.PORTA_PADRAO;
    string codigoApi = "";
    int firewallPorta;
    int firewallUdp;
    int regraVersao;          // versão do formato das regras de firewall já criadas
    bool apiLigada;
    bool ajustandoApi;
    bool tentouFirewall;
    bool avisouFirewall;

    // Outros PCs que este painel também mostra (pcs.txt).
    readonly List<Remoto> remotos = new();
    readonly object travaRemotos = new();
    List<RemotoDados>? cacheRemotos;
    DateTime cacheRemotosEm;

    public Contador()
    {
        Carregar();
        CarregarApps();
        CarregarHoras();
        CarregarRegras();
        CarregarConfig();

        // Sem ícone na bandeja: as opções ficam na própria janela.
        janela.IniciarComWindows.Checked = IniciaComWindows();
        janela.IniciarComWindows.CheckedChanged += (_, _) =>
        {
            if (ajustandoInicio) return;
            ajustandoInicio = true;
            try
            {
                DefinirInicio(janela.IniciarComWindows.Checked);
                janela.IniciarComWindows.Checked = IniciaComWindows();   // reflete o que realmente ficou gravado
            }
            catch { }
            finally { ajustandoInicio = false; }
        };
        janela.AbrirPasta.Click += (_, _) => AbrirPastaDados();
        janela.EditarCategorias.Click += (_, _) => EditarCategorias();
        janela.Fechou += Sair;
        mini.Minimizar.Click += (_, _) => mini.Hide();   // minimizar volta a esconder

        var dicas = new ToolTip();
        dicas.SetToolTip(janela.AbrirPasta, pasta);   // mostra o caminho real dos dados
        dicas.SetToolTip(janela.Celular, L.T(
            "Mostra um painel com seus dados no navegador de um celular na mesma rede Wi-Fi.",
            "Shows a dashboard with your data in a phone's browser on the same Wi-Fi network."));
        dicas.SetToolTip(janela.Link, L.T(
            "Abra este endereço no celular. O fim do link é o código de acesso.",
            "Open this address on your phone. The end of the link is the access code."));

        janela.Celular.Checked = apiLigada;
        janela.Celular.CheckedChanged += (_, _) => AlternarApi();
        janela.CopiarLink.Click += (_, _) => CopiarLink(janela.Link, janela.CopiarLink);
        janela.CopiarLink2.Click += (_, _) => CopiarLink(janela.Link2, janela.CopiarLink2);
        dicas.SetToolTip(janela.Link2, L.T(
            "Este PC está em duas redes. Use este link se o celular estiver nesta outra rede.",
            "This PC is on two networks. Use this link if the phone is on this other network."));
        janela.Pausar.Click += (_, _) => ClicouPausar();
        dicas.SetToolTip(janela.Pausar, L.T(
            "Para de contar por um tempo, mesmo com o PC em uso.",
            "Stops counting for a while, even while the PC is in use."));
        NetworkChange.NetworkAddressChanged += (_, _) => redeMudou = true;

        // Se o painel estava ligado, sobe junto (sem UAC: a regra de firewall já foi criada antes).
        if (apiLigada && !AbrirApi())
        {
            apiLigada = false;
            janela.Celular.Checked = false;
        }

        if (janela.IniciarComWindows.Checked)
        {
            try { DefinirInicio(true); } catch { }   // garante que a entrada aponte para este .exe, com /inicio
        }

        SystemEvents.SessionEnding += (_, _) => Salvar();

        // A contagem roda numa thread própria, então a janela nunca a trava.
        threadContagem = new Thread(Contar) { IsBackground = true };
        threadContagem.Start();
        new Thread(OuvirPedidoDeAbertura) { IsBackground = true }.Start();

        AtalhoGlobal.Instalar(() => atalhoPendente = true, () => infoPendente = true);

        timerAtalho.Tick += (_, _) =>
        {
            if (atalhoPendente)
            {
                atalhoPendente = false;
                AlternarJanela();
            }
            if (infoPendente)
            {
                infoPendente = false;
                MostrarInfo();
            }
            if (redeMudou)
            {
                redeMudou = false;
                AtualizarLink();
            }
            if (pedidoAbertura)
            {
                pedidoAbertura = false;
                if (janela.Visible) janela.Activate();
                else if (mini.Visible) mini.Activate();
                else MostrarMini();
            }
        };
        timerAtalho.Start();

        timerTela.Tick += (_, _) => AtualizarTela();
        timerTela.Start();
        AtualizarTela();
        AtualizarLink();
    }

    // O exe aberto de novo (duplo clique) avisa esta instância para mostrar a janela.
    void OuvirPedidoDeAbertura()
    {
        while (rodando)
        {
            try { if (pedido.WaitOne(500)) pedidoAbertura = true; }
            catch { return; }
        }
    }

    void Contar()
    {
        var relogio = Stopwatch.StartNew();
        long anterior = relogio.ElapsedMilliseconds;
        int ciclos = 0;

        while (rodando)
        {
            Thread.Sleep(1000);
            if (!rodando) break;   // não começa mais um ciclo durante o fechamento

            long agora = relogio.ElapsedMilliseconds;
            long delta = Math.Min(agora - anterior, 2000); // ignora saltos (suspensão/hibernação)
            anterior = agora;

            // O app da frente decide a tolerância: vídeo e reunião aceitam ficar mais tempo sem mexer.
            var (item, tolerancia) = ItemAtual();
            itemAgora = item;
            int limite = tolerancia > 0 ? tolerancia : LIMITE_SEGUNDOS;

            bool pausado = Pausado();
            ativo = !pausado && SegundosSemEntrada() < limite;
            if (!ativo && !pausado)
            {
                // Ligado e sem ninguém usando: vai para o "parado" do dia.
                lock (trava)
                {
                    string chave = Dia(DateTime.Now);
                    if (!dias.TryGetValue(chave, out var reg))
                        dias[chave] = reg = new RegistroDia();
                    reg.MsParado += delta;
                }
            }
            if (ativo)
            {
                var agoraCivil = DateTime.Now;
                lock (trava)
                {
                    string chave = Dia(agoraCivil);
                    if (!dias.TryGetValue(chave, out var reg))
                        dias[chave] = reg = new RegistroDia();

                    if (reg.Ms == 0 && reg.Limite == 0) reg.Limite = LIMITE_SEGUNDOS;
                    else if (reg.Limite != LIMITE_SEGUNDOS) reg.Limite = -1; // dia com regras diferentes

                    reg.Ms += delta;
                    reg.Primeira ??= agoraCivil.TimeOfDay;
                    reg.Ultima = agoraCivil.TimeOfDay;

                    if (!apps.TryGetValue(chave, out var porItem))
                        apps[chave] = porItem = new Dictionary<string, long>();
                    porItem[item] = porItem.GetValueOrDefault(item) + delta;

                    if (!horas.TryGetValue(chave, out var porHora))
                        horas[chave] = porHora = new long[24];
                    porHora[agoraCivil.Hour] += delta;
                }
            }

            if (++ciclos % 5 == 0) RecarregarRegrasSeMudou();
            if (ciclos % 10 == 0) Salvar();
        }
    }

    // Descobre qual item da lista está em uso agora (janela em primeiro plano) e a tolerância dele.
    (string Nome, int Tolerancia) ItemAtual()
    {
        try
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return (OUTROS, 0);

            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            string titulo = sb.ToString();

            string processo = "";
            GetWindowThreadProcessId(h, out uint pid);

            // A própria janela do Time: entra na lista como "Time", para dar
            // para ver no painel quando você está com ele aberto.
            if ((int)pid == Environment.ProcessId) return ("Time", 0);

            try { using var p = Process.GetProcessById((int)pid); processo = p.ProcessName; } catch { }

            List<Regra> lista;
            lock (trava) lista = regras;

            bool navegador = NAVEGADORES.Any(n => processo.Equals(n, StringComparison.OrdinalIgnoreCase));

            foreach (var r in lista)
            {
                bool bate = r.Tipo switch
                {
                    "site" => navegador && titulo.Contains(r.Texto, StringComparison.OrdinalIgnoreCase),
                    "titulo" => titulo.Contains(r.Texto, StringComparison.OrdinalIgnoreCase),
                    _ => processo.StartsWith(r.Texto, StringComparison.OrdinalIgnoreCase)
                };
                if (bate) return (r.Nome, r.Tolerancia);
            }
        }
        catch { }
        return (OUTROS, 0);
    }

    void AlternarJanela()
    {
        // Se alguma está à vista, o atalho esconde. Se está tudo escondido, abre a mínima.
        if (mini.Visible) { mini.Hide(); return; }
        if (janela.Visible) { janela.Hide(); return; }
        MostrarMini();
    }

    // Avisos e preparos que valem para qualquer uma das janelas.
    void PrepararAbertura()
    {
        if (pastaAlternativa && !avisouPasta)
        {
            avisouPasta = true;
            MessageBox.Show(
                L.T($"Não foi possível gravar em:\n{pastaFixa}\n\n" +
                    $"Os dados estão sendo salvos em:\n{pasta}\n\n" +
                    "Para voltar à pasta correta, feche o Time e abra o Time.exe como administrador uma vez.",
                    $"Could not write to:\n{pastaFixa}\n\n" +
                    $"The data is being saved to:\n{pasta}\n\n" +
                    "To go back to the right folder, close Time and run Time.exe as administrator once."),
                "Time", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Primeira vez que a janela abre com o painel ligado: garante a liberação no firewall.
        if (apiLigada && !tentouFirewall)
        {
            tentouFirewall = true;
            LiberarFirewallSePreciso();
        }
        AtualizarLink();
        AtualizarTela();
    }

    // Abre só o nome e o botão de minimizar.
    void MostrarMini()
    {
        PrepararAbertura();
        janela.Hide();
        var area = Screen.PrimaryScreen!.WorkingArea;
        mini.Location = new Point(area.Right - mini.Width - 12, area.Bottom - mini.Height - 12);
        mini.Show();
        mini.Activate();
        mini.BringToFront();
    }

    // Abre a janela completa (atalho Ctrl+Shift+Alt+F2 e depois 1).
    void MostrarInfo()
    {
        PrepararAbertura();
        mini.Hide();
        var area = Screen.PrimaryScreen!.WorkingArea;
        janela.WindowState = FormWindowState.Normal;
        janela.Location = new Point(area.Right - janela.Width - 12, area.Bottom - janela.Height - 12);
        janela.Show();
        janela.Activate();
        janela.BringToFront();
    }

    void AbrirPastaDados()
    {
        try
        {
            if (File.Exists(arquivo)) Process.Start("explorer.exe", $"/select,\"{arquivo}\"");
            else Process.Start("explorer.exe", $"\"{pasta}\"");
        }
        catch { }
    }

    void EditarCategorias()
    {
        try
        {
            if (!File.Exists(arquivoRegras))
                File.WriteAllText(arquivoRegras, RegrasPadrao, new UTF8Encoding(true));
            Process.Start("notepad.exe", $"\"{arquivoRegras}\"");
        }
        catch { }
    }

    void AtualizarTela()
    {
        string textoHoje, periodo, historico, progNomes, progTempos, foco, parado;
        lock (trava)
        {
            string chaveHoje = Dia(DateTime.Now);
            dias.TryGetValue(chaveHoje, out var hoje);

            textoHoje = FormatarSeg((hoje?.Ms ?? 0) / 1000);
            long msParado = hoje?.MsParado ?? 0;
            parado = msParado >= 60000
                ? L.T($"Ligado sem uso: {Formatar(msParado / 1000)}", $"On but idle: {Formatar(msParado / 1000)}")
                : "";
            periodo = hoje?.Primeira != null
                ? L.T($"Início {Hm(hoje.Primeira)}  ·  Última {Hm(hoje.Ultima)}",
                      $"Start {Hm(hoje.Primeira)}  ·  Last {Hm(hoje.Ultima)}")
                : L.T("Sem atividade hoje", "No activity today");

            var top = apps.TryGetValue(chaveHoje, out var porItem)
                ? porItem.OrderByDescending(x => x.Value).Take(5).ToList()
                : new List<KeyValuePair<string, long>>();
            progNomes = top.Any() ? string.Join("\n", top.Select(x => x.Key)) : L.T("(sem dados ainda)", "(no data yet)");
            progTempos = string.Join("\n", top.Select(x => FormatarSeg(x.Value / 1000)));

            // Foco: soma do dia pelos grupos marcados como produtivo ou distração.
            long produtivo = 0, distracao = 0;
            if (porItem != null)
                foreach (var x in porItem)
                {
                    string tipo = FocoDoItem(x.Key);
                    if (tipo == FOCO_PRODUTIVO) produtivo += x.Value;
                    else if (tipo == FOCO_DISTRACAO) distracao += x.Value;
                }
            foco = focos.Values.Any(f => f != FOCO_NEUTRO)
                ? L.T($"Produtivo {Formatar(produtivo / 1000)}  ·  Distração {Formatar(distracao / 1000)}",
                      $"Productive {Formatar(produtivo / 1000)}  ·  Distraction {Formatar(distracao / 1000)}")
                : "";

            var anteriores = dias
                .Where(t => t.Key != chaveHoje && t.Value.Ms > 0)
                .OrderByDescending(t => t.Key)
                .Take(5)
                .Select(t => $"{DataCurta(t.Key)}   {Formatar(t.Value.Ms / 1000)}   ({Hm(t.Value.Primeira)}–{Hm(t.Value.Ultima)})");
            historico = anteriores.Any() ? string.Join("\n", anteriores) : L.T("(sem dados ainda)", "(no data yet)");
        }

        if (janela.Visible)
        {
            janela.Hoje.Text = textoHoje;
            if (Pausado())
            {
                string fim = FimDaPausa();
                janela.Status.Text = fim.Length > 0
                    ? L.T($"❚❚ Pausado até {fim}", $"❚❚ Paused until {fim}")
                    : L.T("❚❚ Pausado", "❚❚ Paused");
                janela.Status.ForeColor = Color.DarkOrange;
                janela.Pausar.Text = L.T("Retomar", "Resume");
            }
            else
            {
                janela.Status.Text = ativo ? L.T("● Contando", "● Counting") : L.T("○ Sem uso agora", "○ Idle");
                janela.Status.ForeColor = ativo ? Color.SeaGreen : Color.Gray;
                janela.Pausar.Text = L.T("Pausar", "Pause");
            }
            janela.Periodo.Text = periodo;
            janela.Foco.Text = foco;
            janela.Parado.Text = parado;
            janela.ProgNomes.Text = progNomes;
            janela.ProgTempos.Text = progTempos;
            janela.Historico.Text = historico;
        }
    }

    static string Dia(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // "26/10" em português, "10/26" em inglês.
    static string DataCurta(string chave) =>
        DateTime.ParseExact(chave, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .ToString(L.T("dd/MM", "MM/dd"), CultureInfo.InvariantCulture);

    static string Hm(TimeSpan? t) => t?.ToString(@"hh\:mm") ?? "--:--";

    static string Formatar(long s) => $"{s / 3600}h {s % 3600 / 60}min";

    static string FormatarSeg(long s) => $"{s / 3600}h {s % 3600 / 60}min {s % 60}s";

    static string Hms(long s) => $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}";

    static string LimiteTexto(int limite) => limite > 0 ? limite.ToString() : limite < 0 ? L.T("misto", "mixed") : "";

    void Sair()
    {
        if (!rodando) return;
        rodando = false;
        timerTela.Stop();
        timerAtalho.Stop();
        AtalhoGlobal.Remover();
        FecharApi();

        // Espera o ciclo de contagem terminar, para não gravar os arquivos ao mesmo tempo.
        if (threadContagem.IsAlive) threadContagem.Join(3000);

        Salvar();
        try { pedido.Close(); } catch { }
        ExitThread();
    }

    // ---------- Iniciar com o Windows ----------

    const string CHAVE_RUN = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string NOME_APP = "Time";

    static bool IniciaComWindows()
    {
        using var k = Registry.CurrentUser.OpenSubKey(CHAVE_RUN);
        return k?.GetValue(NOME_APP) != null;
    }

    static void DefinirInicio(bool ligar)
    {
        using var k = Registry.CurrentUser.CreateSubKey(CHAVE_RUN);
        if (ligar)
        {
            k.SetValue(NOME_APP, $"\"{Environment.ProcessPath}\" /inicio");
        }
        else
        {
            k.DeleteValue(NOME_APP, false);
        }
    }

    // ---------- Windows API ----------

    static double SegundosSemEntrada()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return double.MaxValue;
        uint ociosoMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return ociosoMs / 1000.0;
    }

    [DllImport("user32.dll")]
    static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder texto, int max);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }
}
