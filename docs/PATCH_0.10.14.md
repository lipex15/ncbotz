# PEXBOT 0.10.14 — correções de retomada e interface

## O que mudou

- Cards e campos arredondados, sem o fundo esbranquiçado dos controles bloqueados durante a execução. Configurações continuam protegidas contra edição acidental enquanto o bot roda.
- Indicador permanente de execução, tempo da sessão e última atividade de cada cliente, sem a telemetria de áudio ocupar esses resumos.
- Resumo antes de iniciar, com “Não mostrar novamente” salvo por usuário. A opção “Mostrar resumo ao iniciar” reativa a confirmação.
- Estados das rotinas separados por login do PEXBOT e por slot de cliente, mantidos no banco local após reinício. O primeiro usuário da migração herda os registros anteriores; outros logins não os herdam. Não há sincronização entre computadores nem identificação automática do personagem se trocar a janela de um slot.
- Missões roxas podem corrigir uma conclusão antiga incorreta. Lista ilegível não equivale mais a lista vazia, e o simples texto de caça normal não conclui uma campanha.
- Diretiva pode abrir o painel para conferir a situação quando o atalho verde está ausente/ilegível. O clique em Aceitar não é mais suficiente para registrar aceitação. A situação é reconciliada novamente ao iniciar.
- Checagem de lápide na partida e após morte; EXP e equipamento são conferidos. Uma restauração interrompida permanece salva e impede voltar ao farm sem concluí-la. Falha de captura não inventa uma morte no registro.
- Confirmação de descanso pela captura da janela de cada cliente. Para declarar fechamento, também é necessário reconhecer a tela de jogo; a ausência isolada da imagem de descanso não basta.
- Leitura do brilho das letras de “Entrar”, sem o preço, com confirmação em dois quadros para T.A 1/2/3. Se o estado continuar incerto, não compra entrada e tenta fechar o seletor antes da recuperação.
- Compra de Artigos verifica resultado ou indisponibilidade estável com a loja visível, e tenta liberar o painel também em falhas.
- Boss mantém entrada pelo ícone ao lado do minimapa. Sapheras só bloqueia a tentativa de boss do cliente que a habilitou; restauração pendente tem precedência.
- Cache limitado às 16 referências recentes, evitando acúmulo de imagens completas na memória. Refinamento de um pixel no reconhecimento para reduzir falsos negativos de alinhamento.
- Atalho do painel administrativo testa o servidor e solicita a inicialização da tarefa local antes de abrir a página.

## Já existentes e preservados

Fluxo Comum/Invocação com confirmação e resultado; check-in da Guilda e doações exclusivamente em ouro; rotinas organizadas por cards e escopo de cliente; entrada do boss dois minutos antes e menu de raide apenas para acompanhamento/recompensa; login rápido por apelido no painel.

## Validação e limites

Compilação, teste de referências visuais/contadores/estados, política de restauração, migração isolada de usuários e teste do alerta de áudio. Layout revisado por capturas renderizadas do aplicativo. Os mesmos testes são executados no instalador de cada canal antes da publicação.

Esses testes não equivalem a uma sessão longa no jogo, não garantem zero falhas ou prevenção de todos os desconectes. Não foi realizado gasto de recursos nem controle das janelas do jogo durante esta validação. Captura ilegível mantém a ação pendente; confira o log e o diagnóstico se a condição persistir.

## Verificação recomendada após atualizar

1. Cliente 1 e Cliente 2 já dentro de suas T.A: confirmar “farm detectado” e nenhuma nova entrada comprada.
2. Iniciar com perdas antigas: verificar EXP e equipamento antes do farm. Repetir após uma morte real quando ocorrer.
3. Parar/reiniciar com diárias pela metade: confirmar retomada das missões roxas, sem aceitar novamente.
4. Diretiva disponível, aceita/em andamento e concluída: conferir resultado e registro correspondente.
5. Outro aplicativo sobre o jogo: nenhum “fechado e confirmado” sem evidência da janela correta.
6. No modal, marcar “Não mostrar novamente”, reiniciar o aplicativo e confirmar início direto; usar “Mostrar resumo ao iniciar” para reativar.
