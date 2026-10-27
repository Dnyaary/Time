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

// Time — contador de tempo de uso real do PC.
// Roda sem ícone na bandeja: a janela só aparece pelo atalho global
// (segure Ctrl+Shift+Alt, aperte X e depois 5); o mesmo atalho a esconde.
// O X da janela encerra o programa de verdade.
// Soma o tempo enquanto houve mouse/teclado nos últimos LIMITE_SEGUNDOS
// e separa esse tempo por programa/site, conforme o categorias.txt da pasta de dados.
// O título da janela é usado só para comparar com as regras; ele NUNCA é salvo.
// O painel do celular (Api.cs) mostra os mesmos dados para quem estiver na
// mesma rede, com um código de acesso; ele é ligado pela própria janela.

// Idioma da interface: português quando o Windows está em português; inglês nos demais.
static class L
{
    public static readonly bool Pt =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase);

    public static string T(string pt, string en) => Pt ? pt : en;
}

static class Program
{
    internal const string MUTEX = "Time_Unico";
    internal const string PEDIDO = "Time_MostrarJanela";

    // true quando o Windows abriu o programa pela chave de inicialização.
    // Nesse caso não pedimos UAC: quem instala vê o pedido uma vez na primeira execução.
    internal static bool IniciadoPeloWindows { get; private set; }

    // O programa mora sempre aqui, junto dos dados.
    internal static string PastaInstalada
    {
        get
        {
            string programas = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrWhiteSpace(programas)) programas = @"C:\Program Files";
            return Path.Combine(programas, "Time");
        }
    }

    internal static string CaminhoInstalado => Path.Combine(PastaInstalada, "Time.exe");

    [STAThread]
    static void Main(string[] args)
    {
        IniciadoPeloWindows = args.Any(a => a.Equals("/inicio", StringComparison.OrdinalIgnoreCase));

        // /troca: a cópia anterior está saindo agora (auto-instalação); espera ela
        // soltar o mutex, em vez de pedir para ela abrir a janela.
        bool troca = args.Any(a => a.Equals("/troca", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(false, MUTEX);
        bool dono;
        try { dono = mutex.WaitOne(troca ? TimeSpan.FromSeconds(8) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { dono = true; }

        // Já existe um Time aberto: pede para ele mostrar a janela e sai.
        // Serve de reserva caso o atalho não funcione em algum PC.
        if (!dono)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(PEDIDO, out var pedido))
                {
                    pedido.Set();
                    pedido.Dispose();
                }
            }
            catch { }
            return;
        }

        // Aberto de outro lugar (Downloads, pen drive, área de trabalho): copia
        // para a pasta fixa e reinicia de lá. Assim a pasta de origem pode ser
        // apagada sem quebrar a inicialização automática.
        if (!troca && InstalarSePreciso()) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new Contador());
    }

    // Copia este .exe para C:\Program Files\Time e sobe a cópia instalada.
    // Devolve true quando a cópia foi iniciada (quem chamou deve sair).
    static bool InstalarSePreciso()
    {
        try
        {
            string? atual = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(atual)) return false;
            if (MesmoCaminho(atual, CaminhoInstalado)) return false;

            // Só o .exe de arquivo único (dotnet publish -p:PublishSingleFile=true)
            // pode ser copiado sozinho. No build normal (dotnet build / Visual
            // Studio) o Time.exe depende do Time.dll e de outros arquivos ao
            // lado; copiar só o .exe deixava em Program Files uma cópia que
            // fechava na hora. Nesse caso roda de onde está, sem se instalar.
            if (!string.IsNullOrEmpty(typeof(Program).Assembly.Location)) return false;

            // Garante a pasta dos dados (e o UAC único) antes de copiar.
            _ = Contador.PastaDeDados;

            // UAC negado: está rodando de %LOCALAPPDATA%; não insiste.
            if (Contador.PastaAlternativa) return false;

            Directory.CreateDirectory(PastaInstalada);
            File.Copy(atual, CaminhoInstalado, overwrite: true);

            AjustarInicioAutomatico();

            var inicio = new ProcessStartInfo(CaminhoInstalado)
            {
                UseShellExecute = false,
                WorkingDirectory = PastaInstalada
            };
            if (IniciadoPeloWindows) inicio.ArgumentList.Add("/inicio");
            inicio.ArgumentList.Add("/troca");

            Process.Start(inicio);
            return true;
        }
        catch { return false; }
    }

    static bool MesmoCaminho(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    // Se "Iniciar com o Windows" já estava marcado, aponta para o caminho instalado.
    static void AjustarInicioAutomatico()
    {
        try
        {
            using var chave = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (chave?.GetValue("Time") is not string valor) return;

            string novo = $"\"{CaminhoInstalado}\" /inicio";
            if (!string.Equals(valor, novo, StringComparison.OrdinalIgnoreCase))
                chave.SetValue("Time", novo);
        }
        catch { }
    }
}

