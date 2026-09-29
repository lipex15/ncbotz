# PEXBOT 11.6.1

- Reconhecimento da skill 6 ampliado para o ícone compacto e posições diferentes na barra. O desenho identifica a habilidade; a borda externa distingue ativa de desativada, com testes usando as duas imagens enviadas.
- Modo descanso ativado: no farm confirmado, retorna ao descanso após três minutos sem interação física no PC e com a tela de farm aberta. Não interrompe menus, deslocamentos, diárias, recuperação ou proteções.
- Modo descanso desativado: não reimpõe a preferência durante o farm; respeita o descanso aberto manualmente. O descanso temporário usado pelo próprio fluxo continua sendo tratado ao concluir as ações.
- A verificação usa o estado já observado em segundo plano, sem ativar a janela para consultar. Tentativas de retorno ao descanso são espaçadas, evitando solicitações contínuas de foco.

Mantém as correções da versão 11.6 e preserva configurações e agenda.
