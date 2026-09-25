# PEXBOT 0.10.36

## Correções

- Boss do Amor: procura o desenho da Raide na faixa ao lado do minimapa, inclusive quando deslocado pelo botão de saída. Não usa mais o clique fixo em 413,127 nem sai previamente de uma masmorra paga só para tentar abrir o convite. Confirma o convite e a sala antes de prosseguir.
- Auto: reconhece a palavra e o brilho de ativação sem depender da distância de ataque (20m, 30m, 40m ou infinito). Corrige a espera inconclusiva após chegar ao spot. Q continua sendo enviado apenas quando o Auto está desligado, evitando desligar uma caça já ativa.
- Restauração: nova referência recortada da lápide vermelha observada no diagnóstico real, com confirmação de forma, cor e estabilidade. Funciona também ao iniciar sem registro anterior de morte. Mantém a confirmação dos contadores antes de liberar retorno ao farm.
- Diárias: uma data antiga de aceitação, sozinha, não autoriza assumir que uma missão automática é diária. Exige indicação roxa ou campanha diária efetivamente em andamento. Teste negativo com a missão branca recebida, sem depender do nome.

## Agenda

- Ao iniciar já farmando na masmorra configurada, confirma a atividade e a localização separadamente; se necessário consulta o mapa sem selecionar outro ponto. Adota o farm e passa a descontar o saldo.
- Saldo salvo em checkpoint único por cliente e perfil, a cada dez segundos de progresso e ao encerrar. A migração da antiga espera por limite de entradas não devolve mais o tempo já consumido.
- Exibe o saldo salvo ao abrir novamente o aplicativo. Os minutos configurados continuam representando o plano; o saldo é separado para não reiniciar o plano ao salvar.

## Sapheras

- Preparação antecipada em 90 segundos por cliente selecionado para absorver carregamento e deslocamento. O término não é antecipado por essa preparação. Falha na primeira entrada é reavaliada após dois segundos.
- Movimento W acompanhado de duas observações de saída do ponto fixo, com até três tentativas locais, sem assumir sucesso apenas por ter enviado a tecla.
- Ponto personalizado por cliente: abra o mapa com abas laterais abertas e o zoom desejado, capture o ponto na seção Sapheras e mantenha esse enquadramento. O bot reconhece o mapa, seleciona o ponto, confirma Ir, acompanha a chegada e ativa a caça. Desmarcado conserva o TP aleatório.
- Logs registram a posição do ícone da Raide, as observações de saída do respawn e quantos clientes realmente confirmaram o farm.

## Verificação

- Testes offline com capturas reais: Auto 30m ligado/desligado, lápide vermelha, mapa de Sapheras e saída da T.A como negativo para Raide.
- Regressões automatizadas de deslocamento do ícone da Raide, missão branca, conservação do saldo e referências anteriores.
- Capturas incorporadas são recortes de elementos necessários, sem o restante da tela pessoal.
- Não equivale a teste ao vivo de todos os trajetos nem garante ausência de lag do jogo. O ponto personalizado exige o mesmo enquadramento da captura.
