# PEXBOT 0.10.53 — Descanso configurável e grupo sem checagem repetitiva

- Nova opção independente por cliente: manter descanso durante o farm. Ligada por padrão para preservar o comportamento anterior; desligada mantém o HUD aberto durante o farm.
- Descanso temporário preservado para deslocamento, chegada e informações de diárias. A preferência é aplicada entre os fluxos, sem enviar Q e sem alterar o destino de farm.
- Grupo automático exige descanso no farm desligado. A tela explica a incompatibilidade e o início solicita corrigir a configuração, sem alterá-la silenciosamente.
- Após confirmar a PT, líder e membro deixam de fazer capturas, OCR ou abrir P para verificar o grupo. A verificação só é reativada após reconexão ou nova execução do bot. Limite de cinco convites por pessoa continua preservado na reconexão.
- Página inicial reorganizada: seleção de clientes em duas linhas, descanso em card próprio e seções compactas para configurações de farm e grupo. Grupo pode ser expandido/recolhido; campos de convite aparecem somente para líder e remetente somente para membro.

Validação: compilação, regressões visuais existentes, estados de PT confirmada/reconexão, regras de descanso e renderização da interface. O comportamento novo durante jogo ao vivo requer acompanhamento no uso normal; não foram enviados comandos ao jogo durante os testes.
