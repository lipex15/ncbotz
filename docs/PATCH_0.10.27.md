# PEXBOT 0.10.27

- Agenda preserva o último farm confirmado quando inventário ou outro painel encobre o Auto, inclusive por minutos. Não exige que o indicador permaneça visível a cada segundo.
- Auto reconhecido como desligado encerra essa confirmação. Saída/alteração da etapa, morte, emergência, outra atividade, pausa explícita ou falha/intervalo de captura invalidam a continuidade; é necessária nova confirmação positiva para voltar a contar.
- Um cliente que nunca confirmou farm não ganha tempo apenas porque o Auto está invisível. Estados permanecem separados por cliente e não persistem entre execuções.
- Durante uso manual, contabilização e estatísticas da Agenda continuam sendo atualizadas; somente ações de navegação aguardam a interface disponível.
- Testes de regressão: cinco minutos de Auto encoberto, Auto desligado, estado inicialmente desconhecido, saída, pausa, captura interrompida e troca de etapa.

Validar no jogo: com a Agenda farmando, abrir um painel que esconda o Auto durante alguns minutos. O tempo deve continuar diminuindo. Desligar o Auto visivelmente ou sair da masmorra deve interromper a contagem. Não altera o uso padrão do descanso nem desliga as proteções.
