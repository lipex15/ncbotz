# PEXBOT 0.10.28

- Correção pontual: o painel de serviços/Artigos não confirma mais que o personagem já está na T.A. Esses elementos também aparecem nas cidades comuns.
- Na preparação inicial, caça automática em descanso não é mais suficiente para assumir farm na T.A. A rota consulta o seletor confirmado antes de reutilizar uma entrada existente ou entrar no destino configurado.
- A mensagem completa de ponto fixo é distinta de Aguardando no spot. Ao retomar uma T.A confirmada pelo seletor, ponto fixo encaminha à rota do spot, sem ativar Q ali.
- Sem alterações na contagem da Agenda, estatísticas ou proteções.

Teste no jogo: iniciar o Cliente 1 na cidade, inclusive com caça automática/descanso ligados. Deve conferir o seletor e seguir à T.A configurada, sem declarar farm existente apenas pelos serviços da cidade.
