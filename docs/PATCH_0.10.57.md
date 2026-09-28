# 0.10.57

- Descanso é aplicado na transição para o farm, sem novas tentativas periódicas de trazer o jogo para frente.
- Rotinas diárias mantêm a primeira execução agendada; repetições no mesmo ciclo não roubam foco durante farm em segundo plano. A pendência pode continuar quando o jogo já está em primeiro plano ou fora do farm.
- Interação manual interrompendo uma rotina não é tratada como falha da rota e não dispara recuperação do farm.
- Proteções, detecção de morte e reconexão permanecem ativas.
