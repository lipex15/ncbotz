# 0.10.40 — recuperação sem reiniciar perdas já resolvidas

- Pendência de restauração não é criada apenas por iniciar uma inspeção: exige morte ou perdas observadas.
- Ausência de lápide/painel, HP visível e duas observações estáveis liberam pendência antiga mesmo sem correspondência do ícone decorativo do menu. Sinal vermelho ou forma suspeita continuam impedindo conclusão por ausência.
- Após EXP e equipamento zerados, a tentativa seguinte trata somente o fechamento do painel. Uma nova morte invalida esse progresso. O progresso parcial é mantido durante a execução; na reinicialização, a interface é reavaliada.
- Fechamento observado no mesmo quadro (HP visível, sem painel nem título/contador de perdas), sem exigir reconhecimento do menu.
- TP de emergência exige HP >=70% em duas amostras separadas antes da volta ao farm. Espera passiva por cliente, sem bloquear a fila nem repetir compras/viagens. Proteção continua monitorando; HP ilegível não é tratado como recuperado.
- Esperas de observação preservam a rota e não acionam reset de interface. Inspeções repetidas de perdas não puxam o jogo para frente; imagens inconclusivas limitadas a uma por minuto por cliente.
- Sapheras: saída antecipada para Castelo de Abilius confirmada por três leituras de cidade + ponto fixo + HP retoma o destino normal. Reconhecimento conservador para a cidade documentada: outras localidades ainda dependem do término programado; Auto oculto não prova saída.
- Logs técnicos incluem progresso da restauração, recuperação de HP e evidência de saída de Sapheras.

Verificação: compilação Release e autotestes locais, incluindo regressão de pendência antiga com menu não reconhecido e políticas de HP/saída. Não equivale a teste ao vivo nos PCs dos usuários.
