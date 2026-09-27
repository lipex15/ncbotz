# PEXBOT 0.10.48

- Restauração: HP sozinho não confirma ausência de perdas. A confirmação exige também menu ou controle Auto visível, preservando a pendência em telas ocultas/incompletas.
- Descanso com morte ou instrução de arraste: desbloqueio direto por arraste. Tela de Ressuscitar também confirma saída do descanso, sem aguardar o HUD de um personagem vivo.
- Falha ao desbloquear não é ignorada e não produz conclusão falsa de restauração.
- Clique de Ressuscitar ajustado à posição/tamanho da janela do cliente.
- Notificação antiga de morte não apaga progresso já concluído de EXP/equipamentos sem nova morte visível.
- Diagnóstico técnico inclui evidências de HUD/HP; regressão usa captura real de morte no descanso e controles Auto com distância variável.

Tentativas locais de desbloqueio permanecem limitadas a três. Não foi adicionada espera por HP cheio nem clique especulativo em equipamento. Se o jogo não fornecer evidência válida, a pendência continua registrada: não é seguro declará-la restaurada e enviar o personagem ao farm.