// Atalho global em dois tempos: segure Ctrl+Shift+Alt, aperte X e, em até 2 s, aperte 5.
// As teclas continuam funcionando nos outros programas; só o 5 do atalho é consumido.
static class AtalhoGlobal
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100;
    const int WM_SYSKEYDOWN = 0x0104;
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
    const int VK_LMENU = 0xA4, VK_RMENU = 0xA5;
    const int VK_X = 0x58, VK_5 = 0x35, VK_NUMPAD5 = 0x65;
    const int VK_F2 = 0x71, VK_1 = 0x31, VK_NUMPAD1 = 0x61;
    static readonly TimeSpan ESPERA = TimeSpan.FromSeconds(4);

    static IntPtr gancho;
    static HOOKPROC? retorno;   // mantém o delegate vivo: se ele for coletado, o Windows chama memória inválida
    static DateTime xEm;
    static DateTime f2Em;
    static Action? disparado;
    static Action? disparadoInfo;

    public static void Instalar(Action aoDisparar, Action aoPedirInfo)
    {
        disparado = aoDisparar;
        disparadoInfo = aoPedirInfo;
        retorno = AoTeclar;
        gancho = SetWindowsHookEx(WH_KEYBOARD_LL, retorno, IntPtr.Zero, 0);
    }

    public static void Remover()
    {
        if (gancho != IntPtr.Zero) UnhookWindowsHookEx(gancho);
        gancho = IntPtr.Zero;
        retorno = null;
        disparado = null;
        disparadoInfo = null;
    }

    // Atalhos de dois tempos: segure Ctrl+Shift+Alt e aperte a primeira tecla;
    // depois (até 4 s) aperte a segunda. Soltar Ctrl/Shift/Alt entre as duas não
    // atrapalha: o que vale é a primeira tecla ter vindo com os três. Qualquer
    // outra tecla cancela a sequência.
    static IntPtr AoTeclar(int codigo, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (codigo >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int tecla = Marshal.ReadInt32(lParam);

                if (!EhModificador(tecla))
                {
                    bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                    bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                    bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                    bool comOsTres = ctrl && shift && alt;
                    DateTime agora = DateTime.UtcNow;

                    if (comOsTres && tecla == VK_X)
                    {
                        xEm = agora;                         // 1º tempo: X
                    }
                    else if (comOsTres && tecla == VK_F2)
                    {
                        f2Em = agora;                        // 1º tempo: F2
                    }
                    else if ((tecla == VK_5 || tecla == VK_NUMPAD5) && xEm != default && agora - xEm <= ESPERA)
                    {
                        xEm = f2Em = default;
                        disparado?.Invoke();                 // X e depois 5: janela mínima
                        return (IntPtr)1;                    // consome o 5 do atalho
                    }
                    else if ((tecla == VK_1 || tecla == VK_NUMPAD1) && f2Em != default && agora - f2Em <= ESPERA)
                    {
                        xEm = f2Em = default;
                        disparadoInfo?.Invoke();             // F2 e depois 1: informações
                        return (IntPtr)1;                    // consome o 1 do atalho
                    }
                    else
                    {
                        xEm = f2Em = default;                // outra tecla: esquece a sequência
                    }
                }
            }
        }
        catch { }
        return CallNextHookEx(gancho, codigo, wParam, lParam);
    }

    static bool EhModificador(int tecla) =>
        tecla == VK_SHIFT || tecla == VK_LSHIFT || tecla == VK_RSHIFT ||
        tecla == VK_CONTROL || tecla == VK_LCONTROL || tecla == VK_RCONTROL ||
        tecla == VK_MENU || tecla == VK_LMENU || tecla == VK_RMENU;

    delegate IntPtr HOOKPROC(int codigo, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HOOKPROC lpfn, IntPtr hMod, uint idThread);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int tecla);
}

class RegistroDia
{
    public long Ms;                 // tempo ativo em milissegundos
    public TimeSpan? Primeira;      // hora da primeira atividade do dia
    public TimeSpan? Ultima;        // hora da última atividade do dia
    public int Limite;              // limite de inatividade (s) usado; 0 = desconhecido, -1 = dia medido com limites diferentes
}

// Tipo: "site" (título, só em navegador), "titulo" (título, qualquer janela) ou "programa" (nome do processo).
// Tolerancia: segundos sem mexer que ainda contam para esse item (0 = usa o padrão LIMITE_SEGUNDOS).
// Grupo: o [Grupo] do categorias.txt em que a regra está ("" = sem grupo).
record Regra(string Tipo, string Texto, string Nome, int Tolerancia, string Grupo);

class Janela : Form
{
    public readonly Label Hoje = new();
    public readonly Label Status = new();
    public readonly Label Periodo = new();
    public readonly Label ProgNomes = new();
    public readonly Label ProgTempos = new();
    public readonly Label Historico = new();
    public readonly CheckBox IniciarComWindows;
    public readonly CheckBox Celular;
    public readonly TextBox Link;
    public readonly Button CopiarLink;
    public readonly Button AbrirPasta;
    public readonly Button EditarCategorias;

    public event Action? Fechou;   // disparado quando o X fecha a janela

    public Janela()
    {
        Text = "Time";
        Icon = SystemIcons.Application;    // ícone genérico do Windows (sem marca própria)
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;              // quem minimiza é o atalho
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;             // só enquanto a janela está aberta
        TopMost = true;
        ClientSize = new Size(300, 512);
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.White;

        var titulo = new Label { Text = L.T("Hoje", "Today"), Left = 16, Top = 12, AutoSize = true, ForeColor = Color.Gray };
        Hoje.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
        Hoje.Left = 12; Hoje.Top = 30; Hoje.AutoSize = true;
        Status.Left = 16; Status.Top = 78; Status.AutoSize = true;
        Periodo.Left = 16; Periodo.Top = 98; Periodo.AutoSize = true; Periodo.ForeColor = Color.Gray;

        var tituloProg = new Label { Text = L.T("Hoje por programa", "Today by app"), Left = 16, Top = 126, AutoSize = true, ForeColor = Color.Gray };
        ProgNomes.Left = 16; ProgNomes.Top = 146;
        ProgNomes.AutoSize = false; ProgNomes.Width = 145; ProgNomes.Height = 80;
        ProgNomes.AutoEllipsis = true;
        ProgTempos.Left = 170; ProgTempos.Top = 146; ProgTempos.AutoSize = true;

        var tituloHist = new Label { Text = L.T("Últimos dias", "Previous days"), Left = 16, Top = 238, AutoSize = true, ForeColor = Color.Gray };
        Historico.Left = 16; Historico.Top = 258; Historico.AutoSize = true;

        var linha = new Panel { Left = 16, Top = 344, Width = 268, Height = 1, BackColor = Color.Gainsboro };

        IniciarComWindows = new CheckBox
        {
            Text = L.T("Iniciar com o Windows", "Start with Windows"), Left = 14, Top = 352,
            Width = 270, Height = 22, AutoSize = false
        };

        AbrirPasta = new Button { Text = L.T("Pasta dos dados", "Data folder"), Left = 16, Top = 376, Width = 130, Height = 26 };
        EditarCategorias = new Button { Text = L.T("Categorias", "Categories"), Left = 154, Top = 376, Width = 130, Height = 26 };

        Celular = new CheckBox
        {
            Text = L.T("Acessar pelo celular (rede local)", "Phone access (local network)"), Left = 14, Top = 408,
            Width = 270, Height = 22, AutoSize = false
        };

        Link = new TextBox
        {
            Left = 16, Top = 434, Width = 190, Height = 22,
            ReadOnly = true, Visible = false,
            Font = new Font("Segoe UI", 7f)
        };

        CopiarLink = new Button
        {
            Text = L.T("Copiar", "Copy"), Left = 212, Top = 432, Width = 72, Height = 24,
            Visible = false, Font = new Font("Segoe UI", 8f)
        };

        var atalho = new Label
        {
            Text = L.T("Ctrl+Shift+Alt+X e depois 5 - abre e fecha a janela.\nCtrl+Shift+Alt+F2 e depois 1 - mostra estas informações.",
                       "Ctrl+Shift+Alt+X, then 5 - opens and hides the window.\nCtrl+Shift+Alt+F2, then 1 - shows this information."),
            Left = 16, Top = 458, Width = 268, Height = 30,
            ForeColor = Color.Gray, Font = new Font("Segoe UI", 7.5f)
        };

        var credito = new Label
        {
            Text = "by Daniel 'dnyaary' Carvalho", Left = 16, Top = 492, AutoSize = true,
            ForeColor = Color.Silver, Font = new Font("Segoe UI", 7.5f)
        };

        Controls.AddRange(new Control[]
        {
            titulo, Hoje, Status, Periodo, tituloProg, ProgNomes, ProgTempos, tituloHist, Historico,
            linha, IniciarComWindows, AbrirPasta, EditarCategorias, Celular, Link, CopiarLink, atalho, credito
        });
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        Fechou?.Invoke();
    }
}

