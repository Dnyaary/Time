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

// A janela completa (informações e opções) e a janela mínima do atalho.

class Janela : Form
{
    public readonly Label Hoje = new();
    public readonly Label Status = new();
    public readonly Label Periodo = new();
    public readonly Label Foco = new();
    public readonly Label Parado = new();
    public readonly Label ProgNomes = new();
    public readonly Label ProgTempos = new();
    public readonly Label Historico = new();
    public readonly CheckBox IniciarComWindows;
    public readonly CheckBox Servidor;
    public readonly Button ConfigurarServidor;
    public readonly Label StatusServidor;
    public readonly CheckBox Celular;
    public readonly TextBox Link;
    public readonly Button CopiarLink;
    public readonly Label TituloLink2;
    public readonly TextBox Link2;
    public readonly Button CopiarLink2;
    public readonly Button Pausar;
    public readonly Button AbrirPasta;
    public readonly Button EditarCategorias;

    public event Action? Fechou;   // disparado quando o X fecha a janela

    readonly Label atalho;
    readonly Label credito;
    const int ALTURA_LINK2 = 44;   // espaço do segundo link, quando o PC está em duas redes
    bool link2Visivel;

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
        ClientSize = new Size(300, 598);
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.White;

        var titulo = new Label { Text = L.T("Hoje", "Today"), Left = 16, Top = 12, AutoSize = true, ForeColor = Color.Gray };
        Hoje.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
        Hoje.Left = 12; Hoje.Top = 30; Hoje.AutoSize = true;
        Status.Left = 16; Status.Top = 78; Status.AutoSize = true;
        Periodo.Left = 16; Periodo.Top = 98; Periodo.AutoSize = true; Periodo.ForeColor = Color.Gray;
        Foco.Left = 16; Foco.Top = 118; Foco.AutoSize = true; Foco.ForeColor = Color.Gray;
        Parado.Left = 16; Parado.Top = 138; Parado.AutoSize = true; Parado.ForeColor = Color.Gray;

        Pausar = new Button { Text = L.T("Pausar", "Pause"), Left = 204, Top = 74, Width = 80, Height = 24 };

        var tituloProg = new Label { Text = L.T("Hoje por programa", "Today by app"), Left = 16, Top = 166, AutoSize = true, ForeColor = Color.Gray };
        ProgNomes.Left = 16; ProgNomes.Top = 186;
        ProgNomes.AutoSize = false; ProgNomes.Width = 145; ProgNomes.Height = 80;
        ProgNomes.AutoEllipsis = true;
        ProgTempos.Left = 170; ProgTempos.Top = 186; ProgTempos.AutoSize = true;

        var tituloHist = new Label { Text = L.T("Últimos dias", "Previous days"), Left = 16, Top = 278, AutoSize = true, ForeColor = Color.Gray };
        Historico.Left = 16; Historico.Top = 298; Historico.AutoSize = true;

        var linha = new Panel { Left = 16, Top = 384, Width = 268, Height = 1, BackColor = Color.Gainsboro };

        IniciarComWindows = new CheckBox
        {
            Text = L.T("Iniciar com o Windows", "Start with Windows"), Left = 14, Top = 392,
            Width = 270, Height = 22, AutoSize = false
        };

        AbrirPasta = new Button { Text = L.T("Pasta dos dados", "Data folder"), Left = 16, Top = 416, Width = 130, Height = 26 };
        EditarCategorias = new Button { Text = L.T("Categorias", "Categories"), Left = 154, Top = 416, Width = 130, Height = 26 };

        // Servidor (opcional): envia os dados para um servidor próprio, para ver de qualquer lugar.
        Servidor = new CheckBox
        {
            Text = L.T("Enviar para o servidor", "Send to the server"), Left = 14, Top = 448,
            Width = 190, Height = 22, AutoSize = false
        };

        ConfigurarServidor = new Button
        {
            Text = L.T("Configurar", "Set up"), Left = 212, Top = 446, Width = 72, Height = 24,
            Font = new Font("Segoe UI", 8f)
        };

        StatusServidor = new Label
        {
            Left = 32, Top = 472, Width = 252, Height = 16,
            ForeColor = Color.Gray, Font = new Font("Segoe UI", 7.5f), AutoEllipsis = true
        };

        Celular = new CheckBox
        {
            Text = L.T("Acessar pelo celular (rede local)", "Phone access (local network)"), Left = 14, Top = 494,
            Width = 270, Height = 22, AutoSize = false
        };

        Link = new TextBox
        {
            Left = 16, Top = 520, Width = 190, Height = 22,
            ReadOnly = true, Visible = false,
            Font = new Font("Segoe UI", 7f)
        };

