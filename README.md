# Time

**English** · [Português](README.pt-BR.md) · [中文](README.zh-CN.md)

Made by **Daniel 'dnyaary' Carvalho** · [instagram.com/dnyaary](https://instagram.com/dnyaary)

**Tracks how much you actually use your PC and shows it all on a dashboard on your phone, over your local network.** No account, no cloud, no installer.

Time runs hidden on Windows and counts the time the computer is really in use — not just switched on. It watches which app is in the foreground and pauses on its own when you step away. What it measures becomes a web dashboard served by the PC itself, opened on your phone through the local network IP. Several computers can be added up on the same link.

> The interface follows the language of Windows (the window) and of your phone (the dashboard): English, or Portuguese when that's the system language.

<p>
  <img src="docs/img/panel-today.png" width="300" alt="Time dashboard: today's time, apps and a chart of the last 14 days">
  <img src="docs/img/panel-day.png" width="300" alt="Details of a previous day, with the day marked on the chart and every app used">
  <img src="docs/img/panel-app.png" width="300" alt="Detail of one app in the light theme: today, last 14 days, week and month">
</p>

## Features

- **Real usage time:** counts while the PC is in use and pauses after a while with no mouse or keyboard input. Each moment is credited to the app in the foreground.
- **Per app:** lists today's apps, each with a bar proportional to its time. Names are grouped by simple rules (`categorias.txt`), so "X (Twitter)", "YouTube" and so on show up the way you choose.
- **By hour:** bar chart of the hours of the day you use the PC the most.
- **Last 14 days:** line chart with the period average. The line is green at the bottom and turns amber and red as it rises.
- **This week and this month:** total time, average per day of use and comparison with the previous week and month.
- **Groups:** put apps in groups (Work, Social media, Video & music…) in `categorias.txt` and see the time per group.
- **App detail:** tap an app to see its history — today, last 14 days, week and month.
- **Light and dark theme:** follows the phone, with a button to switch.
- **Previous days:** below the chart, a summary of each day (date and time). Tap a day to open the chart with that day marked and **every** app used on it.
- **Report:** download the history (days, apps or hours) as CSV spreadsheets straight from the dashboard.
- **Several PCs:** each PC runs its own Time; one becomes the main one and adds up the others. The list shows the total and the time per computer — tap a PC to see only what it is using.
- **Automatic discovery:** Time instances on the same network find each other (UDP 8788). You only type the other PC's code once.
- **Discreet window:** the shortcut opens only a small window with the name "Time" and a minimize button (minimizing hides it again). The full information is behind a second shortcut. Time itself also shows up in the app list, so the dashboard shows when you have it open.
- **No cloud:** the data stays in `C:\Program Files\Time`, on your PC. The dashboard only answers on the local network and requires an access code.

## Installation (Windows 10/11, 64-bit)

1. Download `Time.exe` from the [Releases](../../releases/latest) page (a single self-contained file — no need to install .NET).
2. Run it from where you downloaded it. **Nothing appears on screen** — it stays hidden.
3. On the first run it **installs itself to `C:\Program Files\Time\Time.exe`** and
   restarts from there. You can delete the downloaded file and its folder: the app
   no longer depends on them.
4. Windows asks for **two elevations (UAC)**: one to create the folder and one to
   open the firewall. Accept both.
5. Press **Ctrl+Shift+Alt+X, then 5** to open the window: it shows only the name
   **Time** and the minimize button. Minimizing hides it again.
6. With it open, press **Ctrl+Shift+Alt+F2, then 1** to see the details: dashboard
   link, access code and the **Start with Windows** option.
7. On your phone, open `http://YOUR-IP:8787/?k=YOUR-CODE` on the same network.

To open it from Explorer, the app lives in `C:\Program Files\Time\Time.exe`
(right-click → **Send to → Desktop (create shortcut)**).
To update: close Time with the **X of the full window**, run the new `.exe` and
it installs itself over the old one.

Since the `.exe` is not digitally signed, Windows may show "Windows protected your PC" the first time: **More info → Run anyway**. If the phone can't connect, it is almost always a firewall **block** rule created when that Windows prompt was cancelled — see the [manual](docs/manual.en.md).

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```powershell
dotnet build
# creates bin\Debug\net10.0-windows\Time.exe (needs .NET installed;
# runs from that folder, without installing itself to Program Files)

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
# creates bin\Release\net10.0-windows\win-x64\publish\Time.exe (single file, ~117 MB)
```

On every push to `main`, GitHub Actions also builds it and makes `Time.exe` available to download in the **Actions** tab.

## Project structure

| File | Purpose |
|---|---|
| `Program.cs` | counting, window, global shortcut, firewall, data, dashboard and remote PCs |
| `Api.cs` | HTTP server and the whole dashboard page (embedded HTML/CSS/JS) |
| `Descoberta.cs` | discovery of other PCs on the network (UDP 8788, HMAC) |
| `Time.csproj` | .NET 10 WinForms project, icon and metadata |
| `icone.ico` | generic Windows icon embedded in the `.exe` |
| `docs/manual.en.md` | user manual (also in [Portuguese](docs/manual.md) and [Chinese](docs/manual.zh-CN.md)) |
| `CHANGELOG.md` | release notes (Portuguese) |

The source code (names and comments) is written in Portuguese.

## Where the data lives

Everything is in `C:\Program Files\Time`:

| File | Contents |
|---|---|
| `atividade_diaria.csv` | total per day (start, last activity, seconds) |
| `apps_diario.csv` | time per app, per day |
| `horas_diario.csv` | time per hour of the day, per day |
| `categorias.txt` | rules that group apps |
| `config.txt` | port, access code and firewall state |
| `pcs.txt` | other PCs shown on the dashboard |

Copying this folder is a complete backup. The files are plain text (CSV), readable by anything.

### Rules (`categorias.txt`)

One rule per line, in the format `type: text = Name | tolerance`:

```text
site: YouTube = YouTube | 600        # browser tab title only; keeps counting up to 10 min idle
program: CorelDRW = CorelDRAW        # start of the process name (without .exe)
title: Invoice = Invoices            # title of any window
```

The Portuguese keywords `programa:` and `titulo:` work too. The first matching rule wins; anything that matches no rule counts as **"Others"**. The file reloads by itself a few seconds after you save it.

## Privacy

- The app sends nothing outside: the dashboard only answers on the local network.
- The dashboard requires the access code; without it, the API returns `401`.
- PC discovery does not send the code, not even as a hash: the announcement carries only the machine name and port, and the other PC is verified with an HMAC challenge.
- The dashboard is plain HTTP: inside the network, the code travels in the address (`?k=`) without encryption. Use it on networks you trust and never expose the port to the internet.
- What is measured: the foreground process name and time. The window title is only compared against the rules at that moment and is **never saved**. Window contents are not read.
- If you use it on other people's PCs (employees, for example), tell them in writing what is measured.

## Limitations

- Windows only (WinForms + Windows APIs).
- The dashboard needs the PC on, Time running and the phone on the same network.
- Does not work over VPN/mobile data by default; reaching it from outside would mean opening the port on the router (not recommended — use a VPN such as Tailscale or ZeroTier instead).
- No installer and no digital signature.

## License

[MIT](LICENSE) © 2026 Daniel 'dnyaary' Carvalho — [instagram.com/dnyaary](https://instagram.com/dnyaary)

You may use, copy, modify and distribute it, including commercially, as long as you keep the copyright notice and the license.

---

*Time · by Daniel 'dnyaary' Carvalho*
