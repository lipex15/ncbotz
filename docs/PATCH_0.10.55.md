# PEXBOT 0.10.55 — Agenda T.A 1, buff Boost e Masmorra Global

- T.A 1 (Codex) disponível na agenda, usando a coordenada capturada na configuração da T.A 1, sem Favoritos. O início valida a existência do ponto; o saldo só desconta farm confirmado.
- Buff gratuito do Patrocinador em Rotinas: seleção independente de clientes e aviso amarelo de uso exclusivo em servidor Boost. Usa teleporte 7, identifica a cidade e o NPC, permite até três acionamentos e salva no banco local o horário do ícone confirmado. Renova após 24 horas, inclusive durante Global.
- Global em Rotinas: quinta e domingo, entrada a partir de 21:01 e encerramento até 23:00, UTC−3. Seleção de clientes, duração de 1 a 120 minutos (padrão 120) e coordenada personalizada independente por cliente.
- Entrada exige tela Global, botão iluminado e popup específico antes de Y; chegada reconhece ambos os Alicerces e o mapa Candellium. A duração é persistida para não reiniciar ao reconectar ou reiniciar o bot.
- Global tem prioridade sobre os farms e adia rotinas diárias; buff vencido e proteções permanecem ativos. Agenda não consome saldo durante Global/visita ao buff. Farm estabilizado mantém observações em segundo plano.
- Novos cards compactos, desativados por padrão. Use o mesmo zoom e abas do mapa ao capturar e executar o ponto Global.

Validação: compilação e regressões automatizadas, incluindo capturas fornecidas, botão desabilitado, duas chegadas, buff deslocado e limites de horário UTC−3. Não foi executado teste ao vivo de entrada/renovação na conta do usuário.
