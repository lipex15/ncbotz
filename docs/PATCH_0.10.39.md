# PEXBOT 0.10.39

Correções pontuais para os casos observados na log de 26/09, preservando navegação, captura, foco sob demanda e o reconhecimento normal do Auto.

- Partida sem morte pendente: HP reconhecido em quadros estáveis pode confirmar HUD válido mesmo quando o desenho do menu não corresponde. Lápide, contador, cabeçalho e sinais suspeitos continuam sendo avaliados. Depois de uma morte pendente, a exigência mais forte de carregamento completo permanece.
- Auto desconhecido ao chegar: alternativa contextual exige que o fluxo esteja aguardando ativação no spot, duas leituras de descanso em Aguardando, sem movimento/caça/ponto fixo, HP visível após fechar descanso e nenhuma restauração pendente. Só então envia Q e usa a confirmação de caça existente. Auto reconhecido ativo nunca recebe Q; Auto reconhecido desligado mantém o caminho anterior.
- Na retomada desse caso específico, o descanso pode ser consultado para obter evidência alternativa. Se ele já confirmar caça, não envia Q. Ponto fixo invalida a antiga chegada, evitando ativação na cidade/respawn.
- Falha exclusivamente na leitura do Auto no spot não escala para reset da interface nem reentrada na T.A. Outras falhas mantêm a recuperação existente.
- Diagnóstico técnico registra correspondências individuais do Auto, dimensões do quadro e as evidências de espera utilizadas pela alternativa.
- Testes de regressão cobrem Auto ativo/desligado/desconhecido, ausência de chegada, morte pendente, HP ausente, recuperação local e inicialização com menu visualmente diferente.

## Limites da análise

A log confirma chegada e espera do Cliente 2 antes da recusa de Q, além de HP reconhecido no Cliente 1 com falha do menu. Não mede pressão de RAM nem inclui as imagens salvas no computador remoto; portanto não permite atribuir os problemas à memória ou explicar visualmente todas as capturas inconclusivas. Não foram reduzidos limiares globais nem reescritos os fluxos que já funcionavam.
