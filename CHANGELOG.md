# Mudanças

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
