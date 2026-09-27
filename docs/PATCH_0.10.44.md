# 0.10.44 — habilidade 6 na inicialização

- Cada execução do bot verifica a habilidade do slot 6 em cada cliente, também sem reconexão.
- Se reconhecida desligada, envia 6 uma única vez. Se já estiver ativa, não envia tecla.
- Verificação após a conferência inicial de perdas, antes das rotinas normais. Caso esteja em descanso, abre temporariamente a interface e devolve ao descanso.
- Tela encoberta ou habilidade desconhecida não bloqueia o farm nem provoca alternância cega: nova verificação em 30 segundos, respeitando o uso manual e as prioridades de proteção.
- Mantida a ativação após reconexão e o isolamento entre os clientes.
