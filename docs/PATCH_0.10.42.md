# 0.10.42 — reconexão e recuperação de perdas

- Reconexão por cliente quando o jogo volta à tela de login. Avisos no formato enviado são identificados pelo botão OK e pela tela de login, independentemente do texto (inatividade, conexão ou timeout). Não confirma avisos genéricos dentro do jogo.
- Fluxo visual: confirmar aviso, tocar na tela, fechar os pop-ups, reconhecer o seletor sem depender do nome do servidor, manter o personagem selecionado e clicar em Iniciar. Tentativas espaçadas; telas de carregamento não recebem cliques cegos.
- Após entrar, conferir perdas de EXP/equipamento usando o fluxo de restauração e verificar a habilidade do slot 6. Enviar 6 uma única vez quando reconhecida como desligada; não alternar uma habilidade já ativa.
- Agenda de farm preserva o progresso e não conta desconexão como farm. As rotinas normais do cliente desconectado não disputam comandos com a reconexão; o outro cliente continua monitorado.
- Corrigida a leitura do contador verde de equipamentos sobre o cenário. A captura real que mostrava 1 equipamento danificado agora é reconhecida. Um painel já aberto não é tratado como painel fechado por falha de leitura do contador.
- Logs técnicos registram etapa de reconexão, tentativas, restauração e reconhecimento da habilidade. Atividade do cliente exibe Reconectando.

Validação: imagens fornecidas de login, dois pop-ups, personagem e habilidade; aviso com texto removido; OK dentro do jogo rejeitado como desconexão; captura real da falha de equipamento; isolamento dos comandos entre clientes; autotestes existentes.

Escopo: reconectar com a janela do jogo aberta e a sessão de conta ainda válida. Não relança jogo encerrado, não preenche credenciais/CAPTCHA e não escolhe outro personagem. Se a habilidade não puder ser reconhecida, registra aviso e retoma sem alterná-la às cegas. Validação real após desconexão nos PCs continua necessária.
