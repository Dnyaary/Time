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