// Janela mínima: só o nome "Time" e o botão de minimizar, que esconde de novo.
// Sem barra de título do Windows, então não tem fechar nem restaurar; arrasta
// segurando o fundo. O atalho F2+1 troca por esta janela as informações completas.
class JanelaMini : Form
{
    const int WM_NCLBUTTONDOWN = 0xA1;
    const int HTCAPTION = 0x2;

    public readonly Button Minimizar = new();

    public JanelaMini()
    {
        Text = "Time";
        Icon = SystemIcons.Application;
        FormBorderStyle = FormBorderStyle.None;   // sem barra de título: sem fechar/restaurar
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        TopMost = true;
        ClientSize = new Size(280, 80);
        BackColor = Color.White;
        DoubleBuffered = true;

        Minimizar.Text = "—";
        Minimizar.FlatStyle = FlatStyle.Flat;
        Minimizar.FlatAppearance.BorderSize = 0;
        Minimizar.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 235, 235);
        Minimizar.BackColor = Color.White;
        Minimizar.ForeColor = Color.FromArgb(90, 90, 90);
        Minimizar.Font = new Font("Segoe UI", 12f);
        Minimizar.Cursor = Cursors.Hand;
        Minimizar.TabStop = false;
        Minimizar.SetBounds(ClientSize.Width - 48, 26, 34, 28);
        Controls.Add(Minimizar);

