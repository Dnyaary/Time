# Mudanças

## 1.1.0 (2026-10-27)

### Foco
- Grupos marcados como produtivo ou distração no `categorias.txt`: `[Trabalho | produtivo]`, `[Redes sociais | distração]`. As regras padrão já vêm marcadas.
- Painel: barra de foco com o tempo e a porcentagem de produtivo, distração e neutro; as barras dos grupos seguem a mesma cor.
- Janela: linha "Produtivo · Distração" com o tempo de hoje.

### Resumo do dia, da semana e do mês
- Painel: "Resumo do dia" com a frase pronta ("10h ligado: 3h produtivo, 3h de distração, 2h neutro e 2h ligado sem uso"), barra e porcentagens.
- Painel: resumo da semana e do mês, com setas para voltar a qualquer semana ou mês do histórico (na segunda-feira abre na semana que fechou): a frase do período, uma barra por dia (ou por semana, no mês), comparação com o período anterior e destaques (dia mais produtivo, programa mais usado, maior distração, média por dia).
- Relatório: botão **Semana** (ou **Mês**), que baixa o resumo do período aberto em planilha CSV — um dia por linha (ligado, em uso, produtivo, distração, neutro e sem uso), o total e cada programa com grupo e foco.

### Pausar
- Botão **Pausar** na janela: 15 minutos, 1 hora ou até retomar. O painel mostra "Pausado até…" e o PC aparece em amarelo na lista.

### Tempo parado
- Conta também o tempo em que o PC ficou ligado sem ninguém usando ("Ligado sem uso"), na janela, no painel e numa coluna nova do `atividade_diaria.csv`. A pausa não entra nessa conta.

### Rede
- PC ligado em duas redes (cabo e Wi-Fi, ou duas redes na empresa): a janela mostra um link para cada uma.
- O link prefere a placa do Wi-Fi ou do cabo, sem confundir com placas virtuais (Hyper-V, WSL, VirtualBox, VMware), e se atualiza sozinho quando a rede muda.
- Quando um PC da lista não responde, o painel continua somando os últimos dados que ele mandou e mostra o PC como offline. O total não cai e volta a cada oscilação da rede.

### Projeto
- Código separado em arquivos menores (`Contador.*.cs`, `Janelas.cs`, `Modelos.cs`…), sem mudar o funcionamento.
- Modelos de issue no GitHub e aviso do navegador ao baixar o `.exe` no README.

## 1.0.0 (2026-10-27)

Primeira versão, sob a licença MIT.

### Contagem
- Conta o tempo de uso real do PC (mouse e teclado) e pausa sozinho quando ninguém está usando.
- Separa o tempo por programa e site, com regras simples no `categorias.txt`, e tolerância maior para vídeo e reunião.
- Grupos de categorias: uma linha `[Nome do grupo]` no `categorias.txt` junta os programas em grupos (Trabalho, Redes sociais, Vídeo e música…). As regras padrão já vêm separadas em grupos.
- Guarda o tempo por dia, por programa e por hora do dia em planilhas CSV, além de um relatório `.txt`.
- O título das janelas nunca é salvo; nada sai da rede local.

### Programa no Windows
- Roda escondido, sem ícone na bandeja: janela mínima pelo atalho Ctrl+Shift+Alt+X e depois 5, e informações completas por Ctrl+Shift+Alt+F2 e depois 1.
- Se instala sozinho em `C:\Program Files\Time` na primeira execução, com opção de iniciar com o Windows.
- Interface em português ou inglês, conforme o idioma do Windows.

### Painel no celular
- Painel na rede local, protegido por código de acesso, em português ou inglês conforme o idioma do celular, com tema claro e escuro.
- Tempo de hoje, por grupo, por programa e por hora; gráfico dos últimos 14 dias; semana e mês com comparação com o período anterior.
- Detalhe de cada dia anterior e de cada programa (histórico, semana e mês).
- Relatório: baixa o histórico de dias, programas e horas em planilhas CSV.
- Vários PCs somados num link só, com descoberta automática na rede e confirmação do código por HMAC.
