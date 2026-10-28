# Servidor do Time (opcional)

**Português** · [English](servidor.en.md) · [中文](servidor.zh-CN.md)

Por padrão o Time não manda nada para fora da sua rede: o painel do celular só abre no
mesmo Wi-Fi do PC. O servidor é para quando você quer mais do que isso:

- abrir o painel **de qualquer lugar** — no 4G, no trabalho, em outro Wi-Fi;
- ver o histórico de um PC **mesmo com ele desligado**;
- juntar vários PCs **sem depender da rede local** (redes diferentes, internet oscilando).

O servidor é **seu**: roda na sua conta da Cloudflare, e só você tem acesso aos dados.

---

## Como funciona

```
 PC 1 ─┐                                      ┌─ celular (4G ou Wi-Fi)
 PC 2 ─┼──► https://time.seudominio.com ◄─────┤
 PC 3 ─┘    (Cloudflare Worker + banco D1)    └─ navegador de qualquer PC
   envia com a CHAVE_ENVIO                       vê com o CODIGO
```

- **A cada minuto**, cada PC manda o resumo dos dias que mudaram: tempo de uso, tempo
  parado, programas e horas. Nunca o título das janelas.
- Ele manda sempre **o total do dia**. Reenviar só substitui — nunca soma em dobro.
- **Sem internet**, o PC continua contando normalmente e manda tudo quando a conexão
  volta. Depois de reiniciar, ele reenvia o histórico uma vez, para garantir.
- O servidor mostra **o mesmo painel** do Time, com todos os PCs que enviam. Um PC
  que parou de enviar há mais de 2 minutos aparece como offline, com o que já mandou.
- Os PCs **entram sozinhos** na lista: o servidor reconhece cada um pelo nome dele no
  Windows.

### Quanto custa

Nada, no uso normal. O plano grátis da Cloudflare aceita 100 mil requisições por dia e
5 GB de banco. Cada PC faz cerca de 1.440 envios por dia (um por minuto); 20 PCs ficam
em uns 29 mil. Um ano de histórico de um PC ocupa menos de 1 MB.

---

## O que você precisa