        MouseDown += Arrastar;
    }

    // Alt+F4 e o menu da barra de tarefas apenas escondem: fechar de verdade
    // é pelo X da janela completa.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var borda = new Pen(Color.Gainsboro))
            e.Graphics.DrawRectangle(borda, 0, 0, Width - 1, Height - 1);

        using var fonte = new Font("Segoe UI", 11f, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, "Time", fonte,
            new Rectangle(18, 0, ClientSize.Width - 70, ClientSize.Height),
            Color.FromArgb(45, 48, 53),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    void Arrastar(object? remetente, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}

// Um outro PC que aparece no painel (lido do pcs.txt).
// Maquina é o nome que o outro PC anuncia na rede (identidade); Nome é o rótulo
// escolhido por você. Endereco é o último IP conhecido — a descoberta atualiza.
sealed record Remoto(string Nome, string Codigo, string Maquina, Uri Endereco);

// O que o outro PC respondeu em /api/tudo, já no formato que o painel usa.
sealed class RemotoDados
{
    public required Remoto Remoto { get; init; }
    public bool Online { get; init; }
    public string Usuario { get; init; } = "";
    public long Segundos { get; init; }
    public string Inicio { get; init; } = "--:--";
    public string Ultima { get; init; } = "--:--";
    public bool Contando { get; init; }
    public string Atual { get; init; } = "";
    public Dictionary<string, long> Apps { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> Dias { get; init; } = new();
    public long[] Horas { get; init; } = new long[24];   // segundos em cada hora de hoje
}

class Contador : ApplicationContext
{
    const int LIMITE_SEGUNDOS = 30;
    static readonly string CABECALHO = L.T(
        "Data;Usuario;Maquina;Primeira atividade;Ultima atividade;Tempo ativo;Segundos;Limite inatividade (s)",
        "Date;User;Machine;First activity;Last activity;Active time;Seconds;Idle limit (s)");
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
        janela.CopiarLink.Click += (_, _) => CopiarLink();

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

            ativo = SegundosSemEntrada() < limite;
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

        string? ip = IpLocal();
        return ip != null && host == ip.ToLowerInvariant();
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
                                       JsonDescobertos, AdicionarPc, RemoverPc, Csv, JsonApp, JsonGrupos);
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
        janela.Link.Visible = ligado;
        janela.CopiarLink.Visible = ligado;
        janela.Link.Text = ligado ? LinkCelular() : "";
    }

    string LinkCelular() => $"http://{IpLocal() ?? "localhost"}:{portaApi}/?k={codigoApi}";

    void CopiarLink()
    {
        try
        {
            Clipboard.SetText(janela.Link.Text);
            janela.CopiarLink.Text = L.T("Copiado!", "Copied!");
            var volta = new System.Windows.Forms.Timer { Interval = 1500 };
            volta.Tick += (_, _) =>
            {
                volta.Stop();
                volta.Dispose();
                janela.CopiarLink.Text = L.T("Copiar", "Copy");
            };
            volta.Start();
        }
        catch { }
    }

    // Primeiro IPv4 de uma placa de rede ativa: é o endereço que o celular alcança.
    static string? IpLocal()
    {
        try
        {
            foreach (var placa in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (placa.OperationalStatus != OperationalStatus.Up) continue;
                if (placa.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                foreach (var endereco in placa.GetIPProperties().UnicastAddresses)
                {
                    if (endereco.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = endereco.Address.ToString();
                    if (ip.StartsWith("169.254.")) continue;   // endereço automático: rede sem DHCP
                    return ip;
                }
            }
        }
        catch { }
        return null;
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
            dados.AddRange(tarefas.Select(t => t.Result));
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

    static RemotoDados BuscarRemoto(Remoto remoto)
    {
        try
        {
            var alvo = new UriBuilder(remoto.Endereco)
            {
                Path = "/api/tudo",
                Query = "n=70&k=" + Uri.EscapeDataString(remoto.Codigo)
            };

            string corpo = http.GetStringAsync(alvo.Uri).GetAwaiter().GetResult();
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
                Usuario = resumo.GetProperty("usuario").GetString() ?? "",
                Segundos = resumo.GetProperty("segundos").GetInt64(),
                Inicio = resumo.GetProperty("inicio").GetString() ?? "--:--",
                Ultima = resumo.GetProperty("ultima").GetString() ?? "--:--",
                Contando = resumo.GetProperty("contando").GetBoolean(),
                Atual = resumo.GetProperty("atual").GetString() ?? "",
                Apps = apps,
                Dias = dias,
                Horas = LerHoras(resumo)
            };
        }
        catch
        {
            return new RemotoDados { Remoto = remoto, Online = false };
        }
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

    string JsonResumo() => JsonResumo(comRemotos: true);

    string JsonResumoLocal() => JsonResumo(comRemotos: false);

    string JsonResumo(bool comRemotos)
    {
        string chave = Dia(DateTime.Now);
        long segundos;
        string inicio, ultima;
        long[] porHora;
        var itens = new List<KeyValuePair<string, long>>();

        lock (trava)
        {
            porHora = HorasDoDia(chave);
            dias.TryGetValue(chave, out var hoje);
            segundos = (hoje?.Ms ?? 0) / 1000;
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
                online = r.Online,
                este = false
            });

            if (!r.Online) continue;

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
            inicio,
            ultima,
            contando = ativo,
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
                segundosPorDia[t.Key] = t.Value.Ms / 1000;
                faixas[t.Key] = (Hm(t.Value.Primeira), Hm(t.Value.Ultima));
            }
        }

        if (comRemotos)
        {
            foreach (var r in ConsultarRemotos())
            {
                if (!r.Online) continue;

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
        if (!dados.Online) return "{\"erro\":\"offline\"}";

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

        long segundos = 0;
        string inicio = "--:--", ultima = "--:--";
        var itens = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long[] porHora;

        lock (trava)
        {
            porHora = HorasDoDia(data);
            if (dias.TryGetValue(data, out var reg))
            {
                segundos = reg.Ms / 1000;
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
                if (!r.Online) continue;

                string? corpo = BuscarDiaRemoto(r.Remoto, data);
                if (corpo == null) continue;

                try
                {
                    using var doc = JsonDocument.Parse(corpo);
                    var raiz = doc.RootElement;

                    if (raiz.TryGetProperty("erro", out _)) continue;

                    segundos += raiz.GetProperty("segundos").GetInt64();
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

    // /api/grupos: nome do programa/site -> grupo, conforme o categorias.txt deste PC.
    // O painel usa para somar o tempo de cada grupo.
    string JsonGrupos()
    {
        Dictionary<string, string> mapa;
        lock (trava) mapa = grupos;
        return JsonSerializer.Serialize(new { mapa });
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

    void AtualizarTela()
    {
        string textoHoje, periodo, historico, progNomes, progTempos;
        lock (trava)
        {
            string chaveHoje = Dia(DateTime.Now);
            dias.TryGetValue(chaveHoje, out var hoje);

            textoHoje = FormatarSeg((hoje?.Ms ?? 0) / 1000);
            periodo = hoje?.Primeira != null
                ? L.T($"Início {Hm(hoje.Primeira)}  ·  Última {Hm(hoje.Ultima)}",
                      $"Start {Hm(hoje.Primeira)}  ·  Last {Hm(hoje.Ultima)}")
                : L.T("Sem atividade hoje", "No activity today");

            var top = apps.TryGetValue(chaveHoje, out var porItem)
                ? porItem.OrderByDescending(x => x.Value).Take(5).ToList()
                : new List<KeyValuePair<string, long>>();
            progNomes = top.Any() ? string.Join("\n", top.Select(x => x.Key)) : L.T("(sem dados ainda)", "(no data yet)");
            progTempos = string.Join("\n", top.Select(x => FormatarSeg(x.Value / 1000)));

            var anteriores = dias
                .Where(t => t.Key != chaveHoje)
                .OrderByDescending(t => t.Key)
                .Take(5)
                .Select(t => $"{DataCurta(t.Key)}   {Formatar(t.Value.Ms / 1000)}   ({Hm(t.Value.Primeira)}–{Hm(t.Value.Ultima)})");
            historico = anteriores.Any() ? string.Join("\n", anteriores) : L.T("(sem dados ainda)", "(no data yet)");
        }

        if (janela.Visible)
        {
            janela.Hoje.Text = textoHoje;
            janela.Status.Text = ativo ? L.T("● Contando", "● Counting") : L.T("○ Pausado (sem uso)", "○ Paused (idle)");
            janela.Status.ForeColor = ativo ? Color.SeaGreen : Color.Gray;
            janela.Periodo.Text = periodo;
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

    // ---------- Regras (categorias.txt) ----------

    static readonly string[] NAVEGADORES = { "chrome", "msedge", "firefox", "opera", "brave", "vivaldi" };

    // categorias.txt criado na primeira execução, no idioma do Windows.
    static string RegrasPadrao => L.Pt ? REGRAS_PT : REGRAS_EN;

    const string REGRAS_PT =
@"# Categorias do Time
# Uma regra por linha, no formato:   tipo: texto = Nome que aparece
#
#   site:     procura o texto no título da aba, SÓ quando a janela é um navegador
#             (Chrome, Edge, Firefox, Opera, Brave, Vivaldi)
#   titulo:   procura o texto no título de QUALQUER janela (use com cuidado)
#   programa: compara com o começo do nome do programa, sem .exe
#             (veja o nome em Gerenciador de Tarefas > Detalhes)
#
# Tolerância opcional no fim da linha:   Nome | segundos
#   É quanto tempo sem mexer no mouse/teclado ainda conta para aquele item.
#   Use em vídeo e reunião, onde a pessoa assiste sem tocar em nada.
#   Sem o | usa o padrão do programa (30 s).
#   Ex.: site: YouTube = YouTube | 600   (continua contando até 10 min parado)
#
# Grupos (opcional): uma linha [Nome do grupo] coloca as regras de baixo nesse
#   grupo, até o próximo [ ]. Uma linha [] volta para sem grupo. O painel mostra
#   quanto tempo foi para cada grupo. Pode criar, renomear e trocar à vontade.
#
# A primeira regra que bater vence: os navegadores ficam no fim, depois dos sites.
# O que não bater em nenhuma regra conta como ""Outros"".
# O título da janela nunca é salvo, só o Nome da regra.
# Depois de salvar este arquivo, o contador recarrega sozinho em alguns segundos.

[Redes sociais]
site: Instagram = Instagram
site: Facebook = Facebook
site: Messenger = Facebook
site: TikTok = TikTok | 180
site: / X = X (Twitter)
site:  no X: = X (Twitter)
site:  on X: = X (Twitter)
site: Twitter = X (Twitter)
site: Threads = Threads
site: LinkedIn = LinkedIn
site: Pinterest = Pinterest
site: Reddit = Reddit
site: Kwai = Kwai | 180

[Vídeo e música]
site: YouTube = YouTube | 600
site: Twitch = Twitch | 600
site: Netflix = Netflix | 600
site: Prime Video = Prime Video | 600
site: Disney+ = Disney+ | 600
site: Spotify = Spotify
programa: Spotify = Spotify
programa: vlc = VLC | 600
programa: Video.UI = Filmes e TV | 600
programa: Microsoft.Media.Player = Media Player | 600
programa: mpc-hc = Media Player Classic | 600

[Comunicação]
site: WhatsApp = WhatsApp
site: Gmail = Gmail
site: Outlook = Outlook
site: Meet = Google Meet | 900
site: Microsoft Teams = Teams | 900
programa: WhatsApp = WhatsApp
programa: Telegram = Telegram
programa: Discord = Discord
programa: OUTLOOK = Outlook
programa: olk = Outlook
programa: ms-teams = Teams | 900
programa: Teams = Teams | 900
programa: Zoom = Zoom | 900

[Inteligência artificial]
site: ChatGPT = ChatGPT
site: Claude = Claude
site: Gemini = Gemini
site: Grok = Grok
site: Copilot = Copilot
site: DeepSeek = DeepSeek
site: Perplexity = Perplexity
programa: ChatGPT = ChatGPT
programa: claude = Claude

[Trabalho]
site: Google Drive = Google Drive
site: Google Docs = Google Docs
site: Documentos Google = Google Docs
site: Google Sheets = Google Planilhas
site: Planilhas Google = Google Planilhas
site: Canva = Canva
site: Freepik = Freepik
site: Figma = Figma
site: Imprima = Imprima Store
programa: CorelDRW = CorelDRAW
programa: CorelPP = Corel PHOTO-PAINT
programa: Photoshop = Photoshop
programa: Illustrator = Illustrator
programa: InDesign = InDesign
programa: Adobe Premiere Pro = Premiere Pro
programa: AfterFX = After Effects
programa: Lightroom = Lightroom
programa: acad = AutoCAD
programa: SketchUp = SketchUp
programa: 3dsmax = 3ds Max
programa: Revit = Revit
programa: blender = Blender
programa: UnrealEditor = Unreal Engine
programa: Figma = Figma
programa: Inventor = Inventor
programa: Fusion360 = Fusion 360
programa: SLDWORKS = SolidWorks
programa: Rhino = Rhino
programa: ArchiCAD = Archicad
programa: Roamer = Navisworks
programa: Lumion = Lumion
programa: Twinmotion = Twinmotion
programa: Promob = Promob
programa: qgis = QGIS
programa: maya = Maya
programa: Cinema 4D = Cinema 4D
programa: ZBrush = ZBrush
programa: Unity = Unity
programa: Godot = Godot
programa: inkscape = Inkscape
programa: gimp = GIMP
programa: krita = Krita
programa: Resolve = DaVinci Resolve
programa: CapCut = CapCut
programa: obs64 = OBS Studio
programa: soffice = LibreOffice
programa: Acrobat = Adobe Acrobat
programa: AcroRd32 = Adobe Acrobat
programa: Foxit = Foxit PDF
programa: EXCEL = Excel
programa: WINWORD = Word
programa: POWERPNT = PowerPoint
programa: ONENOTE = OneNote
programa: Code = VS Code
programa: devenv = Visual Studio
programa: AnyDesk = AnyDesk
programa: notepad = Bloco de Notas

[Compras]
site: Mercado Livre = Mercado Livre
site: Shopee = Shopee
site: Amazon = Amazon
site: AliExpress = AliExpress

[Jogos]
programa: Steam = Steam

# ===== Sem grupo =====
[]
# Navegadores (o que não for um dos sites acima)
programa: chrome = Navegador
programa: msedge = Navegador
programa: firefox = Navegador
programa: opera = Navegador
programa: brave = Navegador
programa: vivaldi = Navegador

# Windows
programa: explorer = Pastas do Windows
";

    const string REGRAS_EN =
@"# Time categories
# One rule per line, in the format:   type: text = Name shown
#
#   site:     looks for the text in the tab title, ONLY when the window is a browser
#             (Chrome, Edge, Firefox, Opera, Brave, Vivaldi)
#   title:    looks for the text in the title of ANY window (use with care)
#   program:  compares with the start of the program name, without .exe
#             (see the name in Task Manager > Details)
#
# Optional tolerance at the end of the line:   Name | seconds
#   How long without touching the mouse/keyboard still counts for that item.
#   Use it for video and meetings, where you watch without touching anything.
#   Without the | the program default is used (30 s).
#   E.g.: site: YouTube = YouTube | 600   (keeps counting up to 10 min idle)
#
# Groups (optional): a [Group name] line puts the rules below it in that group,
#   until the next [ ]. A [] line goes back to no group. The dashboard shows how
#   much time went to each group. Create, rename and move rules as you like.
#
# The first matching rule wins: browsers go at the end, after the websites.
# Anything that matches no rule counts as ""Others"".
# The window title is never saved, only the rule Name.
# After saving this file, Time reloads it by itself within a few seconds.

[Social media]
site: Instagram = Instagram
site: Facebook = Facebook
site: Messenger = Facebook
site: TikTok = TikTok | 180
site: / X = X (Twitter)
site:  on X: = X (Twitter)
site: Twitter = X (Twitter)
site: Threads = Threads
site: LinkedIn = LinkedIn
site: Pinterest = Pinterest
site: Reddit = Reddit

[Video & music]
site: YouTube = YouTube | 600
site: Twitch = Twitch | 600
site: Netflix = Netflix | 600
site: Prime Video = Prime Video | 600
site: Disney+ = Disney+ | 600
site: Spotify = Spotify
program: Spotify = Spotify
program: vlc = VLC | 600
program: Video.UI = Movies & TV | 600
program: Microsoft.Media.Player = Media Player | 600
program: mpc-hc = Media Player Classic | 600

[Communication]
site: WhatsApp = WhatsApp
site: Gmail = Gmail
site: Outlook = Outlook
site: Meet = Google Meet | 900
site: Microsoft Teams = Teams | 900
program: WhatsApp = WhatsApp
program: Telegram = Telegram
program: Discord = Discord
program: OUTLOOK = Outlook
program: olk = Outlook
program: ms-teams = Teams | 900
program: Teams = Teams | 900
program: Zoom = Zoom | 900

[AI]
site: ChatGPT = ChatGPT
site: Claude = Claude
site: Gemini = Gemini
site: Grok = Grok
site: Copilot = Copilot
site: DeepSeek = DeepSeek
site: Perplexity = Perplexity
program: ChatGPT = ChatGPT
program: claude = Claude

[Work]
site: Google Drive = Google Drive
site: Google Docs = Google Docs
site: Google Sheets = Google Sheets
site: Canva = Canva
site: Freepik = Freepik
site: Figma = Figma
program: CorelDRW = CorelDRAW
program: CorelPP = Corel PHOTO-PAINT
program: Photoshop = Photoshop
program: Illustrator = Illustrator
program: InDesign = InDesign
program: Adobe Premiere Pro = Premiere Pro
program: AfterFX = After Effects
program: Lightroom = Lightroom
program: acad = AutoCAD
program: SketchUp = SketchUp
program: 3dsmax = 3ds Max
program: Revit = Revit
program: blender = Blender
program: UnrealEditor = Unreal Engine
program: Figma = Figma
program: Inventor = Inventor
program: Fusion360 = Fusion 360
program: SLDWORKS = SolidWorks
program: Rhino = Rhino
program: ArchiCAD = Archicad
program: Roamer = Navisworks
program: Lumion = Lumion
program: Twinmotion = Twinmotion
program: qgis = QGIS
program: maya = Maya
program: Cinema 4D = Cinema 4D
program: ZBrush = ZBrush
program: Unity = Unity
program: Godot = Godot
program: inkscape = Inkscape
program: gimp = GIMP
program: krita = Krita
program: Resolve = DaVinci Resolve
program: CapCut = CapCut
program: obs64 = OBS Studio
program: soffice = LibreOffice
program: Acrobat = Adobe Acrobat
program: AcroRd32 = Adobe Acrobat
program: Foxit = Foxit PDF
program: EXCEL = Excel
program: WINWORD = Word
program: POWERPNT = PowerPoint
program: ONENOTE = OneNote
program: Code = VS Code
program: devenv = Visual Studio
program: AnyDesk = AnyDesk
program: notepad = Notepad

[Shopping]
site: Amazon = Amazon
site: eBay = eBay
site: AliExpress = AliExpress
site: Shopee = Shopee

[Games]
program: Steam = Steam

# ===== No group =====
[]
# Browsers (anything that isn't one of the websites above)
program: chrome = Browser
program: msedge = Browser
program: firefox = Browser
program: opera = Browser
program: brave = Browser
program: vivaldi = Browser

# Windows
program: explorer = File Explorer
";

    void CarregarRegras()
    {
        try
        {
            if (!File.Exists(arquivoRegras))
                File.WriteAllText(arquivoRegras, RegrasPadrao, new UTF8Encoding(true));

            var novas = new List<Regra>();
            string grupo = "";
            foreach (var bruta in File.ReadAllLines(arquivoRegras))
            {
                string linha = bruta.Trim();
                if (linha.Length == 0 || linha.StartsWith('#')) continue;

                // [Grupo]: as regras de baixo ficam nesse grupo até o próximo [ ]; "[]" = sem grupo.
                if (linha.StartsWith('[') && linha.EndsWith(']'))
                {
                    grupo = linha[1..^1].Trim();
                    continue;
                }

                int dois = linha.IndexOf(':');
                int igual = linha.LastIndexOf('=');
                if (dois < 0 || igual < dois) continue;

                string tipo = linha[..dois].Trim().ToLowerInvariant();
                string texto = linha[(dois + 1)..igual].Trim();
                string nome = linha[(igual + 1)..].Trim();

                // Tolerância opcional no fim:  Nome | 600
                int tolerancia = 0;
                int barra = nome.LastIndexOf('|');
                if (barra >= 0)
                {
                    int.TryParse(nome[(barra + 1)..].Trim(), out tolerancia);
                    nome = nome[..barra].Trim();
                }
                if (texto.Length == 0 || nome.Length == 0) continue;
                if (texto.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) texto = texto[..^4];

                if (tipo is "título" or "title") tipo = "titulo";
                if (tipo == "program") tipo = "programa";
                if (tipo is "site" or "titulo" or "programa") novas.Add(new Regra(tipo, texto, nome, Math.Max(0, tolerancia), grupo));
            }

            // O grupo de cada nome é o da primeira regra com esse nome que tem grupo.
            var mapa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in novas)
                if (r.Grupo.Length > 0 && !mapa.ContainsKey(r.Nome)) mapa[r.Nome] = r.Grupo;

            lock (trava)
            {
                regras = novas;
                grupos = mapa;
            }
            regrasModificadas = File.GetLastWriteTimeUtc(arquivoRegras);
        }
        catch { /* arquivo com problema: mantém as regras atuais */ }
    }

    void RecarregarRegrasSeMudou()
    {
        try
        {
            if (File.Exists(arquivoRegras) && File.GetLastWriteTimeUtc(arquivoRegras) != regrasModificadas)
                CarregarRegras();
        }
        catch { }
    }

    // ---------- Carregar ----------

    void Carregar()
    {
        if (!File.Exists(arquivo)) return;

        string[] conteudo;
        try { conteudo = LerLinhas(arquivo); }
        catch
        {
            protegerDias = true;
            return;
        }

        foreach (var linha in conteudo)
        {
            // Data;Usuario;Maquina;Primeira;Ultima;Tempo ativo;Segundos;Limite
            var p = linha.Split(';');
            if (p.Length >= 7 && long.TryParse(p[6], out var seg1) && DataValida(p[0]))
            {
                dias[p[0]] = new RegistroDia
                {
                    Ms = seg1 * 1000,
                    Primeira = TimeSpan.TryParse(p[3], CultureInfo.InvariantCulture, out var a) ? a : null,
                    Ultima = TimeSpan.TryParse(p[4], CultureInfo.InvariantCulture, out var b) ? b : null,
                    Limite = p.Length >= 8 ? (p[7] is "misto" or "mixed" ? -1 : int.TryParse(p[7], out var lim) ? lim : 0) : 0
                };
            }
        }
    }

    void CarregarApps()
    {
        if (!File.Exists(arquivoApps)) return;

        string[] conteudo;
        try { conteudo = LerLinhas(arquivoApps); }
        catch
        {
            protegerApps = true;
            return;
        }

        foreach (var linha in conteudo)
        {
            // Data;Usuario;Maquina;Item;Tempo;Segundos
            var p = linha.Split(';');
            if (p.Length >= 6 && DataValida(p[0]) && long.TryParse(p[5], out var seg))
            {
                if (!apps.TryGetValue(p[0], out var porItem))
                    apps[p[0]] = porItem = new Dictionary<string, long>();
                porItem[p[3]] = seg * 1000;
            }
        }
    }

    void CarregarHoras()
    {
        if (!File.Exists(arquivoHoras)) return;

        string[] conteudo;
        try { conteudo = LerLinhas(arquivoHoras); }
        catch
        {
            protegerHoras = true;
            return;
        }

        foreach (var linha in conteudo)
        {
            // Data;Usuario;Maquina;Hora;Segundos
            var p = linha.Split(';');
            if (p.Length >= 5 && DataValida(p[0]) &&
                int.TryParse(p[3], out int hora) && hora >= 0 && hora < 24 &&
                long.TryParse(p[4], out var seg))
            {
                if (!horas.TryGetValue(p[0], out var porHora))
                    horas[p[0]] = porHora = new long[24];
                porHora[hora] = seg * 1000;
            }
        }
    }

    // Lê mesmo com o arquivo aberto no Excel (o File.ReadAllLines falha quando
    // outro programa está com o arquivo aberto para escrita).
    static string[] LerLinhas(string caminho)
    {
        using var fluxo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var leitor = new StreamReader(fluxo, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var linhas = new List<string>();
        string? linha;
        while ((linha = leitor.ReadLine()) != null) linhas.Add(linha);
        return linhas.ToArray();
    }

    static bool DataValida(string s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    // ---------- Salvar ----------

    void Salvar()
    {
        string[] linhas, linhasApps, linhasHoras, relatorio;
        lock (trava)
        {
            linhas = LinhasDias();
            linhasApps = LinhasApps();
            linhasHoras = LinhasHoras();
            relatorio = MontarRelatorio(Environment.UserName, Environment.MachineName);
        }

        // Cada arquivo em seu próprio try: se um estiver aberto no Excel, os outros ainda são salvos.
        Gravar(Path.ChangeExtension(arquivo, ".txt"), relatorio);
        Gravar(protegerDias ? Path.ChangeExtension(arquivo, ".novo.csv") : arquivo, linhas);
        Gravar(protegerApps ? Path.ChangeExtension(arquivoApps, ".novo.csv") : arquivoApps, linhasApps);
        Gravar(protegerHoras ? Path.ChangeExtension(arquivoHoras, ".novo.csv") : arquivoHoras, linhasHoras);
    }

    // Linhas dos CSV, com o cabeçalho. Chamar dentro de lock (trava).
    string[] LinhasDias()
    {
        string usuario = Environment.UserName, maquina = Environment.MachineName;
        return dias.OrderBy(t => t.Key).Select(t =>
        {
            long s = t.Value.Ms / 1000;
            return string.Join(';', t.Key, usuario, maquina,
                t.Value.Primeira?.ToString(@"hh\:mm\:ss") ?? "",
                t.Value.Ultima?.ToString(@"hh\:mm\:ss") ?? "",
                Hms(s), s, LimiteTexto(t.Value.Limite));
        }).Prepend(CABECALHO).ToArray();
    }

    string[] LinhasApps()
    {
        string usuario = Environment.UserName, maquina = Environment.MachineName;
        return apps.OrderBy(d => d.Key)
            .SelectMany(d => d.Value.OrderByDescending(i => i.Value).Select(i =>
            {
                long s = i.Value / 1000;
                return string.Join(';', d.Key, usuario, maquina, i.Key.Replace(";", ","), Hms(s), s);
            }))
            .Prepend(CABECALHO_APPS).ToArray();
    }

    // Só as horas com pelo menos 1 s de uso, para o arquivo não crescer à toa.
    string[] LinhasHoras()
    {
        string usuario = Environment.UserName, maquina = Environment.MachineName;
        return horas.OrderBy(d => d.Key)
            .SelectMany(d => Enumerable.Range(0, 24)
                .Where(h => d.Value[h] >= 1000)
                .Select(h => string.Join(';', d.Key, usuario, maquina, h, d.Value[h] / 1000)))
            .Prepend(CABECALHO_HORAS).ToArray();
    }

    static void Gravar(string caminho, string[] linhas)
    {
        try { File.WriteAllLines(caminho, linhas, new UTF8Encoding(true)); }
        catch { /* arquivo aberto ou sem permissão: tenta de novo no próximo ciclo */ }
    }

    // Versão legível para abrir no Bloco de Notas: dados fixos no topo e colunas alinhadas.
    string[] MontarRelatorio(string usuario, string maquina)
    {
        var l = new List<string>
        {
            "Time",
            L.T("Usuário: ", "User: ") + usuario,
            L.T("Máquina: ", "Machine: ") + maquina,
            "",
            L.T("Data", "Date").PadRight(12) + L.T("Início", "Start").PadRight(10) + L.T("Fim", "End").PadRight(10) +
                L.T("Tempo ativo", "Active time").PadRight(14) + L.T("Limite", "Limit"),
            new string('-', 10).PadRight(12) + new string('-', 8).PadRight(10) + new string('-', 8).PadRight(10)
                + new string('-', 12).PadRight(14) + new string('-', 6)
        };

        foreach (var t in dias.OrderBy(x => x.Key))
        {
            string data = DataBr(t.Key);
            string ini = t.Value.Primeira?.ToString(@"hh\:mm\:ss") ?? "--:--:--";
            string fim = t.Value.Ultima?.ToString(@"hh\:mm\:ss") ?? "--:--:--";
            string lim = t.Value.Limite > 0 ? t.Value.Limite + " s" : t.Value.Limite < 0 ? L.T("misto", "mixed") : "-";
            l.Add(data.PadRight(12) + ini.PadRight(10) + fim.PadRight(10) + Hms(t.Value.Ms / 1000).PadRight(14) + lim);
        }

        if (apps.Count > 0)
        {
            l.Add("");
            l.Add(L.T("Por programa", "Per app"));
            l.Add(L.T("Data", "Date").PadRight(12) + L.T("Programa / site", "App / website").PadRight(24) + L.T("Tempo", "Time"));
            l.Add(new string('-', 10).PadRight(12) + new string('-', 22).PadRight(24) + new string('-', 8));
            foreach (var d in apps.OrderBy(x => x.Key))
                foreach (var i in d.Value.OrderByDescending(x => x.Value))
                    l.Add(DataBr(d.Key).PadRight(12) + i.Key.PadRight(24) + Hms(i.Value / 1000));
        }

        return l.ToArray();
    }

    static string DataBr(string chave) =>
        DateTime.ParseExact(chave, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .ToString(L.T("dd/MM/yyyy", "yyyy-MM-dd"), CultureInfo.InvariantCulture);

    // ---------- Pasta de dados ----------
    // Caminho fixo: C:\Program Files\Time (o programa cria na primeira execução).
    // Só se não houver como gravar ali é que usa %LocalAppData%\Time, avisando na janela.

    static readonly string pastaFixa = Program.PastaInstalada;
    static readonly string pasta = PastaDados();
    static bool pastaAlternativa;

    // Usado pela auto-instalação, antes de existir um Contador.
    internal static string PastaDeDados => pasta;
    internal static bool PastaAlternativa => pastaAlternativa;

    static string PastaDados()
    {
        bool ok = PodeGravar(pastaFixa);

        // Pasta do Program Files exige elevação para ser criada/liberada.
        // Pedimos uma única vez, na execução manual (não quando sobe com o Windows).
        if (!ok && !Program.IniciadoPeloWindows)
        {
            LiberarComAdmin(pastaFixa);
            ok = PodeGravar(pastaFixa);
        }

        string escolhida = pastaFixa;
        if (!ok)
        {
            pastaAlternativa = true;
            escolhida = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Time");
            try { Directory.CreateDirectory(escolhida); } catch { }
        }

        return escolhida;
    }

    // Cria a pasta se faltar e confirma que dá para gravar de verdade.
    static bool PodeGravar(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string teste = Path.Combine(dir, $".teste_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(teste, "x");
            File.Delete(teste);
            return true;
        }
        catch { return false; }
    }

    // Cria a pasta e libera a gravação para o usuário atual, com uma elevação (UAC).
    static void LiberarComAdmin(string dir)
    {
        try
        {
            string usuario = $"{Environment.UserDomainName}\\{Environment.UserName}";
            string comando = $"if not exist \"{dir}\" mkdir \"{dir}\" & icacls \"{dir}\" /grant \"{usuario}:(OI)(CI)M\" /T /C";
            var psi = new ProcessStartInfo("cmd.exe", "/c " + comando)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(120000);
        }
        catch { /* UAC recusado ou usuário sem permissão: segue com a pasta alternativa */ }
    }

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
