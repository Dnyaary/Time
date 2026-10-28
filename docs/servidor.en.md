# Time server (optional)

[Português](servidor.md) · **English** · [中文](servidor.zh-CN.md)

By default Time sends nothing outside your network: the phone dashboard only opens on
the same Wi-Fi as the PC. The server is for when you want more than that:

- open the dashboard **from anywhere** — on mobile data, at work, on another Wi-Fi;
- see a PC's history **even when it's off**;
- bring several PCs together **without depending on the local network** (different
  networks, unstable internet).

The server is **yours**: it runs on your Cloudflare account, and only you have access to
the data.

---

## How it works

```
 PC 1 ─┐                                       ┌─ phone (mobile data or Wi-Fi)
 PC 2 ─┼──► https://time.yourdomain.com ◄──────┤
 PC 3 ─┘    (Cloudflare Worker + D1 database)  └─ any PC's browser
   send with the CHAVE_ENVIO                     view with the CODIGO
```

- **Every minute**, each PC sends the summary of the days that changed: usage time, idle
  time, apps and hours. Never window titles.
- It always sends **the day's total**. Sending again only replaces — it never counts
  twice.
- **Without internet**, the PC keeps counting normally and sends everything when the
  connection is back. After a restart, it sends the history once more, to be safe.
- The server shows **the same dashboard** as Time, with every PC that sends. A PC that
  stopped sending more than 2 minutes ago shows as offline, with what it already sent.
- PCs **join the list by themselves**: the server recognizes each one by its Windows name.

### How much it costs

Nothing, for normal use. Cloudflare's free plan allows 100,000 requests a day and 5 GB of
database. Each PC makes about 1,440 uploads a day (one a minute); 20 PCs come to about
29,000. A year of history for one PC takes less than 1 MB.

---

## What you need

