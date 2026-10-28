# Time — by Daniel 'dnyaary' Carvalho

[English](manual.en.md) · **Português** · [中文](manual.zh-CN.md)

Programa para Windows que mede o **tempo de tela real** do PC: soma o tempo
enquanto a pessoa está usando a máquina e separa esse tempo por **programa e
site**.

O Time roda **sem ícone na bandeja do sistema**. A janelinha fica escondida e
só aparece pelo atalho global:

> **Segure Ctrl + Shift + Alt, aperte X e, em até 4 segundos, aperte 5.**

O mesmo atalho esconde a janela. Para encerrar o programa de verdade, use o **X**
da própria janela (o X salva os dados e fecha).

A interface segue o idioma do Windows (na janela) e do celular (no painel):
português quando o sistema está em português, inglês nos demais.

Dá também para acompanhar tudo pelo **celular**, na mesma rede Wi-Fi, sem nuvem
e sem conta — veja [Painel no celular](#painel-no-celular-rede-local).

---

## O que ele mede

- **Tempo ativo:** só conta quando houve mouse ou teclado dentro de um limite
  de tolerância. PC parado ou tela bloqueada não contam.
- **Por programa/site:** o tempo do momento vai para o app que está na frente
  (janela em primeiro plano), conforme as regras do `categorias.txt`.
- **Vídeo e reunião:** têm tolerância maior, então assistir sem mexer no mouse
  ainda conta como tempo de tela.

### O que ele NÃO faz

- Não registra quais teclas são pressionadas.
- Não captura a tela nem tira prints.
- Não salva o título das janelas — o título é usado só na hora, para comparar
  com as regras; nos arquivos vai apenas o nome da categoria.
- Não conta app em segundo plano (música tocando atrás) nem aba de navegador
  que não está na frente.

---

## Arquivos gerados (em `C:\Program Files\Time`)

| Arquivo | Para quê |
|---|---|
| `atividade_diaria.csv` | Tempo total por dia. Abre no Excel (separador `;`). |
| `atividade_diaria.txt` | Mesma coisa, em texto alinhado para ler no Bloco de Notas. |
| `apps_diario.csv` | Tempo por programa/site, por dia. Para o Excel. |
| `horas_diario.csv` | Tempo em cada hora do dia (0 a 23), por dia. Para o Excel. |
| `categorias.txt` | A lista do que contar e como nomear. Você edita este. |
| `config.txt` | Porta e código de acesso do painel do celular. |
| `pcs.txt` | Outros PCs do painel, um por linha: `nome; codigo; maquina; ip:porta`. |
| `pcs_dados.json` | Últimos dados recebidos de cada PC do `pcs.txt`, usados enquanto ele está offline. |

O caminho é sempre `C:\Program Files\Time`. Se a pasta não existir, o programa
cria na primeira execução — e essa primeira execução **pede uma elevação (UAC)
uma única vez**, porque o Windows não deixa gravar em `Program Files` sem
administrador. Depois disso ele grava sozinho, sem pedir mais nada (inclusive
quando sobe junto com o Windows).

Se você recusar o UAC, o programa continua funcionando em
`%LocalAppData%\Time` e avisa isso quando a janela é aberta; basta abrir o
`Time.exe` como administrador uma vez para voltar ao caminho certo.

### Colunas do `atividade_diaria.csv`

```
Data;Usuario;Maquina;Primeira atividade;Ultima atividade;Tempo ativo;Segundos;Limite inatividade (s);Parado (s)
2026-10-22;daniel;PC-TI;08:12:40;17:41:05;06:48:12;24492;30;3120
```

`Parado (s)` é o tempo em que o PC ficou ligado, com o Time rodando, mas sem
ninguém usando. Não entra no tempo ativo, e a pausa não conta como parado.

### Colunas do `apps_diario.csv`

```
Data;Usuario;Maquina;Item;Tempo;Segundos
2026-10-22;daniel;PC-TI;CorelDRAW;03:12:40;11560
```

### Colunas do `horas_diario.csv`

```
Data;Usuario;Maquina;Hora;Segundos
2026-10-22;daniel;PC-TI;14;3240
```

Uma linha por hora com uso (`14` = das 14:00 às 14:59). Num Windows em inglês, os
nomes das colunas saem em inglês.

---

## A lista `categorias.txt`

Uma regra por linha, no formato:

```
tipo: texto = Nome que aparece | tolerância
```

**Tipos:**

- `site:` — procura o texto no título da aba, **só quando a janela é um
  navegador** (Chrome, Edge, Firefox, Opera, Brave, Vivaldi).
- `titulo:` — procura o texto no título de **qualquer** janela. Use com cuidado.
- `programa:` — compara com o começo do nome do processo, sem `.exe`
  (veja o nome em Gerenciador de Tarefas → Detalhes).

As palavras em inglês (`title:` e `program:`) também funcionam.

**Regras de funcionamento:**

- A **primeira regra que bater vence**, então os sites ficam em cima dos
  navegadores. Por isso YouTube aberto no Chrome conta como "YouTube", e não
  como "Navegador".
- O que não bater em nenhuma regra conta como **"Outros"**.
- O próprio **Time** é a exceção: com a janela aberta, ele aparece na lista como
  **"Time"** (não precisa de regra no `categorias.txt`).
- Ao salvar o arquivo, o programa **recarrega sozinho** em alguns segundos, sem
  reiniciar.

**Grupos (opcional):**

Uma linha com o nome entre colchetes coloca as regras de baixo naquele grupo, até
o próximo grupo. Uma linha `[]` volta para "sem grupo". O painel soma o tempo de
cada grupo na seção **Por grupo**.

```
[Redes sociais]
site: Instagram = Instagram
site: TikTok = TikTok | 180

[Trabalho]
programa: CorelDRW = CorelDRAW
programa: EXCEL = Excel

[]
programa: chrome = Navegador
```

O `categorias.txt` novo já vem com grupos (Redes sociais, Vídeo e música,
Comunicação, Inteligência artificial, Trabalho, Compras e Jogos). Os grupos não
ficam gravados no histórico: mudou o grupo de um programa, o painel já soma os
dias antigos no grupo novo.

**Foco: produtivo ou distração (opcional):**

No fim do nome do grupo, `| produtivo` ou `| distração` diz o que aquele tempo
significa para você. O **Resumo do dia** do painel separa o tempo em
produtivo, distração e neutro, e a janela mostra a linha
"Produtivo 5h 10min · Distração 1h 20min".

```
[Trabalho | produtivo]
programa: devenv = Visual Studio

[Redes sociais | distração]
site: Instagram = Instagram

[Comunicação]
programa: WhatsApp = WhatsApp
```

Grupo sem marca (como Comunicação acima) e programa sem grupo contam como
**neutro**. O arquivo padrão já vem com Trabalho e Inteligência artificial como
produtivos, e Redes sociais, Vídeo e música, Compras e Jogos como distração.
Quem decide é você: troque a marca à vontade — o painel recalcula todos os dias.
Se nenhum grupo tiver marca, os grupos com os nomes padrão usam essa marcação
padrão; use `| neutro` para deixar um grupo de fora.

**Tolerância (opcional, no fim da linha):**

É quanto tempo sem mexer no mouse/teclado ainda conta para aquele item. Sem o
`|`, usa o padrão de 30 s. Serve para vídeo e reunião, onde a pessoa assiste sem
tocar em nada.

```
site: YouTube = YouTube | 600      (continua contando até 10 min parado)
site: TikTok = TikTok | 180        (3 min)
programa: Zoom = Zoom | 900         (15 min)
```

Padrões já configurados: vídeo 10 min, vídeo curto 3 min, reunião 15 min, todo o
resto 30 s.

**Se um programa cair em "Outros":** abra o Gerenciador de Tarefas → Detalhes,
veja o nome do processo e adicione `programa: Nome = Como aparece`.

---

## Compilar e publicar

Requer o .NET 10 SDK (ou Visual Studio). Na pasta com `Program.cs` e
`Time.csproj`:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

O executável único fica em:

```
bin\Release\net10.0-windows\win-x64\publish\Time.exe
```

É autocontido: roda em qualquer PC com Windows, sem instalar o .NET. Para testar
rápido antes de publicar, use `dotnet run` na pasta.

O `.pdb` gerado junto é só para depuração; pode entregar apenas o `.exe`.

---

## Instalação em cada PC

1. Rode o `Time.exe` de onde baixou. **Nada aparece na tela** — ele fica rodando
   escondido.
2. Na primeira execução ele **se copia para `C:\Program Files\Time\Time.exe`** e
   passa a rodar de lá. A pasta de onde você baixou pode ser apagada depois: o
   programa não depende mais dela.
3. O Windows pede **duas elevações (UAC)**: uma para criar e liberar a pasta
   `C:\Program Files\Time` (onde ficam os dados) e outra para liberar o firewall.
4. Aperte o atalho **Ctrl+Shift+Alt+X e depois 5** para abrir a janela: ela mostra
   só o nome **Time** e o botão de minimizar.
5. Com ela aberta, aperte **Ctrl+Shift+Alt+F2 e depois 1** para ver as informações
   completas e marcar **"Iniciar com o Windows"**.

O mesmo `Time.exe` serve para todos os PCs — é um arquivo só, autocontido, sem
instalador. Como ele não tem assinatura digital, o Windows pode avisar
"O Windows protegeu o computador" na primeira vez: clique em **Mais informações**
e **Executar assim mesmo**. Enviar por WhatsApp ou e-mail costuma ser bloqueado
por ser `.exe`; use um pen drive, uma pasta da rede ou um link de nuvem.

Depois dessa primeira elevação, o programa roda sem administrador e não pede
mais nada. Se a pasta `C:\Program Files\Time` já foi liberada por outro
usuário deste PC, nem o pedido aparece.

"Iniciar com o Windows" grava na chave do próprio usuário, então cada usuário
marca uma vez no seu login. Quando o Windows abre o programa, ele **não** pede
UAC (o pedido só acontece na execução manual).

**Importante:** o programa mora em `C:\Program Files\Time\Time.exe`. Se você
mover esse arquivo, rode-o de novo: ele se copia de volta para lá e a
inicialização volta a apontar certo. Para atualizar, feche o Time pelo **X da
janela completa** antes de rodar o `.exe` novo — ele se instala por cima.

---

## Uso no dia a dia

- **Atalho Ctrl+Shift+Alt+X e depois 5:** abre a janela mínima (canto inferior
  direito): só o nome **Time** e o botão **—** de minimizar. Minimizar esconde
  de novo; apertando o mesmo atalho com ela aberta, também esconde.
- **Atalho Ctrl+Shift+Alt+F2 e depois 1:** com a janela aberta, mostra as
  informações completas (tempo, programas, dias anteriores, link do painel). Se
  nada estiver aberto, ele já abre essa janela completa. Você tem até **4
  segundos** para a segunda tecla e pode soltar Ctrl/Shift/Alt depois da
  primeira; qualquer outra tecla no meio cancela a sequência.
- **X da janela completa:** encerra o programa, salvando os dados. Para deixar
  rodando, use o atalho em vez do X. A janela mínima não tem X.
- **Linha de estado:** **● Contando** quando o PC está em uso, **○ Sem uso agora**
  quando passou a tolerância sem mexer no mouse/teclado, e **❚❚ Pausado** durante
  uma pausa. Abaixo, **Ligado sem uso** mostra quanto tempo o PC ficou ligado hoje
  sem ninguém usando (aparece a partir de 1 minuto; no painel também).
- **Botões da janela:**
  - **Pausar:** para a contagem por **15 minutos**, **1 hora** ou **até eu
    retomar**, mesmo com o PC em uso — para um intervalo, ou quando outra pessoa
    vai usar o PC. Durante a pausa o botão vira **Retomar** e o painel do celular
    mostra "Pausado até 15:30". A pausa não fica gravada: fechar o Time ou
    reiniciar o PC volta a contar.
  - **Pasta dos dados:** abre `C:\Program Files\Time` no Explorador.
  - **Categorias:** abre o `categorias.txt` no Bloco de Notas.
  - **Iniciar com o Windows:** liga/desliga a inicialização automática.
  - **Enviar para o servidor** e **Configurar:** mandam os dados para o seu
    servidor, se você tiver um (veja [Servidor](#servidor-opcional)). A linha de
    baixo mostra o último envio ou o motivo de não ter enviado.
  - **Acessar pelo celular:** liga o painel na rede local e mostra o link para
    copiar (veja a seção [Painel no celular](#painel-no-celular-rede-local)).
- **Abrir o `Time.exe` de novo** (duplo clique) não cria uma segunda cópia:
  ele só mostra a janela mínima do que já está rodando. É a reserva caso o
  atalho falhe em algum PC.

---

## Painel no celular (rede local)

O Time pode mostrar os mesmos dados no navegador do celular. Não usa nuvem,
não precisa de conta e não manda nada para fora: é o próprio programa que serve
uma página na rede local.

### Como ligar

1. Abra a janela (atalho) e marque **"Acessar pelo celular (rede local)"**.
2. Na primeira vez o Windows pede permissão (UAC) para liberar as portas no
   firewall (a do painel e a da descoberta entre PCs). Aceite — é só essa vez.
3. Copie o link com o botão **Copiar**. Ele sai assim:
   `http://192.168.0.10:8787/?k=ABCD-EFGH` (o IP e o código são os do seu PC).
4. Abra esse link no celular, na **mesma rede Wi-Fi**.
5. No celular, use **"Adicionar à tela de início"** para virar um app.

No próprio PC, o mesmo painel abre em `http://localhost:8787/?k=SEU-CODIGO`.

Se o PC estiver em **duas redes** (cabo e Wi-Fi, ou duas redes na empresa), a
janela mostra um segundo link, **"Na outra rede"**. Use o link da rede em que o
celular (ou o outro PC) está. Placas virtuais (Hyper-V, WSL, VirtualBox, VMware)
são ignoradas.

O fim do link (`?k=...`) é o **código de acesso**. Quem abrir o endereço sem ele
vê apenas a tela de código; os dados só aparecem com o código certo.

### O que aparece

- Tempo de **hoje** e o período (primeira e última atividade).
- **Por PC**: quanto cada computador usou hoje e se está online. Toque em um PC
  para ver só o que ele está usando.
- **Resumo do dia**: a frase pronta, por exemplo "10h ligado: 3h produtivo, 3h
  de distração, 2h neutro e 2h ligado sem uso", com uma barra e a porcentagem de
  cada parte: produtivo (verde), distração (vermelho), neutro (cinza) e ligado sem
  uso (listrado). Sem grupos marcados, aparece só "em uso" e "sem uso".
- **Por grupo**: o tempo de hoje somado por grupo do `categorias.txt` (só aparece
  quando há grupos). A barra de cada grupo segue a cor do foco.
- **Top programas** de hoje, com uma barra fina do tamanho de cada um. Toque em um
  programa para abrir o **detalhe dele**: o tempo de hoje, o gráfico dos últimos 14
  dias, a semana, o mês e os dias em que foi usado.
- **Por hora**: 24 barras, uma para cada hora do dia, com a hora de maior uso em
  destaque. Aparece também quando você abre um dia anterior.
- **Últimos 14 dias** em um gráfico de linha, com a média do período. A linha é
  verde na base e vai ficando âmbar e vermelha conforme sobe: quanto mais alto o
  dia, mais quente a cor.
- **Esta semana** e **Este mês**: tempo total desde segunda-feira e desde o dia 1º,
  com a média por dia de uso e a comparação com o mesmo trecho da semana e do mês
  anteriores (por exemplo, "▲ 12% que a semana passada").
- **Resumo da semana / do mês**: as abas **Semana** (de segunda a domingo) e
  **Mês** escolhem o período, e as setas **‹ ›** andam para trás e para a frente —
  dá para ver qualquer semana ou mês do histórico (o mês retrasado, por exemplo).
  Na segunda-feira ele já abre na semana que acabou de fechar. Mostra a frase do
  período inteiro, uma barra por dia na semana ou por semana no mês (toque para
  abrir), a comparação com o período anterior — no período em andamento, até o
  mesmo dia — e os destaques: dia mais produtivo, programa mais usado, maior
  distração e média por dia de uso.
- **Dias anteriores**: abaixo do gráfico, o resumo de cada dia (data e tempo
  total, só isso). Toque em um dia para abrir o gráfico com aquele dia marcado e
  a lista completa dos programas usados nele; **← Voltar** retorna para hoje.
- **Relatório**: o botão **Semana** (ou **Mês**) baixa o resumo do período que está
  aberto no resumo acima: um dia por linha com ligado, em uso, produtivo,
  distração, neutro e sem uso, o total do período e cada programa com grupo e foco.
  Os botões **Dias**, **Programas** e **Horas** baixam o histórico em
  planilhas CSV, para abrir no Excel. Na visão de todos os PCs, vem o histórico de
  todos os PCs da lista; dentro de um PC, só daquele PC.
- **Adicionar ou remover PCs**, no fim da página, para juntar os outros computadores.
- **Na rede agora**: os outros Time ligados na mesma rede, prontos para adicionar.
- Atualiza sozinho a cada 30 segundos.

O visual é sem cantos arredondados, feito para ler rápido no celular. Ele segue o
tema do celular (claro ou escuro); o botão ☀/☾ no topo troca o tema, e a escolha
fica guardada naquele aparelho. O
painel continua funcionando com a janela escondida — só precisa do PC ligado e
do Time rodando.

### Vários PCs

Cada PC roda o próprio Time. Para ver todos em **um link só**, escolha um deles
como principal (o que fica mais tempo ligado).

No outro PC é só repetir a instalação acima (o mesmo `Time.exe` serve para
todos) e marcar **Acessar pelo celular (rede local)** na janela dele — é isso que
deixa o PC principal ler os dados. O `pcs.txt` fica só no PC principal.

**O painel acha os outros PCs sozinho.** No PC principal — em `localhost` ou no
celular — abra **Adicionar ou remover PCs**, no fim da página. Os Time que
estão ligados na mesma rede aparecem em **Na rede agora**, com o IP ao lado:

```
NA REDE AGORA
DESKTOP-NOTE                      192.168.0.51
[ código do outro PC ]           [ ADICIONAR ]
```

O nome já vem com o da máquina, mas você pode trocá-lo por "Notebook",
"Escritório" etc. antes de adicionar. Digite o **código de acesso** daquele PC
(o mesmo `?k=` do link que a janela dele mostra) e toque em **Adicionar**: é uma
vez só, e o painel confirma na própria rede que o código está certo antes de
aceitar.

Também dá para cadastrar pelo link: cole o link completo do outro PC
(`http://192.168.0.51:8787/?k=IJKL-MNOP`) no campo de baixo, com um nome, e toque
em **Adicionar**. Vale na hora, sem reiniciar. O **×** ao lado de cada nome tira
o PC da lista.

**Quando o IP muda, o painel se ajeita sozinho.** Cada Time avisa na rede a
cada 10 segundos (sem mandar nenhum dado seu: só o nome da máquina e a porta).
O principal troca o endereço do PC assim que o aviso aparece,
depois de confirmar que quem respondeu tem o código — ninguém consegue se passar
pelo nome de outro PC. O IP guardado no `pcs.txt` é só uma reserva, para quando a
rede não deixa os avisos passarem.

O `pcs.txt` da pasta de dados guarda uma linha por PC:

```
nome; codigo; maquina; ip:porta
Notebook; IJKL-MNOP; DESKTOP-NOTE; 192.168.0.51:8787
```

Dá para editar à mão, mas não é preciso: o que você adiciona pela página é
gravado ali automaticamente.

O painel do PC principal soma o tempo de todos e mostra a lista **Por PC**. Se um
PC não responder (desligado, rede oscilando), ele aparece como `offline` com o
último tempo que mandou, e esse tempo continua na soma — o total não cai e volta a
cada queda da rede. Quando ele volta, o número se atualiza sozinho. Os PCs
precisam estar na mesma rede e com o painel ligado — em cada um, na primeira vez,
o Windows pede a liberação do firewall (o mesmo UAC de quando você ligou o
painel): ele libera a porta do painel e a da descoberta de uma vez.

### Ver um PC só

Toque em qualquer PC da lista **Por PC** (tem uma seta `›` no fim da linha). O
painel passa a mostrar só aquele computador:

- o tempo de hoje, com o período (primeira e última atividade);
- o que ele está usando agora, no topo (`Contando · VS Code`);
- os programas dele, com o tempo de cada um;
- o gráfico dos últimos 14 dias daquele PC.

O **← Todos os PCs** volta para a soma. O endereço fica com `&pc=...` no fim,
então recarregar a página mantém o mesmo PC aberto. PC desligado não abre — ele
aparece como `offline` na lista e o painel não tem como consultar o que ele usou.

### `config.txt`

| Chave | O que é |
|---|---|
| `porta` | Porta do painel (padrão `8787`). |
| `codigo` | Senha do painel. Troque para bloquear os celulares já pareados. |
| `api` | `1` = painel ligado ao abrir o programa; `0` = desligado. |
| `firewall` | Porta que o Windows já liberou (`0` = nenhuma). |
| `firewalludp` | Porta da descoberta na rede que o Windows já liberou (`0` = nenhuma). |

Depois de editar o arquivo, **feche e abra o Time** para valer.

### Se o celular não abrir

- Confirme que o celular está na **mesma rede** do PC.
- Se o Windows mostrar o aviso "Permitir que o Time se comunique...", clique em
  **Permitir**. Cancelar esse aviso cria uma regra de **bloqueio** para o
  `Time.exe` - e bloqueio ganha de liberação, então o celular fica de fora mesmo
  com a porta liberada. Para conferir: **Firewall do Windows com Segurança
  Avançada** → **Regras de Entrada** → apague qualquer regra de bloqueio com o
  nome do `Time.exe`.
- O Time cria três regras sozinho: por porta (TCP 8787 e UDP 8788) e por
  programa. A regra por programa é a que evita esse aviso do Windows.
- A rede do Windows pode ficar como **Particular** ou **Pública**: as regras valem
  nos dois casos.
- Confira se o PC está ligado e o Time rodando.
- O endereço é **HTTP**, não HTTPS — é normal o navegador não mostrar o cadeado.
- Se **Na rede agora** não mostrar o outro PC, veja se o painel dele está ligado,
  se a rede do Windows está como **Particular** e se o firewall foi liberado nele
  (abra a janela dele uma vez e aceite o UAC).

### Segurança

O painel é para a rede de casa ou do escritório. O código protege os dados, mas
o tráfego não é criptografado (não tem TLS). **Não abra essa porta na internet.**

A descoberta na rede também não manda o código, nem em forma de hash: o aviso
leva só o nome da máquina e a porta. Quando o endereço de um PC muda, o painel
confirma a identidade com um desafio respondido por HMAC — sem o código, ninguém
consegue se passar por aquele PC.

---

## Servidor (opcional)

Para ver o painel fora da rede (no 4G, em outro Wi-Fi) e continuar vendo o
histórico de um PC desligado, cada PC pode mandar os dados para um servidor seu,
na sua conta da Cloudflare. O passo a passo para criar o servidor está no
[guia do servidor](servidor.md); o código fica na pasta `servidor/`.

- **Em cada PC:** **Configurar** → endereço do servidor (ex.:
  `https://time.seudominio.com`) e a **chave de envio** → OK. A caixa **Enviar para
  o servidor** fica marcada. Os dois ficam no `config.txt` (`servidor`, `chave`,
  `enviar`).
- **Envio:** a cada minuto, o PC manda os dias que mudaram. Na primeira vez (ou ao
  trocar de servidor) manda o histórico inteiro, em lotes. Ele manda sempre o total
  do dia, então reenviar só substitui — nunca soma em dobro.
- **Sem internet:** a contagem continua normal no PC. A linha de baixo mostra "Sem
  conexão com o servidor" e, quando a internet volta, tudo é enviado.
- **No celular:** abra o endereço do servidor e digite o **código de leitura** (o
  `CODIGO` criado no servidor). É o mesmo painel, com todos os PCs que enviam. Um PC
  que parou de enviar há mais de 2 minutos aparece como offline, com os dados que
  já mandou.
- **Segurança:** a chave de envio só serve para mandar dados; o código de leitura só
  serve para ver. O tráfego vai por HTTPS. O servidor guarda o mesmo que os CSV: nome
  dos programas e tempos, nunca o título das janelas.

O painel da rede local continua funcionando igual, com ou sem servidor.

---

## Limitações

- `GetLastInputInfo` enxerga só a sessão interativa atual; não é um serviço
  global para todas as sessões do Windows.
- O programa não sabe se um vídeo está tocando ou pausado. Um vídeo pausado na
  frente conta até completar a tolerância do item.
- Começa a contar a partir do momento em que é iniciado; não recupera o tempo
  antes disso.
- O atalho depende do Windows entregar o teclado ao programa (gancho de
  teclado). Se alguma política de segurança do PC bloquear isso, o atalho não
  funciona; nesse caso, abra o `Time.exe` de novo para ver a janela.
- Gravar em `C:\Program Files` exige administrador. Na primeira execução o
  programa pede UAC uma vez para liberar a pasta; se o pedido for recusado, ele
  cai em `%LocalAppData%\Time` e avisa na janela.
- Sem admin, qualquer pessoa pode fechar o programa pelo X ou pelo Gerenciador
  de Tarefas. Nesse caso, aquele dia fica com dados incompletos.
- O painel do celular só funciona com o PC ligado, o Time rodando e o celular
  na mesma rede. Ele é HTTP simples (sem TLS), então não deve ser exposto na
  internet.
- Os dados por programa dependem de o Windows informar qual janela está na
  frente. Programas abertos atrás, ou uma aba que não está ativa, não contam.
- A descoberta dos PCs usa aviso por broadcast na rede local. Redes de empresa
  que bloqueiam broadcast, ou Wi-Fi com isolamento de clientes, não deixam os PCs
  se acharem; nesse caso, cadastre cada um pelo link.

---

## Privacidade

O Time foi feito para **uso pessoal**: medir o próprio tempo no PC e ter mais
foco. Os números são seus e ficam no seu computador.

- Não registra teclas, não tira prints e não salva títulos de janela.
- O painel do celular não sai da rede local e exige o código de acesso.
- Os arquivos ficam em `C:\Program Files\Time`.

Se um dia usar com outra pessoa ou com a equipe, avise **por escrito** o que é
medido (tempo por app) e o que não é (teclas, prints, conteúdo). Monitoramento
oculto de terceiros costuma ser considerado prova ilícita e gera dano moral na
Justiça do Trabalho. Use os números como referência de rotina, não como nota de
desempenho: tempo na frente do PC não é produtividade — reunião, telefone e
espera de render não aparecem como atividade.

Discreto para não incomodar — e, com terceiros, combinado com quem usa.

---

*Time · by Daniel 'dnyaary' Carvalho · [instagram.com/dnyaary](https://instagram.com/dnyaary) · licença MIT*
