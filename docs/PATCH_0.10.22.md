# PEXBOT Teste 0.10.22 — Meu painel

Disponível exclusivamente no ambiente de testes, partindo da mesma base v0.10.21 do normal. Sem promoção automática para main ou release normal e sem instalação no PC durante o desenvolvimento.

## Painel

- Nova página Meu painel: cards arredondados lado a lado, saudação por horário, nicks opcionais por cliente, filtros Hoje / Esta sessão / Últimos 7 dias e Cliente 1 / Cliente 2 / Ambos.
- Nicks são rótulos, não reconhecimento de conta. Trocar a conta dentro da mesma janela não separa o histórico daquele cliente.
- Histórico discreto, recolhido por padrão, com até 100 movimentações; totais calculados sobre todo o período filtrado. Hoje usa meia-noite do PC; regras e resets do jogo permanecem os mesmos.
- Dados salvos em statistics.db no diretório exclusivo PEXBOT-Teste, separados por usuário do bot e cliente. Sem recuperar estatísticas retroativas das versões antigas.

## Ouro

- Leitura do valor atual da tela, não tabela fixa de preços: entradas T.A, Abadia/Anônima, Loja diária, Mercador de Artigos e teleporte de Diárias.
- Reconhece moeda de ouro e região correspondente, com concordância entre variantes de OCR; divergências numéricas não são somadas. Recurso necessário é separado de Seus recursos.
- Preço sozinho não vira gasto: entrada exige chegada reconhecida; compra exige resultado de recompensa. Um preço ilegível aparece como valor não apurado, nunca como zero real.
- Teleporte da Diária: popup fechado registra ação pendente, fora do total; Campanha automática reconhecida em até 60 segundos confirma o gasto. Se faltar essa evidência, permanece pendente sem atrapalhar a rotina.
- Tecla 7 não soma ouro. Proteções contam incidentes com comando de TP enviado, com deduplicação; chegada à cidade é informação separada quando observada. Não confundir alerta de HP com ataque de jogador, nem comando enviado com TP realizado.

## Outras estatísticas

- Baús da Guilda coletados, sem transformar um aviso em quantidade de itens. Drops gerais ainda não são contabilizados nesta primeira versão.
- Boss: conclusões observadas por ciclo e situação atual diária/semanal; progresso sem observação aparece desconhecido, não concluído por suposição.
- Diárias e diretivas concluídas, sem duplicar o mesmo ciclo ao reiniciar.
- Tempo reconhecido pelo contador de farm da Agenda, salvo em lotes de aproximadamente 30 segundos e ao parar normalmente. Uma queda abrupta pode perder a fração ainda não salva. Não é contador de todo farm fora da Agenda.
- Falha ao registrar estatísticas gera diagnóstico técnico, não aciona recuperação nem interrompe o fluxo. Não abre menus ou executa cliques adicionais para alimentar o painel.

## Teste sugerido

1. Atualize somente PEXBOT Teste. Abra Meu painel, salve os nicks e alterne os clientes/períodos.
2. Durante uso normal, confira uma entrada paga, uma compra em lote e um TP de Diária contra o preço mostrado pelo jogo. Sem provocar gastos ou mortes só para testar.
3. Confira que o TP da tecla 7 não altera ouro e que uma compra cancelada não entra no total confirmado.
4. Pare e reinicie: histórico e nicks permanecem; Esta sessão passa a representar a nova execução.
5. Confira baús, rotinas e Boss sem duplicação; o farm da Agenda só cresce junto com o contador de farm existente.
6. Compare os dois clientes. Em caso de divergência, enviar log técnica e print do preço/resultado e do painel.

Testes automatizados incluem imagens reais de preços, recortes posicionados para validar ancoragem, telas negativas, valores conflitantes, deduplicação, limites de período, persistência e isolamento. Validação ao vivo no jogo continua necessária.
