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

// categorias.txt: regras de programas e sites, grupos e as regras padrão.

partial class Contador
{
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
# Foco (opcional): no fim do grupo, | produtivo ou | distração.
#   Ex.: [Trabalho | produtivo]   [Redes sociais | distração]
#   O painel e a janela mostram quanto do dia foi produtivo e quanto foi distração.
#   Grupo sem marca (e o que não tem grupo) conta como neutro.
#
# A primeira regra que bater vence: os navegadores ficam no fim, depois dos sites.
# O que não bater em nenhuma regra conta como ""Outros"".
# O título da janela nunca é salvo, só o Nome da regra.
# Depois de salvar este arquivo, o contador recarrega sozinho em alguns segundos.

[Redes sociais | distração]
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

[Vídeo e música | distração]
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

[Inteligência artificial | produtivo]
site: ChatGPT = ChatGPT
site: Claude = Claude
site: Gemini = Gemini
site: Grok = Grok
site: Copilot = Copilot
site: DeepSeek = DeepSeek
site: Perplexity = Perplexity
programa: ChatGPT = ChatGPT
programa: claude = Claude

[Trabalho | produtivo]
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

[Compras | distração]
site: Mercado Livre = Mercado Livre
site: Shopee = Shopee
site: Amazon = Amazon
site: AliExpress = AliExpress

[Jogos | distração]
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
# Focus (optional): at the end of the group, | productive or | distraction.
#   E.g.: [Work | productive]   [Social media | distraction]
#   The dashboard and the window show how much of the day was productive and
#   how much was distraction. Unmarked groups (and rules with no group) are neutral.
#
# The first matching rule wins: browsers go at the end, after the websites.
# Anything that matches no rule counts as ""Others"".
# The window title is never saved, only the rule Name.
# After saving this file, Time reloads it by itself within a few seconds.

[Social media | distraction]
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

[Video & music | distraction]
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

[AI | productive]
site: ChatGPT = ChatGPT
site: Claude = Claude
site: Gemini = Gemini
site: Grok = Grok
site: Copilot = Copilot
site: DeepSeek = DeepSeek
site: Perplexity = Perplexity
program: ChatGPT = ChatGPT
program: claude = Claude

[Work | productive]
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

[Shopping | distraction]
site: Amazon = Amazon
site: eBay = eBay
site: AliExpress = AliExpress
site: Shopee = Shopee

[Games | distraction]
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
            var focoNovo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string grupo = "";
            foreach (var bruta in File.ReadAllLines(arquivoRegras))
            {
                string linha = bruta.Trim();
                if (linha.Length == 0 || linha.StartsWith('#')) continue;

                // [Grupo]: as regras de baixo ficam nesse grupo até o próximo [ ]; "[]" = sem grupo.
                // [Grupo | produtivo] ou [Grupo | distração] marca o grupo para a barra de foco.
                if (linha.StartsWith('[') && linha.EndsWith(']'))
                {
                    string dentro = linha[1..^1];
                    string marca = "";
                    int barraGrupo = dentro.LastIndexOf('|');
                    if (barraGrupo >= 0)
                    {
                        marca = TipoDeFoco(dentro[(barraGrupo + 1)..]);
                        dentro = dentro[..barraGrupo];
                    }
                    grupo = dentro.Trim();
                    if (grupo.Length > 0 && marca.Length > 0 && !focoNovo.ContainsKey(grupo)) focoNovo[grupo] = marca;
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

            // Nenhum grupo marcado: os grupos com os nomes padrão usam a marcação padrão.
            if (focoNovo.Count == 0)
                foreach (string nomeGrupo in mapa.Values.Distinct(StringComparer.OrdinalIgnoreCase))
                    if (FOCO_PADRAO.TryGetValue(nomeGrupo, out string? padrao)) focoNovo[nomeGrupo] = padrao;

            lock (trava)
            {
                regras = novas;
                grupos = mapa;
                focos = focoNovo;
            }
            regrasModificadas = File.GetLastWriteTimeUtc(arquivoRegras);
        }
        catch { /* arquivo com problema: mantém as regras atuais */ }
    }

    // ---------- Foco: produtivo ou distração ----------

    const string FOCO_PRODUTIVO = "produtivo";
    const string FOCO_DISTRACAO = "distracao";
    const string FOCO_NEUTRO = "neutro";

    // Marcação dos grupos que vêm no categorias.txt padrão (português e inglês).
    static readonly Dictionary<string, string> FOCO_PADRAO = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Trabalho"] = FOCO_PRODUTIVO, ["Work"] = FOCO_PRODUTIVO,
        ["Inteligência artificial"] = FOCO_PRODUTIVO, ["AI"] = FOCO_PRODUTIVO,
        ["Redes sociais"] = FOCO_DISTRACAO, ["Social media"] = FOCO_DISTRACAO,
        ["Vídeo e música"] = FOCO_DISTRACAO, ["Video & music"] = FOCO_DISTRACAO,
        ["Compras"] = FOCO_DISTRACAO, ["Shopping"] = FOCO_DISTRACAO,
        ["Jogos"] = FOCO_DISTRACAO, ["Games"] = FOCO_DISTRACAO
    };

    // "produtivo", "distração", "productive", "distraction"... -> o valor interno; outro texto -> "".
    static string TipoDeFoco(string texto)
    {
        string t = texto.Trim().ToLowerInvariant()
            .Replace("ç", "c").Replace("ã", "a").Replace("á", "a").Replace("é", "e");
        return t switch
        {
            "produtivo" or "productive" => FOCO_PRODUTIVO,
            "distracao" or "distraction" => FOCO_DISTRACAO,
            "neutro" or "neutral" => FOCO_NEUTRO,
            _ => ""
        };
    }

    // Foco de um programa/site pelo grupo dele. Chamar dentro de lock (trava).
    string FocoDoItem(string nome) =>
        grupos.TryGetValue(nome, out string? g) && focos.TryGetValue(g, out string? f) ? f : FOCO_NEUTRO;

    void RecarregarRegrasSeMudou()
    {
        try
        {
            if (File.Exists(arquivoRegras) && File.GetLastWriteTimeUtc(arquivoRegras) != regrasModificadas)
                CarregarRegras();
        }
        catch { }
    }
}
