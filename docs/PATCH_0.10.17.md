# PEXBOT 0.10.17 — checagem inicial e retomada

- A inicialização não clica no local da lápide para testar se existe. Duas observações da janela cruzam HUD, ícone vermelho, referência visual e OCR do cabeçalho de restauração.
- A decoração genérica de fechamento não confirma mais um painel de restauração. A referência passa a incluir os seletores de EXP/equipamento e é testada contra telas negativas.
- Captura desconhecida não registra uma perda. Pendências antigas incorretas são removidas somente quando a ausência é confirmada em uma tela de jogo válida.
- O OCR amplia o cabeçalho e usa tratamentos de contraste; leituras discordantes permanecem desconhecidas. O diagnóstico registra os sinais que produziram a decisão.
- A falha inicial não é contada novamente pelo tratamento externo. A recuperação aguarda 5, 10 ou no máximo 15 segundos, não cinco minutos.
- O redimensionamento reaproveita a captura recriada, evitando reiniciar repetidamente com dimensões antigas.
- A busca de Diárias inclui a parte inferior da lista e exige um ícone de missão próximo da cor roxa. Na inicialização, duas leituras de missão pendente corrigem um estado salvo de conclusão e retomam sem aceitar novamente.
- Lista abreviada no descanso não basta para concluir as Diárias; a lista normal é conferida. Diárias já aceitas podem ser retomadas antes do horário de nova aceitação.
- Cliques nas abas da restauração são mapeados para a janela do cliente responsável.

## Validação

Testes automatizados com referências visuais, OCR, políticas de estado e áudio. Não houve instalação nem teste de rotinas nos personagens. As capturas de diagnóstico dos PCs afetados não estão disponíveis localmente; os logs motivaram a correção, mas a validação real nesses PCs continua necessária.
