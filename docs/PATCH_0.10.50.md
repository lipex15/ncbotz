# PEXBOT 0.10.50

Correção localizada da seleção de personagem na reconexão.

- O título da seleção confirma essa etapa independentemente da correspondência visual do botão Iniciar e da aparência/quantidade de personagens.
- Leitura textual do título oferece alternativa ao reconhecimento por imagem.
- HP isolado não classifica mais a seleção como jogo carregado. O HUD aberto exige um controle independente do jogo.
- O mesmo critério foi aplicado ao monitor de desconexão, evitando que uma leitura falsa de HP impeça a detecção.
- Logs incluem as evidências individuais de seleção, botão, HP, menu, Auto e título lido.
- Testes cobrem a seleção com botão não reconhecido e HP falso, além de login, pop-ups, morte no descanso e mundo carregado.

Não altera as rotinas de farm, compras ou restauração. A correção impede que a restauração seja iniciada antes de sair da seleção de personagem.
