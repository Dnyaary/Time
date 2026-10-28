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

// Arquivos de dados: carregar e salvar os CSV, o relatório .txt e a pasta de dados.

partial class Contador
{
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
            // Data;Usuario;Maquina;Primeira;Ultima;Tempo ativo;Segundos;Limite;Parado
            var p = linha.Split(';');
            if (p.Length >= 7 && long.TryParse(p[6], out var seg1) && DataValida(p[0]))
            {
                dias[p[0]] = new RegistroDia
                {
                    Ms = seg1 * 1000,
                    Primeira = TimeSpan.TryParse(p[3], CultureInfo.InvariantCulture, out var a) ? a : null,
                    Ultima = TimeSpan.TryParse(p[4], CultureInfo.InvariantCulture, out var b) ? b : null,
                    Limite = p.Length >= 8 ? (p[7] is "misto" or "mixed" ? -1 : int.TryParse(p[7], out var lim) ? lim : 0) : 0,
                    MsParado = p.Length >= 9 && long.TryParse(p[8], out var parado) ? parado * 1000 : 0
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
                Hms(s), s, LimiteTexto(t.Value.Limite), t.Value.MsParado / 1000);
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
}
