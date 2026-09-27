# 0.10.46 — proteção e restauração

Diagnóstico da sessão de 27/09: o Cliente 2 permaneceu morto no descanso após L, e a restauração posteriormente considerou o HP do descanso evidência de ausência da lápide. O TP de segundo plano também aguardou 152 segundos pela rota do outro cliente antes da tentativa em primeiro plano. Captura de Artigos mostra inventário 200/200; nenhuma exclusão automática de itens foi adicionada.

- TP pendente de personagem vivo interrompe a ação do outro cliente em pontos seguros; somente o fluxo principal controla o foco.
- Atendimento de dois TPs simultâneos não alterna indefinidamente entre janelas.
- Descanso recebe referência adicional de sua instrução fixa de desbloqueio e arraste verificado como alternativa quando L falha.
- Restauração reavalia descanso após carregamento/renascimento; tela coberta não comprova ausência de perdas.
- Leitura de restauração remove oferta conhecida antes de examinar indicadores.
- Renascimento confirmado filtra pulsação residual sem aguardar cura.
- Testes de prioridade e reconhecimento incluem captura real da falha e negativos de jogo/login.
- Limpeza de diagnóstico não roda em comandos de teste e não bloqueia a abertura da interface.

Não é possível garantir fuga sem consumível disponível. Inventário cheio deve ser liberado pelo usuário; o bot não descarta nem vende itens arbitrariamente.