1. **A Cloudflare account** — free, at [dash.cloudflare.com](https://dash.cloudflare.com/sign-up).
2. **Node.js** (LTS version) — at [nodejs.org](https://nodejs.org). To check, open `cmd`
   and type `node -v`: a version number must show up.
3. **Time's `servidor` folder** — it comes with the project's code. On Time's GitHub,
   **Code → Download ZIP** and extract it, or use `git clone`.
4. **(Optional) A domain on Cloudflare**, for an address like `time.yourdomain.com`.
   Without a domain, the server lives at a free address like
   `time.your-account.workers.dev`.

---

## Step by step

Open `cmd` inside the `servidor` folder (in Explorer, click the address bar, type `cmd`
and press Enter). Run one command at a time.

### 1. Prepare

```
copy wrangler.exemplo.toml wrangler.toml
npm install
```

`wrangler.toml` is your configuration. It stays on your PC only (it doesn't go to Git).
`npm install` downloads Cloudflare's tool (*wrangler*).

### 2. Sign in to Cloudflare

```
npx wrangler login
```

The browser opens on Cloudflare asking you to authorize **Wrangler** ("This application
is owned and managed by Cloudflare"). Click **Authorize**. `cmd` shows
`Successfully logged in`.

### 3. Create the database

```
npx wrangler d1 create time
```

It shows a block with `database_id = "..."`. Open `wrangler.toml` in Notepad and replace
`COLE-AQUI-O-ID-DO-BANCO` with that id.

### 4. Address and time zone (in `wrangler.toml`)

- **Your own address (optional):** remove the `#` at the start of the `routes` line and
  replace `time.seudominio.com` with the address you want. The domain must be on your
  Cloudflare account. Cloudflare creates the address and the certificate (HTTPS) by itself.
- **Time zone:** `FUSO = "America/Sao_Paulo"` decides which day is "today" on the
  dashboard. Change it if you are in another time zone (e.g. `"Europe/London"`,
  `"America/New_York"`).

### 5. Create the tables

```
npm run banco
```

If it asks `Ok to proceed?`, answer **y**. It should show `Processed 3 queries`.

### 6. Create the two keys

```
npx wrangler secret put CHAVE_ENVIO
```

When `Enter a secret value:` shows up, type (or paste) the **upload key** and press
Enter. Nothing shows while you type — that's normal. If it asks whether to create the
Worker "time", answer **y**.

```
npx wrangler secret put CODIGO
```

Same thing, now with the **read code**.

| Key | What for | Where it goes |
|---|---|---|
| `CHAVE_ENVIO` | only **sending** data | on each PC, in Time's "Set up" |
| `CODIGO` | only **viewing** the dashboard | on the phone, on the code screen |

Use a long upload key (20 characters or more, letters and numbers) and a code that is
easy to type on the phone (e.g. `AB12-CD34`). The name goes in the command; the value,
only when it asks.

### 7. Publish

```
npm run deploy
```

At the end it shows the address: `time.yourdomain.com (custom domain)` or
`https://time.your-account.workers.dev`. A new address may take 1 or 2 minutes to open.

---

## Turning it on in the PCs

On each PC with Time (version 1.2.0 or newer):

1. Open the full window: **Ctrl+Shift+Alt+F2, then 1**.
2. Click **Set up**.
3. **Server address:** `https://time.yourdomain.com` (or the `workers.dev` one).
4. **Upload key:** the `CHAVE_ENVIO`.
5. **OK**.

The **Send to the server** box is ticked and, within a few seconds, the line below shows
**"Sent at 22:55 to time.yourdomain.com"**. It is saved in `config.txt`: no need to do it
again after a restart.

## Opening on the phone

Open the server address in the browser and type the `CODIGO`. It is remembered on that
device. Use **"Add to Home screen"** to turn it into an app.

---

## Maintenance

**Updating the server** (when a new Time version comes out): download the new code, copy
your `wrangler.toml` into the new `servidor` folder and run:

```
npm install
npm run banco
npm run deploy
```

`npm run banco` is safe to run again: it only creates what's missing and deletes nothing.

**Changing the code or the key:** run `npx wrangler secret put CODIGO` (or
`CHAVE_ENVIO`) again. It takes effect right away. If you change the upload key, update
it on each PC, in **Set up**.

**Looking at it on the Cloudflare dashboard:** **Workers & Pages → time** shows requests
and errors; **Storage & Databases → D1 → time → Console** lets you query the database,
e.g. `SELECT maquina, usuario FROM pcs`.

**Backup:**

```
npx wrangler d1 export time --remote --output=backup-time.sql
```

**Removing a PC from the list** (turn off sending on it first, or it comes back):

```
npx wrangler d1 execute time --remote --command="DELETE FROM dias WHERE maquina='PC-NAME'"
npx wrangler d1 execute time --remote --command="DELETE FROM pcs WHERE maquina='PC-NAME'"
```

**Turning it all off:** `npx wrangler delete` (deletes the Worker) and
`npx wrangler d1 delete time` (deletes the database and the data). Each PC's data stays in
its own CSV files.

---

## Common problems

| What shows up | What to do |
|---|---|
| "The server refused the upload key." | The PC's key differs from `CHAVE_ENVIO`. Check it in **Set up**. |
| "No connection to the server." | No internet or wrong address. Open the address in the PC's browser to test. It retries by itself every minute. |
| The dashboard keeps asking for the code | The code typed isn't the `CODIGO`. |
| The dashboard's "today" is the wrong day | Fix `FUSO` in `wrangler.toml` and run `npm run deploy`. |
| A PC disappears or mixes with another | Two PCs with the same Windows name. Rename one in **Settings → System → About**. |
| The address doesn't open right after publishing | Wait 1 or 2 minutes (certificate for the new address). |
| `Authentication error` when publishing | Run `npx wrangler login` again. |

---

## Security and privacy

- Traffic goes over **HTTPS**. The upload key can only send data; the code can only
  view it.
- The server keeps the same as each PC's CSV files: app names and times. **Never** window
  titles, screen contents or what is typed.
- The data stays in the D1 database on **your** account. Nobody else has access — not even
  Time's author.
- If you use it on other people's PCs (employees, for example), tell them in writing what
  is measured, as the [manual](manual.en.md#privacy) explains.

---

*Time · by Daniel 'dnyaary' Carvalho · [instagram.com/dnyaary](https://instagram.com/dnyaary) · MIT license*