1. **Uma conta na Cloudflare** — grátis, em [dash.cloudflare.com](https://dash.cloudflare.com/sign-up).
2. **Node.js** (versão LTS) — em [nodejs.org](https://nodejs.org). Para conferir, abra o
   `cmd` e digite `node -v`: tem que aparecer um número de versão.
3. **A pasta `servidor` do Time** — ela vem no código do projeto. No GitHub do Time,
   **Code → Download ZIP** e extraia, ou use `git clone`.
4. **(Opcional) Um domínio na Cloudflare**, para ter um endereço como
   `time.seudominio.com`. Sem domínio, o servidor fica num endereço grátis do tipo
   `time.sua-conta.workers.dev`.

---

## Passo a passo

Abra o `cmd` dentro da pasta `servidor` (no Explorador, clique na barra de endereço,
digite `cmd` e Enter). Rode um comando de cada vez.

### 1. Preparar

```
copy wrangler.exemplo.toml wrangler.toml
npm install
```

O `wrangler.toml` é a sua configuração. Ele fica só no seu PC (não vai para o Git).
O `npm install` baixa a ferramenta da Cloudflare (o *wrangler*).

### 2. Entrar na Cloudflare

```
npx wrangler login
```

O navegador abre na Cloudflare pedindo para autorizar o **Wrangler** ("This application
is owned and managed by Cloudflare"). Clique em **Authorize**. No `cmd` aparece
`Successfully logged in`.

### 3. Criar o banco

```
npx wrangler d1 create time
```

Ele mostra um bloco com `database_id = "..."`. Abra o `wrangler.toml` no Bloco de Notas
e troque `COLE-AQUI-O-ID-DO-BANCO` por esse id.

### 4. Endereço e fuso (no `wrangler.toml`)

- **Endereço próprio (opcional):** tire o `#` do começo da linha `routes` e troque
  `time.seudominio.com` pelo endereço que você quer. O domínio precisa estar na sua
  conta da Cloudflare. A Cloudflare cria o endereço e o certificado (HTTPS) sozinha.
- **Fuso:** `FUSO = "America/Sao_Paulo"` decide qual é o dia de "hoje" no painel.
  Troque se você estiver em outro fuso (ex.: `"Europe/Lisbon"`, `"America/New_York"`).

### 5. Criar as tabelas

```
npm run banco
```

Se perguntar `Ok to proceed?`, responda **y**. Deve aparecer `Processed 3 queries`.

### 6. Criar as duas chaves

```
npx wrangler secret put CHAVE_ENVIO
```

Quando aparecer `Enter a secret value:`, digite (ou cole) a **chave de envio** e dê
Enter. Nada aparece enquanto você digita — é normal. Se perguntar se quer criar o
Worker "time", responda **y**.

```
npx wrangler secret put CODIGO
```

Do mesmo jeito, agora com o **código de leitura**.

| Chave | Para quê | Onde vai |
|---|---|---|
| `CHAVE_ENVIO` | só **mandar** dados | em cada PC, no "Configurar" do Time |
| `CODIGO` | só **ver** o painel | no celular, na tela de código |

Use uma chave de envio longa (20 caracteres ou mais, letras e números) e um código fácil
de digitar no celular (ex.: `AB12-CD34`). O nome vai no comando; o valor, só quando ele
pedir.

### 7. Publicar

```
npm run deploy
```

No fim aparece o endereço: `time.seudominio.com (custom domain)` ou
`https://time.sua-conta.workers.dev`. Um endereço novo pode levar 1 ou 2 minutos para
abrir.

---

## Ligar nos PCs

Em cada PC com o Time (versão 1.2.0 ou mais nova):

1. Abra a janela completa: **Ctrl+Shift+Alt+F2 e depois 1**.
2. Clique em **Configurar**.
3. **Endereço do servidor:** `https://time.seudominio.com` (ou o `workers.dev`).
4. **Chave de envio:** a `CHAVE_ENVIO`.
5. **OK**.

A caixa **Enviar para o servidor** fica marcada e, em alguns segundos, a linha de baixo
mostra **"Enviado às 22:55 para time.seudominio.com"**. Fica salvo no `config.txt`: não
precisa fazer de novo depois de reiniciar.

## Abrir no celular

Abra o endereço do servidor no navegador e digite o `CODIGO`. Ele fica guardado naquele
aparelho. Use **"Adicionar à tela de início"** para virar um app.

---

## Manutenção

**Atualizar o servidor** (quando sair uma versão nova do Time): baixe o código novo,
copie o seu `wrangler.toml` para a pasta `servidor` nova e rode:

```
npm install
npm run banco
npm run deploy
```

O `npm run banco` é seguro de rodar de novo: ele só cria o que estiver faltando e não
apaga nada.

**Trocar o código ou a chave:** rode de novo `npx wrangler secret put CODIGO` (ou
`CHAVE_ENVIO`). Vale na hora. Se trocar a chave de envio, atualize em cada PC, no
**Configurar**.

**Ver no painel da Cloudflare:** em **Workers & Pages → time** ficam os acessos e os
erros; em **Storage & Databases → D1 → time → Console** dá para consultar o banco, por
exemplo `SELECT maquina, usuario FROM pcs`.

**Backup:**

```
npx wrangler d1 export time --remote --output=backup-time.sql
```

**Tirar um PC da lista** (desmarque o envio nele antes, senão ele volta):

```
npx wrangler d1 execute time --remote --command="DELETE FROM dias WHERE maquina='NOME-DO-PC'"
npx wrangler d1 execute time --remote --command="DELETE FROM pcs WHERE maquina='NOME-DO-PC'"
```

**Desligar tudo:** `npx wrangler delete` (apaga o Worker) e `npx wrangler d1 delete time`
(apaga o banco e os dados). Os dados de cada PC continuam nos CSV de cada um.

---

## Problemas comuns

| O que aparece | O que fazer |
|---|---|
| "O servidor recusou a chave de envio." | A chave do PC é diferente da `CHAVE_ENVIO`. Confira no **Configurar**. |
| "Sem conexão com o servidor." | Sem internet ou endereço errado. Abra o endereço no navegador do PC para testar. Ele tenta de novo sozinho a cada minuto. |
| O painel pede o código toda hora | O código digitado não é o `CODIGO`. |
| O "hoje" do painel vira o dia errado | Ajuste o `FUSO` no `wrangler.toml` e rode `npm run deploy`. |
| Um PC some ou mistura com outro | Dois PCs com o mesmo nome no Windows. Renomeie um em **Configurações → Sistema → Sobre**. |
| O endereço não abre logo depois de publicar | Espere 1 ou 2 minutos (certificado do endereço novo). |
| `Authentication error` ao publicar | Rode `npx wrangler login` de novo. |

---

## Segurança e privacidade

- O tráfego vai por **HTTPS**. A chave de envio só serve para mandar dados; o código
  só serve para ver.
- O servidor guarda o mesmo que os CSV de cada PC: nome dos programas e tempos. **Nunca**
  o título das janelas, o conteúdo da tela ou o que é digitado.
- Os dados ficam no banco D1 da **sua** conta. Ninguém mais tem acesso — nem o autor do
  Time.
- Se usar em PCs de outras pessoas (funcionários, por exemplo), avise por escrito o que é
  medido, como explica o [manual](manual.md#privacidade).

---

*Time · by Daniel 'dnyaary' Carvalho · [instagram.com/dnyaary](https://instagram.com/dnyaary) · licença MIT*
