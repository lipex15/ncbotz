# PEXBOT 0.10.35

- Corrige a restauração ignorada após morte durante farm da Abadia na Agenda. O sinal vermelho de perda com imagem parcialmente reconhecida não é mais classificado como ausência quando existe restauração pendente.
- Nesse contexto, a forma da lápide com sinal vermelho na região conhecida pode abrir o painel após duas leituras estáveis. A confirmação do painel e dos contadores continua obrigatória; abrir o painel não conclui a restauração.
- Sem evidência suficiente, a perda permanece pendente e bloqueia a volta ao farm. O reconhecimento normal, fora desse contexto, mantém o limiar anterior.
- Teste de regressão reproduz os valores da log (0,617 em 1535,63), com negativos para falta de contexto, ausência do vermelho, posição incorreta e forma insuficiente.
- Logs registram quando a inspeção contextual foi utilizada.