        CopiarLink = new Button
        {
            Text = L.T("Copiar", "Copy"), Left = 212, Top = 518, Width = 72, Height = 24,
            Visible = false, Font = new Font("Segoe UI", 8f)
        };

        // Segundo link: só aparece quando o PC está ligado em duas redes (cabo e Wi-Fi, por exemplo).
        TituloLink2 = new Label
        {
            Left = 16, Top = 548, Width = 268, Height = 16, Visible = false,
            ForeColor = Color.Gray, Font = new Font("Segoe UI", 7.5f)
        };

        Link2 = new TextBox
        {
            Left = 16, Top = 566, Width = 190, Height = 22,
            ReadOnly = true, Visible = false,
            Font = new Font("Segoe UI", 7f)
        };

        CopiarLink2 = new Button
        {
            Text = L.T("Copiar", "Copy"), Left = 212, Top = 564, Width = 72, Height = 24,
            Visible = false, Font = new Font("Segoe UI", 8f)
        };

        atalho = new Label
        {
            Text = L.T("Ctrl+Shift+Alt+X e depois 5 - abre e fecha a janela.\nCtrl+Shift+Alt+F2 e depois 1 - mostra estas informações.",
                       "Ctrl+Shift+Alt+X, then 5 - opens and hides the window.\nCtrl+Shift+Alt+F2, then 1 - shows this information."),
            Left = 16, Top = 544, Width = 268, Height = 30,
            ForeColor = Color.Gray, Font = new Font("Segoe UI", 7.5f)
        };

        credito = new Label
        {
            Text = "by Daniel 'dnyaary' Carvalho", Left = 16, Top = 578, AutoSize = true,
            ForeColor = Color.Silver, Font = new Font("Segoe UI", 7.5f)
        };

        Controls.AddRange(new Control[]
        {
            titulo, Hoje, Pausar, Status, Periodo, Foco, Parado, tituloProg, ProgNomes, ProgTempos, tituloHist, Historico,
            linha, IniciarComWindows, AbrirPasta, EditarCategorias, Servidor, ConfigurarServidor, StatusServidor,
            Celular, Link, CopiarLink,
            TituloLink2, Link2, CopiarLink2, atalho, credito
        });
    }

    // Mostra ou esconde o segundo link; a janela cresce para cima, sem sair da tela.
    public void MostrarLink2(bool mostrar)
    {
        if (link2Visivel == mostrar) return;
        link2Visivel = mostrar;

        int extra = mostrar ? ALTURA_LINK2 : -ALTURA_LINK2;
        TituloLink2.Visible = Link2.Visible = CopiarLink2.Visible = mostrar;
        atalho.Top += extra;
        credito.Top += extra;
        ClientSize = new Size(ClientSize.Width, ClientSize.Height + extra);
        if (Visible) Top -= extra;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        Fechou?.Invoke();
    }
}

// Endereço e chave de envio do servidor (o botão "Configurar" da janela).
class JanelaServidor : Form
{
    public readonly TextBox Endereco = new();
    public readonly TextBox Chave = new();

    public JanelaServidor(string endereco, string chave)
    {
        Text = L.T("Time — servidor", "Time — server");
        Icon = SystemIcons.Application;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(360, 214);
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.White;

        var explica = new Label
        {
            Text = L.T("O Time envia o resumo de cada dia para o seu servidor. Assim o painel abre de qualquer lugar e mostra todos os PCs, mesmo desligados.",
                       "Time sends each day's summary to your server. The dashboard then opens from anywhere and shows every PC, even when they are off."),
            Left = 16, Top = 12, Width = 328, Height = 46, ForeColor = Color.DimGray
        };
        var rotuloEndereco = new Label { Text = L.T("Endereço do servidor", "Server address"), Left = 16, Top = 64, AutoSize = true };
        Endereco.SetBounds(16, 84, 328, 24);
        Endereco.Text = endereco.Length > 0 ? endereco : "https://";
        var rotuloChave = new Label { Text = L.T("Chave de envio", "Upload key"), Left = 16, Top = 116, AutoSize = true };
        Chave.SetBounds(16, 136, 328, 24);
        Chave.Text = chave;
        Chave.UseSystemPasswordChar = true;

        var ok = new Button { Text = "OK", Left = 188, Top = 174, Width = 76, Height = 28, DialogResult = DialogResult.OK };
        var cancelar = new Button { Text = L.T("Cancelar", "Cancel"), Left = 268, Top = 174, Width = 76, Height = 28, DialogResult = DialogResult.Cancel };
        AcceptButton = ok;
        CancelButton = cancelar;

        Controls.AddRange(new Control[] { explica, rotuloEndereco, Endereco, rotuloChave, Chave, ok, cancelar });
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
