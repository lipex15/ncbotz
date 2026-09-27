# 0.10.43 — reconexão e restauração

- Reconexão automática por cliente: confirmar aviso na tela de login independentemente do texto, fechar pop-ups, avançar com o servidor atual e iniciar o personagem selecionado.
- Após o login, conferir perdas de EXP/equipamento e ativar a habilidade 6 uma única vez quando reconhecida como desligada. Retomar o fluxo configurado preservando o progresso da Agenda, sem contar o tempo desconectado como farm.
- Painel de restauração aberto não é mais confundido com painel fechado por falha na leitura do contador. Contraste específico para texto e número verde, com alternativa visual para o contador de 1 equipamento da captura real enviada.
- A alternativa visual só confirma perda pendente: nunca inventa contador zero nem declara restauração concluída. Mantidas as verificações de conclusão e a proteção contra repetir toda a restauração após falha ao fechar o painel.
- A reconexão não envia comandos do fluxo anterior enquanto o cliente está na tela de login. Logs técnicos registram as etapas e o estado de cada cliente.

A versão 0.10.42 não foi distribuída: a validação do instalador identificou diferença de OCR entre versões do Windows. Esta versão acrescenta reconhecimento visual independente desse OCR para a evidência encontrada.

Testes com capturas fornecidas e autotestes de regressão. Ainda é necessário acompanhar uma desconexão real nos PCs. O recurso pressupõe jogo aberto e sessão válida; não relança o executável, não preenche credenciais/CAPTCHA e não troca personagem. Skill sem reconhecimento gera aviso, sem alternância cega.
