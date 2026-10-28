# Time 服务器（可选）

[Português](servidor.md) · [English](servidor.en.md) · **中文**

默认情况下，Time 不会向你的网络之外发送任何数据：手机仪表盘只能在与电脑相同的 Wi-Fi 中打开。当你需要更多功能时，可以使用服务器：

- **在任何地方**打开仪表盘——使用移动数据、在公司、在其他 Wi-Fi 下；
- **即使电脑关机**也能查看它的历史记录；
- **不依赖局域网**也能汇总多台电脑（不同的网络、不稳定的互联网）。

服务器是**你自己的**：它运行在你的 Cloudflare 账号上，只有你能访问这些数据。

---

## 工作原理

```
 电脑 1 ─┐                                    ┌─ 手机（移动数据或 Wi-Fi）
 电脑 2 ─┼──► https://time.yourdomain.com ◄───┤
 电脑 3 ─┘    （Cloudflare Worker + D1 数据库）└─ 任何电脑的浏览器
   使用 CHAVE_ENVIO 发送                         使用 CODIGO 查看
```

- 每台电脑**每分钟**发送一次有变化的日子的摘要：使用时间、空闲时间、程序和小时。从不发送窗口标题。
- 它总是发送**当天的总量**。重复发送只会替换，绝不会重复计算。
- **没有网络时**，电脑照常计时，并在网络恢复后补发全部数据。重启后，为保险起见会再发送一次历史记录。
- 服务器显示与 Time **相同的仪表盘**，包含所有发送数据的电脑。超过 2 分钟没有发送的电脑会显示为离线，并保留它已发送的数据。
- 电脑会**自动加入**列表：服务器通过每台电脑在 Windows 中的名称来识别它。

### 费用

正常使用免费。Cloudflare 的免费套餐每天允许 10 万次请求，并提供 5 GB 数据库。每台电脑每天大约上传 1,440 次（每分钟一次）；20 台电脑约为 29,000 次。一台电脑一年的历史记录不到 1 MB。

---

## 准备工作

