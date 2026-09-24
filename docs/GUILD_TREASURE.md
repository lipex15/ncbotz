# Baú do Tesouro da Guilda — v0.10.21

- Cada cliente ativo possui seu próprio intervalo de 17 horas, salvo em UTC por usuário do PEXBOT e posição do cliente, sem depender do nick.
- Sem histórico, a primeira verificação ocorre na primeira oportunidade segura. Bot parado/offline não executa verificações; ao retornar, uma verificação vencida é atendida sem repetir ciclos perdidos.
- O monitoramento normal agenda a abertura; não interrompe morte, restauração, Boss, Diárias em execução ou uma recuperação pendente. Janelas prioritárias podem adiar a verificação até o fluxo normal retornar.
- Quando a página de informações da Guilda já está visível durante check-in, acesso ao painel de Diretivas ou fechamento de uma visita, coleta os baús disponíveis sem exigir que as 17 horas tenham passado. Não abre a Guilda só para uma verificação oportunista.
- Reconhecimento usa o cabeçalho Baú do Tesouro, quantidade, botão Abrir e notificação. Não lê nome do item, nome de jogador nem desenho do conteúdo.
- Abrir usa (1627, 942); Item Obtido é dispensado em (957, 440). Coordenadas são mapeadas para a janela do cliente responsável.
- A cada abertura, exige Item Obtido e redução estável da quantidade. Repete para os demais baús, sem fechar a Guilda entre eles. Zero e o aviso de caixa vazia encerram a coleta; a visita programada fecha a Guilda com ESC e restaura descanso se ele estava ativo. Na visita oportunista, primeiro continua a ação original da Guilda.
- Contador ilegível, quantidade que não diminui ou ausência do aviso não provocam clique cego. Há limite de três minutos por visita para evitar um fluxo infinito. Uma falha é registrada, não marcada como caixa vazia.
- Para respeitar o pedido de não reabrir fora de hora, uma tentativa programada de abertura também ocupa o intervalo de 17 horas. Se falhar, a próxima visita normal à Guilda ainda permite conferir novamente. Reiniciar o bot não elimina essa proteção. Não há garantia contra expiração enquanto o jogo/bot estiver offline ou o fluxo indisponível.

## Validação

Testes com as capturas fornecidas: disponível (1), vazio (0), Item Obtido e tela alheia. Remoção sintética do desenho/nome do item não muda a decisão. Testes de quantidade 0/1/2/7/30, leituras discordantes, sequência 3→2→1→0, bloqueio de repetição sem progresso, fronteira exata de 17 horas, persistência após reabrir banco e isolamento entre clientes.

Não houve clique nos personagens nem instalação local. Incluído no patch 0.10.21 junto do reforço localizado da lápide; a validação em jogo permanece necessária.
