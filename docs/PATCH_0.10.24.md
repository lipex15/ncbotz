# PEXBOT 0.10.24 — Estatísticas e correções de fluxo

## Nos dois canais

- Estatísticas como seção independente: ouro confirmado, proteções, baús da Guilda, Boss, rotinas e tempo de farm na Agenda. Valores não lidos ou ações não confirmadas não viram gastos inventados. O histórico começa quando esta versão observa os eventos, sem preencher execuções antigas.
- Mesma base normal/testes, com armazenamento, nome e atualização separados. Removida a adaptação experimental 1366×768/1440×900; permanece 1920×1080 com escala Windows 100%.
- T.A 1: o botão de entrada do destino não depende mais de reconhecer todos os demais cartões. Continua exigindo localizar o próprio botão e confirmar seu estado. Diagnóstico técnico informa a evidência por cartão.
- Agenda: relógio alimentado pela observação de cada janela, inclusive enquanto o fluxo principal atende o outro cliente. Pausa, morte, proteção pendente e captura interrompida não contam. O saldo pendente é persistido ao encerrar normalmente.
- Diárias: leitura inicial inconclusiva não declara conclusão; preserva o progresso, retorna ao farm e libera nova leitura passiva após 30 segundos. Várias missões pendentes podem corrigir uma conclusão salva incorretamente. Uma linha roxa isolada após conclusão não reabre a rotina, evitando confundir o aviso do Boss.
- Anônima: reconhece também o cabeçalho do mapa e a área Desastre Imprevisto com tempo/HUD. Evita tratar mapa da masmorra como cidade durante a busca por Artigos.
- Recuperação de Artigos: fecha descanso/mapa antes do retorno, trata a confirmação de saída e não deixa a tentativa anterior bloquear o próximo teleporte. Não clica no NPC enquanto mapa, descanso ou Anônima ainda são reconhecidos.
- Morte no descanso: tenta revelar Ressuscitar com L. Se a morte permanece sem tela completa confirmada, bloqueia a restauração de perdas e comandos normais.
- Falha de recuperação após Boss agenda retomada controlada, em vez de continuar silenciosamente com interface bloqueada.

## Teste no jogo

1. Amigo: somente T.A 1, primeiro Cliente 2 sozinho e depois os dois. Verificar entrada, Artigos, mapa, zoom, ponto personalizado e farm.
2. Agenda: comparar um minuto real de farm com o saldo; testar atendimento do outro cliente e saída por proteção. Fora do farm o saldo deve parar.
3. Diárias habilitadas: iniciar com missões parcialmente feitas e verificar retomada sem falsa conclusão. Clientes com diárias desabilitadas não devem abrir essa rotina.
4. Após morte, Ressuscitar deve preceder perdas, Artigos e mapa. Se ocorrer falha, enviar log técnico e o diagnóstico indicado nele.
5. Estatísticas: verificar gastos confirmados e separação dos clientes. Teleporte da tecla 7 não é somado como gasto de ouro.

Compilação e regressões são validações locais, não substituem teste do jogo nos dois PCs. O reconhecimento continua sujeito a alterações visuais e não garante sucesso em toda máquina.
