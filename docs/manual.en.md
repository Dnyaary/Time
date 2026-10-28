# Time — by Daniel 'dnyaary' Carvalho

**English** · [Português](manual.md) · [中文](manual.zh-CN.md)

A Windows program that measures the PC's **real screen time**: it adds up the time
while someone is actually using the machine and splits it by **program and
website**.

> The interface follows the language of Windows (the window) and of your phone
> (the dashboard): Portuguese when that's the system language, English otherwise.
> This manual uses the English labels.

Time runs **without a system tray icon**. Its small window stays hidden and only
shows up through the global shortcut:

> **Hold Ctrl + Shift + Alt, press X and, within 4 seconds, press 5.**

The same shortcut hides the window. To really quit the program, use the **X** of
the full window (it saves the data and closes).

You can also follow everything from your **phone**, on the same Wi-Fi network, with
no cloud and no account — see [Phone dashboard](#phone-dashboard-local-network).

---

## What it measures

- **Active time:** it only counts when there was mouse or keyboard input within a
  tolerance limit. An idle PC or a locked screen does not count.
- **Per program/website:** each moment goes to the app in front (the foreground
  window), according to the rules in `categorias.txt`.
- **Video and meetings:** have a longer tolerance, so watching without touching
  the mouse still counts as screen time.

### What it does NOT do

- It does not record which keys are pressed.
- It does not capture the screen or take screenshots.
- It does not save window titles — the title is only used at that moment, to
  compare with the rules; the files only get the category name.
- It does not count apps in the background (music playing behind) or browser tabs
  that are not in front.

---

## Generated files (in `C:\Program Files\Time`)

| File | Purpose |
|---|---|
| `atividade_diaria.csv` | Total time per day. Opens in Excel (`;` separator). |
| `atividade_diaria.txt` | Same thing, as aligned text to read in Notepad. |
| `apps_diario.csv` | Time per program/website, per day. For Excel. |
| `horas_diario.csv` | Time per hour of the day (0 to 23), per day. For Excel. |
| `categorias.txt` | The list of what to count and how to name it. You edit this one. |
| `config.txt` | Port and access code of the phone dashboard. |
| `pcs.txt` | Other PCs on the dashboard, one per line: `name; code; machine; ip:port`. |
| `pcs_dados.json` | Last data received from each PC in `pcs.txt`, used while it is offline. |

The path is always `C:\Program Files\Time`. If the folder doesn't exist, the
program creates it on the first run — and that first run **asks for elevation
(UAC) only once**, because Windows doesn't allow writing to `Program Files`
without administrator rights. After that it writes on its own without asking
again (including when it starts with Windows).

If you decline the UAC prompt, the program keeps working from
`%LocalAppData%\Time` and says so when the window opens; just run `Time.exe` as
administrator once to go back to the right path.

### Columns of `atividade_diaria.csv`

```
Date;User;Machine;First activity;Last activity;Active time;Seconds;Idle limit (s);Idle (s)
2026-10-22;daniel;PC-TI;08:12:40;17:41:05;06:48:12;24492;30;3120
```

`Idle (s)` is the time the PC stayed on, with Time running, but with nobody using
it. It is not part of the active time, and a pause doesn't count as idle.

### Columns of `apps_diario.csv`

```
Date;User;Machine;Item;Time;Seconds
2026-10-22;daniel;PC-TI;CorelDRAW;03:12:40;11560
```

### Columns of `horas_diario.csv`

```
Date;User;Machine;Hour;Seconds
2026-10-22;daniel;PC-TI;14;3240
```

One line per hour with use (`14` = from 2:00 to 2:59 PM).

On a Windows in Portuguese, the column names come out in Portuguese.

---

## The `categorias.txt` list

One rule per line, in the format:

```
type: text = Name shown | tolerance
```

**Types:**

- `site:` (website) — looks for the text in the tab title, **only when the window
  is a browser** (Chrome, Edge, Firefox, Opera, Brave, Vivaldi).
- `title:` — looks for the text in the title of **any** window. Use with care.
- `program:` — compares with the start of the process name, without
  `.exe` (see the name in Task Manager → Details).

The Portuguese keywords (`titulo:` and `programa:`) work too.

**How the rules work:**

- The **first matching rule wins**, so websites go above browsers. That's why
  YouTube open in Chrome counts as "YouTube" and not as "Browser".
- Anything that matches no rule counts as **"Others"**.
- **Time** itself is the exception: with its window open, it shows up in the list
  as **"Time"** (no rule needed in `categorias.txt`).
- When you save the file, the program **reloads it by itself** within a few
  seconds, without restarting.

**Groups (optional):**

A line with a name in square brackets puts the rules below it in that group, until
the next group. A `[]` line goes back to "no group". The dashboard adds up the time
of each group in the **By group** section.

```
[Social media]
site: Instagram = Instagram
site: TikTok = TikTok | 180

[Work]
program: CorelDRW = CorelDRAW
program: EXCEL = Excel

[]
program: chrome = Browser
```

A new `categorias.txt` already comes with groups (Social media, Video & music,
Communication, AI, Work, Shopping and Games). Groups aren't stored in the history:
move a program to another group and the dashboard adds up its past days in the
new group right away.

**Focus: productive or distraction (optional):**

At the end of the group name, `| productive` or `| distraction` says what that
time means to you. The dashboard shows the **Focus** bar (how much of the day was
productive, distraction and neutral) and the window shows the line
"Productive 5h 10min · Distraction 1h 20min".

```
[Work | productive]
program: devenv = Visual Studio

[Social media | distraction]
site: Instagram = Instagram

[Communication]
program: WhatsApp = WhatsApp
```

Unmarked groups (like Communication above) and programs with no group count as
**neutral**. The default file already marks Work and AI as productive, and Social
media, Video & music, Shopping and Games as distraction. It's your call: change
the marks as you like — the dashboard recalculates every day. If no group is
marked at all, groups with the default names use these default marks; use
`| neutral` to leave a group out. The Portuguese words `produtivo`, `distração`
and `neutro` work too.

**Tolerance (optional, at the end of the line):**

How long without touching the mouse/keyboard still counts for that item. Without
the `|`, the default of 30 s is used. It's meant for video and meetings, where you
watch without touching anything.

```
site: YouTube = YouTube | 600      (keeps counting up to 10 min idle)
site: TikTok = TikTok | 180        (3 min)
program: Zoom = Zoom | 900         (15 min)
```

Defaults already set: video 10 min, short video 3 min, meetings 15 min, everything
else 30 s.

**If a program falls into "Others":** open Task Manager → Details, check the
process name and add `program: Name = How it appears`.

---

## Building and publishing

Requires the .NET 10 SDK (or Visual Studio). In the folder with `Program.cs` and
`Time.csproj`:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The single executable ends up in:

```
bin\Release\net10.0-windows\win-x64\publish\Time.exe
```

It's self-contained: it runs on any Windows PC without installing .NET. For a
quick test before publishing, use `dotnet run` in the folder (that build runs from
its own folder and does not install itself to `Program Files`).

The `.pdb` generated alongside is only for debugging; you can ship just the `.exe`.

---

## Installing on each PC

1. Run `Time.exe` from where you downloaded it. **Nothing appears on screen** — it
   keeps running hidden.
2. On the first run it **copies itself to `C:\Program Files\Time\Time.exe`** and
   runs from there from then on. The download folder can be deleted afterwards:
   the program no longer depends on it.
3. Windows asks for **two elevations (UAC)**: one to create and unlock the folder
   `C:\Program Files\Time` (where the data lives) and one to open the firewall.
4. Press the shortcut **Ctrl+Shift+Alt+X, then 5** to open the window: it shows
   only the name **Time** and the minimize button.
5. With it open, press **Ctrl+Shift+Alt+F2, then 1** to see the full information
   and tick **Start with Windows**.

The same `Time.exe` works for every PC — it's a single self-contained file with no
installer. Since it has no digital signature, Windows may show "Windows protected
your PC" the first time: click **More info** and **Run anyway**. Sending it by
WhatsApp or e-mail is often blocked because it's an `.exe`; use a USB drive, a
network folder or a cloud link.

After that first elevation, the program runs without administrator rights and
doesn't ask for anything else. If the `C:\Program Files\Time` folder was already
unlocked by another user on this PC, the prompt doesn't even appear.

"Start with Windows" writes to the user's own registry key, so each user ticks
it once in their own login. When Windows starts the program, it does **not** ask
for UAC (the prompt only happens on a manual run).

**Important:** the program lives in `C:\Program Files\Time\Time.exe`. If you move
that file, run it again: it copies itself back there and the startup entry points
to the right place again. To update, close Time with the **X of the full window**
before running the new `.exe` — it installs itself over the old one.

---

## Day-to-day use

- **Shortcut Ctrl+Shift+Alt+X, then 5:** opens the mini window (bottom-right
  corner): only the name **Time** and the **—** minimize button. Minimizing hides
  it again; pressing the same shortcut while it's open also hides it.
- **Shortcut Ctrl+Shift+Alt+F2, then 1:** with the window open, shows the full
  information (time, programs, previous days, dashboard link). If nothing is open,
  it opens the full window directly. You have up to **4 seconds** for the second
  key and can release Ctrl/Shift/Alt after the first; any other key in between
  cancels the sequence.
- **X of the full window:** quits the program, saving the data. To keep it
  running, use the shortcut instead of the X. The mini window has no X.
- **Status line:** **● Counting** while the PC is in use, **○ Idle** once the
  tolerance passes with no mouse/keyboard input, and **❚❚ Paused** during a pause.
  Below it, **On but idle** shows how long the PC stayed on today with nobody using
  it (shown from 1 minute on; on the dashboard too).
- **Window buttons:**
  - **Pause**: stops counting for **15 minutes**, **1 hour** or **until you
    resume**, even while the PC is in use — for a break, or when someone else is
    going to use the PC. During the pause the button turns into **Resume** and the
    phone dashboard shows "Paused until 15:30". The pause isn't saved: closing
    Time or restarting the PC goes back to counting.
  - **Data folder**: opens `C:\Program Files\Time` in Explorer.
  - **Categories**: opens `categorias.txt` in Notepad.
  - **Start with Windows**: turns automatic startup on/off.
  - **Phone access**: turns on the local network
    dashboard and shows the link to copy (see [Phone dashboard](#phone-dashboard-local-network)).
- **Opening `Time.exe` again** (double-click) doesn't create a second copy: it only
  shows the mini window of the one already running. It's the fallback in case the
  shortcut fails on some PC.

---

## Phone dashboard (local network)

Time can show the same data in your phone's browser. It uses no cloud, needs no
account and sends nothing outside: the program itself serves a page on the local
network.

### How to turn it on

1. Open the window (shortcut) and tick **Phone access (local network)**.
2. The first time, Windows asks for permission (UAC) to open the ports in the
   firewall (the dashboard port and the PC discovery port). Accept — only this once.
3. Copy the link with the **Copy** button. It looks like this:
   `http://192.168.0.10:8787/?k=ABCD-EFGH` (the IP and code are your PC's).
4. Open that link on your phone, on the **same Wi-Fi network**.
5. On the phone, use **"Add to Home screen"** to turn it into an app.

On the PC itself, the same dashboard opens at `http://localhost:8787/?k=YOUR-CODE`.

If the PC is on **two networks** (cable and Wi-Fi, or two networks at the office),
the window shows a second link, **"On the other network"**. Use the link of the
network the phone (or the other PC) is on. Virtual adapters (Hyper-V, WSL,
VirtualBox, VMware) are ignored.

The end of the link (`?k=...`) is the **access code**. Anyone opening the address
without it only sees the code screen; the data only appears with the right code.

### What it shows

- **Today**: today's time and the period (first and last activity).
- **Per PC**: how much each computer was used today and whether it's
  online. Tap a PC to see only what it's using.
- **Day summary**: a ready-made sentence, e.g. "10h on: 3h productive, 3h of
  distraction, 2h neutral and 2h on but idle", with a bar and the percentage of
  each part: productive (green), distraction (red), neutral (grey) and on but idle
  (striped). With no marked groups, it shows only "in use" and "idle".
- **By group**: today's time added up per `categorias.txt` group (only shown when
  there are groups). Each group's bar follows its focus color.
- **Per app**: today's top programs, with a thin bar
  proportional to each one. Tap an app to open its **detail**: today's
  time, the last 14 days chart, the week, the month and the days it was used.
- **By hour**: 24 bars, one per hour of the day, with the busiest hour
  highlighted. It also appears when you open a previous day.
- **Last 14 days**: a line chart with the period average. The
  line is green at the bottom and turns amber and red as it rises: the higher the
  day, the warmer the color.
- **This week** and **This month**: total time since Monday and since the 1st
  of the month, with the average per day of use and a comparison with the same
  stretch of the previous week and month (e.g. "▲ 12% vs last week").
- **Week / month summary**: the **Week** (Monday to Sunday) and **Month** tabs pick
  the period, and the **‹ ›** arrows go back and forth — you can see any week or
  month in the history (two months ago, for example). On Monday it opens on the
  week that just closed. It shows the sentence for the whole period, one bar per
  day in a week or per week in a month (tap to open), the comparison with the
  period before — for the period in progress, up to the same day — and the
  highlights: most productive day, most used app, biggest distraction and average
  per day of use.
- **Previous days**: below the chart, a summary of each day
  (date and total time, nothing else). Tap a day to open the chart with that day
  marked and the full list of programs used on it; **← Back** returns
  to today.
- **Report**: the **Week** (or **Month**) button downloads the summary of the period
  open in the summary above: one day per line with on, in use, productive,
  distraction, neutral and idle time, the period total and every app with its group
  and focus. The **Days**, **Apps** and **Hours** buttons download the history as
  CSV spreadsheets (open in Excel). In the all-PCs view they include every PC in
  the list; inside a single PC, only that one.
- **Add or remove PCs**, at the bottom of the page,
  to bring in the other computers.
- **On the network now**: other Time instances on the same
  network, ready to be added.
- It refreshes by itself every 30 seconds.

The look has no rounded corners and is made for quick reading on a phone. It
follows the phone's theme (light or dark); the ☀/☾ button at the top switches it,
and the choice is remembered on that device. The
dashboard keeps working with the window hidden — it only needs the PC on and Time
running.

### Several PCs

Each PC runs its own Time. To see them all in **a single link**, pick one as the
main PC (the one that stays on the longest).

On the other PC just repeat the installation above (the same `Time.exe` works for
all of them) and tick **Phone access (local network)** in its window — that
is what lets the main PC read its data. `pcs.txt` only lives on the main PC.

**The dashboard finds the other PCs by itself.** On the main PC — on `localhost`
or on the phone — open **Add or remove PCs** at the bottom of the page.
Time instances running on the same network show up under **On the network now**,
with the IP next to them:

```
ON THE NETWORK NOW
DESKTOP-NOTE                      192.168.0.51
[ other PC's code ]                     [ ADD ]
```

The name comes pre-filled with the machine's name, but you can change it to
"Notebook", "Office" etc. before adding. Type that PC's **access code** (the same
`?k=` from the link its window shows) and tap **Add**: it's a
one-time step, and the dashboard checks on the network itself that the code is
right before accepting it.

You can also add a PC by its link: paste the other PC's full link
(`http://192.168.0.51:8787/?k=IJKL-MNOP`) in the field below, with a name, and tap
**Add**. It takes effect right away, without restarting. The **×** next to
each name removes that PC from the list.

**When the IP changes, the dashboard adjusts by itself.** Each Time announces itself
on the network every 10 seconds (without sending any of your data: only the
machine name and port). The main PC updates that PC's address as soon as the
announcement arrives, after confirming that whoever answered has the code — nobody
can pretend to be another PC. The IP stored in `pcs.txt` is only a fallback, for
when the network doesn't let the announcements through.

The `pcs.txt` in the data folder keeps one line per PC:

```
nome; codigo; maquina; ip:porta
Notebook; IJKL-MNOP; DESKTOP-NOTE; 192.168.0.51:8787
```

(name; code; machine; ip:port.) You can edit it by hand, but you don't need to:
whatever you add through the page is saved there automatically.

The main PC's dashboard adds up everyone's time and shows the **Per PC** list.
If a PC doesn't respond (turned off, unstable network), it shows as `offline` with the
last time it sent, and that time stays in the total — the total doesn't drop and come
back with every network hiccup. When the PC is back, the number updates by itself. The PCs need to
be on the same network with the dashboard turned on — on each one, the first time,
Windows asks to open the firewall (the same UAC as when you turned the dashboard
on): it opens the dashboard port and the discovery port at once.

### Viewing a single PC

Tap any PC in the **Per PC** list (there's an arrow `›` at the end of the line).
The dashboard then shows only that computer:

- today's time, with the period (first and last activity);
- what it is using right now, at the top (`Counting · VS Code`);
- its programs, with the time of each one;
- that PC's chart of the last 14 days.

**← All PCs** goes back to the total. The address ends with
`&pc=...`, so reloading the page keeps the same PC open. A PC that is off can't be
opened — it shows as `offline` in the list and the dashboard has no way to ask it
what it used.

### `config.txt`

| Key | What it is |
|---|---|
| `porta` | Dashboard port (default `8787`). |
| `codigo` | Dashboard password. Change it to lock out phones that are already paired. |
| `api` | `1` = dashboard on when the program starts; `0` = off. |
| `firewall` | Port Windows has already opened (`0` = none). |
| `firewalludp` | Network discovery port Windows has already opened (`0` = none). |

After editing the file, **close and reopen Time** for it to take effect.

### If the phone can't connect

- Make sure the phone is on the **same network** as the PC.
- If Windows shows the prompt "Allow Time to communicate...", click **Allow**.
  Cancelling that prompt creates a **block** rule for `Time.exe` — and a block
  beats an allow, so the phone stays out even with the port open. To check:
  **Windows Defender Firewall with Advanced Security** → **Inbound Rules** →
  delete any block rule with the name `Time.exe`.
- Time creates three rules by itself: by port (TCP 8787 and UDP 8788) and by
  program. The program rule is the one that avoids that Windows prompt.
- The Windows network can be set to **Private** or **Public**: the rules apply in
  both cases.
- Check that the PC is on and Time is running.
- The address is **HTTP**, not HTTPS — it's normal for the browser not to show the
  padlock.
- If **On the network now** doesn't show the other PC, check that its dashboard is on,
  that the Windows network is set to **Private** and that the firewall was opened
  on it (open its window once and accept the UAC prompt).

### Security

The dashboard is meant for a home or office network. The code protects the data,
but the traffic is not encrypted (no TLS). **Do not open this port to the
internet.**

Network discovery doesn't send the code either, not even as a hash: the
announcement only carries the machine name and port. When a PC's address changes,
the dashboard confirms its identity with an HMAC challenge — without the code,
nobody can pretend to be that PC.

---

## Limitations

- `GetLastInputInfo` only sees the current interactive session; it is not a global
  service for every Windows session.
- The program doesn't know whether a video is playing or paused. A paused video in
  front keeps counting until the item's tolerance runs out.
- It starts counting from the moment it is launched; it can't recover the time
  before that.
- The shortcut depends on Windows passing the keyboard to the program (keyboard
  hook). If some security policy on the PC blocks that, the shortcut won't work; in
  that case, open `Time.exe` again to see the window.
- Writing to `C:\Program Files` requires administrator rights. On the first run the
  program asks for UAC once to unlock the folder; if declined, it falls back to
  `%LocalAppData%\Time` and says so in the window.
- Without admin restrictions, anyone can close the program with the X or Task
  Manager. In that case, that day's data is incomplete.
- The phone dashboard only works with the PC on, Time running and the phone on the
  same network. It's plain HTTP (no TLS), so it must not be exposed to the internet.
- Per-program data depends on Windows reporting which window is in front. Programs
  open behind, or a tab that isn't active, don't count.
- PC discovery uses broadcast announcements on the local network. Company networks
  that block broadcast, or Wi-Fi with client isolation, don't let the PCs find each
  other; in that case, add each one by its link.

---

## Privacy

Time was made for **personal use**: measuring your own time on the PC and staying
focused. The numbers are yours and stay on your computer.

- It doesn't record keystrokes, doesn't take screenshots and doesn't save window
  titles.
- The phone dashboard never leaves the local network and requires the access code.
- The files stay in `C:\Program Files\Time`.

If you ever use it with someone else or with a team, tell them **in writing** what
is measured (time per app) and what isn't (keystrokes, screenshots, content).
Hidden monitoring of other people can be illegal in many places — in Brazil, for
example, labor courts usually treat it as unlawful evidence and grounds for moral
damages. Use the numbers as a routine reference, not as a performance score: time
in front of the PC is not productivity — meetings, phone calls and waiting for a
render don't show up as activity.

Discreet so it doesn't get in the way — and, with other people, agreed on with
whoever uses it.

---

*Time · by Daniel 'dnyaary' Carvalho · [instagram.com/dnyaary](https://instagram.com/dnyaary) · MIT license*
