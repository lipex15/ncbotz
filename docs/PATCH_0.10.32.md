# PEXBOT 0.10.32

- Coleta da Raide usa a coordenada conhecida após confirmar painel e contador completo, sem depender da correspondência da imagem do botão com texto animado.
- Coleta só é registrada após confirmar o aviso de recompensa obtida.
- Painel, contador e estado da recompensa são confirmados em duas leituras; leitura incerta aguarda até 15 segundos.
- Recompensas já recebidas continuam sendo ignoradas e coleta inconclusiva permanece pendente.
