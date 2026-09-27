# PEXBOT 0.10.54 — Farm sem foco periódico

- A preferência de descanso deixa de ser reaplicada periodicamente durante o farm estabilizado. Ela é aplicada no início, na reconexão e após ações do fluxo que abrem/fecham descanso.
- HUD aberto reconhecido impede tentativa de fechar descanso baseada em uma correspondência visual isolada. Nenhum L, arraste ou pedido de foco é enviado nesse caso.
- Monitoramento de HP, áudio, morte, reconexão e horários permanece ativo em segundo plano. Ações reais de proteção e rotinas continuam podendo solicitar foco para executar seus comandos.
- Testes cobrem HUD aberto contradizendo falso descanso e preferência encerrada durante ciclos ociosos, reativada somente por transições do fluxo.

Diagnóstico: logs locais mostravam pedidos de foco de `rest_preference` no monitoramento, sem rotina de farm nova, inclusive às 19:35–19:39 de 27/09. Compilação e regressões automatizadas verificadas; não foram enviados comandos ao jogo do usuário durante os testes.
