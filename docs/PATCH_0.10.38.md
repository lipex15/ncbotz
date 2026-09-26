# PEXBOT 0.10.38

- Preparar uma rotina não ativa mais a janela por antecipação. Capturas e reconhecimento continuam vinculados à janela de cada cliente, em segundo plano.
- Teclas, cliques, movimento do cursor e rolagem obtêm foco apenas antes do comando. O alvo é validado novamente antes da entrada; mudança de cliente, foco ou cancelamento bloqueia o envio.
- A checagem periódica das diárias não sai do descanso nem abre painéis. Lista encoberta ou captura indisponível preserva a campanha e agenda nova observação passiva, em vez de disparar retorno ao farm.
- Duas observações de caça comum ou descanso aguardando, sem missão automática, autorizam o fluxo de retomada das diárias. Abrir painéis, entregar missões e teleportar continuam sendo ações que podem precisar de foco.
- Emergências mantêm seu caminho de ativação e proteção. A primeira leitura de perdas na inicialização passa a ser passiva; restauração efetiva continua podendo usar foco.
- Log técnico registra `focus_request reason=workflow_input` e leituras passivas inconclusivas sem cancelar a campanha.
- Testes offline verificam foco sob demanda, cancelamento, bloqueio de interação e troca de cliente, além das regressões existentes. Não é garantia contra falhas do jogo/captura: mantenha as janelas disponíveis para captura; minimizar pode suspender os quadros do jogo.
