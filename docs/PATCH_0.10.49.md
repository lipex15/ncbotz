# PEXBOT 0.10.49

Inclui as correções de restauração da versão 0.10.48.

- Reconexão reconhece retorno ao jogo já morto/no descanso, mesmo sem HP visível, e encaminha à restauração.
- Morte durante a preparação pós-login não inicia o farm antes de concluir a reconexão.
- Tela de login sem avanço volta a ser reavaliada em 3 segundos, removendo a pausa fixa de 30 segundos. As ações continuam condicionadas à tela reconhecida.
- Teste de regressão usa a captura real de morte no descanso para validar a passagem reconexão → restauração.
- Leitura de perdas incompleta reinicia a captura da janela antes da próxima tentativa, em vez de apenas aguardar sobre a mesma captura inválida.

A pendência não é descartada quando uma tentativa falha. O monitor de desconexão continua ativo e o fluxo de reconexão pode interromper a recuperação para realizar novo login. Não há espera por HP cheio.