1. **一个 Cloudflare 账号**——免费，在 [dash.cloudflare.com](https://dash.cloudflare.com/sign-up) 注册。
2. **Node.js**（LTS 版本）——在 [nodejs.org](https://nodejs.org) 下载。要确认是否已安装，打开 `cmd` 并输入 `node -v`：应显示一个版本号。
3. **Time 的 `servidor` 文件夹**——它包含在项目代码中。在 Time 的 GitHub 页面选择 **Code → Download ZIP** 并解压，或使用 `git clone`。
4. **（可选）一个托管在 Cloudflare 上的域名**，用于 `time.yourdomain.com` 这样的地址。没有域名时，服务器会使用 `time.your-account.workers.dev` 这样的免费地址。

---

## 分步操作

在 `servidor` 文件夹中打开 `cmd`（在资源管理器中点击地址栏，输入 `cmd` 并按 Enter）。每次运行一条命令。

### 1. 准备

```
copy wrangler.exemplo.toml wrangler.toml
npm install
```

`wrangler.toml` 是你的配置文件，只保存在你的电脑上（不会进入 Git）。`npm install` 会下载 Cloudflare 的工具（*wrangler*）。

### 2. 登录 Cloudflare

```
npx wrangler login
```

浏览器会打开 Cloudflare 页面，请求授权 **Wrangler**（"This application is owned and managed by Cloudflare"）。点击 **Authorize**。`cmd` 中会显示 `Successfully logged in`。

### 3. 创建数据库

```
npx wrangler d1 create time
```

它会显示一段包含 `database_id = "..."` 的内容。用记事本打开 `wrangler.toml`，把 `COLE-AQUI-O-ID-DO-BANCO` 替换为这个 id。

### 4. 地址和时区（在 `wrangler.toml` 中）

- **自己的地址（可选）：** 去掉 `routes` 一行开头的 `#`，并把 `time.seudominio.com` 换成你想要的地址。该域名必须在你的 Cloudflare 账号中。Cloudflare 会自动创建地址和证书（HTTPS）。
- **时区：** `FUSO = "America/Sao_Paulo"` 决定仪表盘中的"今天"是哪一天。如果你在其他时区，请修改（例如 `"Asia/Shanghai"`）。

### 5. 创建数据表

```
npm run banco
```

如果询问 `Ok to proceed?`，回答 **y**。应显示 `Processed 3 queries`。

### 6. 创建两个密钥

```
npx wrangler secret put CHAVE_ENVIO
```

出现 `Enter a secret value:` 时，输入（或粘贴）**上传密钥**并按 Enter。输入时屏幕上不会显示任何内容——这是正常的。如果询问是否创建名为 "time" 的 Worker，回答 **y**。

```
npx wrangler secret put CODIGO
```

同样操作，这次输入**读取码**。

| 密钥 | 用途 | 填写位置 |
|---|---|---|
| `CHAVE_ENVIO` | 只能**发送**数据 | 每台电脑上 Time 的 "Set up"（设置） |
| `CODIGO` | 只能**查看**仪表盘 | 手机上的访问码页面 |

上传密钥请使用较长的字符串（20 个字符以上，字母和数字），读取码则使用便于在手机上输入的格式（例如 `AB12-CD34`）。命令中写的是名称；值只在它询问时输入。

### 7. 发布

```
npm run deploy
```

最后会显示地址：`time.yourdomain.com (custom domain)` 或 `https://time.your-account.workers.dev`。新地址可能需要 1 到 2 分钟才能打开。

---

## 在电脑上开启

在每台装有 Time（1.2.0 或更新版本）的电脑上：

1. 打开完整窗口：**Ctrl+Shift+Alt+F2，然后按 1**。
2. 点击 **"Set up"**（设置）。
3. **服务器地址：** `https://time.yourdomain.com`（或 `workers.dev` 地址）。
4. **上传密钥：** `CHAVE_ENVIO`。
5. **OK**。

**"Send to the server"**（发送到服务器）会被勾选，几秒钟后下面一行会显示 **"Sent at 22:55 to time.yourdomain.com"**。设置保存在 `config.txt` 中：重启后无需重新设置。

## 在手机上打开

在浏览器中打开服务器地址并输入 `CODIGO`。该设备会记住它。使用 **"添加到主屏幕"** 可以把它变成一个应用。

---

## 维护

**更新服务器**（Time 发布新版本时）：下载新代码，把你的 `wrangler.toml` 复制到新的 `servidor` 文件夹中，然后运行：

```
npm install
npm run banco
npm run deploy
```

`npm run banco` 可以放心重复运行：它只会创建缺少的部分，不会删除任何内容。

**更换读取码或上传密钥：** 再次运行 `npx wrangler secret put CODIGO`（或 `CHAVE_ENVIO`），立即生效。如果更换了上传密钥，请在每台电脑的 **"Set up"** 中同步更新。

**在 Cloudflare 控制台查看：** **Workers & Pages → time** 显示请求和错误；**Storage & Databases → D1 → time → Console** 可以查询数据库，例如 `SELECT maquina, usuario FROM pcs`。

**备份：**

```
npx wrangler d1 export time --remote --output=backup-time.sql
```

**从列表中移除一台电脑**（请先在该电脑上取消发送，否则它会重新出现）：

```
npx wrangler d1 execute time --remote --command="DELETE FROM dias WHERE maquina='电脑名称'"
npx wrangler d1 execute time --remote --command="DELETE FROM pcs WHERE maquina='电脑名称'"
```

**全部关闭：** `npx wrangler delete`（删除 Worker）和 `npx wrangler d1 delete time`（删除数据库和数据）。每台电脑的数据仍保存在各自的 CSV 文件中。

---

## 常见问题

| 显示的内容 | 解决方法 |
|---|---|
| "The server refused the upload key."（服务器拒绝了上传密钥） | 电脑上的密钥与 `CHAVE_ENVIO` 不同。请在 **"Set up"** 中检查。 |
| "No connection to the server."（无法连接服务器） | 没有网络或地址错误。在电脑的浏览器中打开该地址进行测试。它每分钟会自动重试。 |
| 仪表盘一直要求输入访问码 | 输入的不是 `CODIGO`。 |
| 仪表盘中的"今天"日期不对 | 修改 `wrangler.toml` 中的 `FUSO` 并运行 `npm run deploy`。 |
| 某台电脑消失或与另一台混在一起 | 两台电脑在 Windows 中同名。在 **设置 → 系统 → 关于** 中重命名其中一台。 |
| 发布后地址无法立即打开 | 等待 1 到 2 分钟（新地址的证书生成中）。 |
| 发布时出现 `Authentication error` | 重新运行 `npx wrangler login`。 |

---

## 安全与隐私

- 传输通过 **HTTPS** 进行。上传密钥只能发送数据；读取码只能查看。
- 服务器保存的内容与每台电脑的 CSV 文件相同：程序名称和时长。**从不**包含窗口标题、屏幕内容或输入的文字。
- 数据保存在**你自己**账号的 D1 数据库中。其他任何人都无法访问——包括 Time 的作者。
- 如果在他人的电脑上使用（例如员工的电脑），请按照[使用手册](manual.zh-CN.md#隐私)的说明，以书面形式告知对方统计了哪些内容。

---

*Time · by Daniel 'dnyaary' Carvalho · [instagram.com/dnyaary](https://instagram.com/dnyaary) · MIT 许可证*
