# PEXBOT 0.10.25

- Reconhecimento do botão Auto ligado/desligado fora do descanso usando as capturas fornecidas. Estado encoberto é desconhecido, não caça desligada. Auto ligado evita alternar Q/L desnecessariamente nos caminhos de ativação e retomada de farm.
- Agenda aceita a evidência de Auto ligado fora do descanso, mantendo as condições de localização/estado da masmorra. A saída do descanso não zera o estado de farm.
- Interação física com as janelas do jogo adia comandos normais e troca de foco, sem botão de retomada. Monitoramento e proteção continuam. Eventos injetados pelo bot não contam como interação física. Há uma margem de 2,5 segundos após a última interação para não disputar os comandos; se a interface estiver encoberta, aguarda reaparecer o HUD/descanso. Não coleta texto digitado.
- TP de emergência não é bloqueado pelo controle de interação. Ressurreição continua prioritária.
- Conclusão de Diárias não é desfeita apenas por linhas roxas durante farm ou reinício. Evita o ciclo observado na log de reabrir missões, Q/L e seletor da T.A.
- Estatísticas: um nome opcional para saudação; clientes continuam separados. Zerar hoje ou todo o histórico do cliente selecionado, com confirmação e backup. Não altera configurações, agenda ou conclusão de rotinas; mantém deduplicação de eventos antigos.
- Compra em lote: procura o total pelo rótulo Preço, tolera deslocamento da janela e tenta novamente por um intervalo limitado antes de confirmar. Gasto desconhecido aparece como total não apurado, não zero.
- Coleta do Baú do Tesouro continua presente: por cliente, intervalo de 17 horas e aproveitamento das visitas à Guilda. Não depende do nome do item.

## Verificação no jogo

1. Manter Auto ligado sem descanso e verificar que não força Q/L nem uma nova entrada na T.A.
2. Abrir inventário/loja manualmente durante farm e conferir que não disputa cliques. Fechar o painel e verificar retomada natural, sem botão.
3. Confirmar a proteção durante interação e a contagem da Agenda com/sem descanso.
4. Conferir o preço do lote nos dois clientes. Valores históricos desconhecidos não são reconstruídos.
5. Testar Zerar estatísticas somente se desejar: cria backup local e não altera o funcionamento das rotinas.

Os testes automatizados verificam imagens, preços, separação e reset. Interação real com o jogo ainda precisa ser validada pelo usuário. Compatibilidade: 1920×1080, escala Windows 100%.
