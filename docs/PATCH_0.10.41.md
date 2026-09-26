# 0.10.41 — fluxo imediato após fuga e restauração

- Removida a espera de HP antes de viajar, entrar em masmorra ou retomar o farm. HP baixo ou ilegível não bloqueia mais o retorno.
- Após TP ou encerramento da restauração, o alerta residual do mesmo episódio é consumido sem provocar outro TP. A regra é por cliente e cobre áudio e contingência visual, inclusive durante Sapheras.
- Rearme com duas leituras de HP >=60%, ou nova queda de pelo menos 12 pontos percentuais em duas amostras. Quadros transitórios dos primeiros 5 segundos não rearmam a proteção; isso não pausa nenhuma ação do fluxo.
- A detecção de morte continua independente. Não há desativação geral do bot nem espera para o HP encher.
- Se o HP estiver ilegível, o fluxo continua; distinguir novo dano durante esse intervalo depende de voltar a ter leitura visual válida. O som residual sozinho não é tratado como novo ataque.
- Mantidas as correções de restauração da v0.10.40.

Testes de regressão: HP baixo constante por 120 segundos sem nova emergência, leitura ausente, recuperação, novo dano e isolamento entre clientes. Compilação e autotestes; validação real nos PCs continua necessária.
