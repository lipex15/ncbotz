# PEXBOT Teste 0.10.23 — adaptação de resolução

Exclusiva do canal de testes, a partir da v0.10.22. O canal normal permanece na v0.10.21.

## Detecção automática

- 1920×1080, 1366×768 e 1440×900, sempre com escala do Windows em 100% e os mesmos ajustes de interface do jogo.
- Detecta ao abrir o app, sem configuração manual; a indicação aparece abaixo do menu. A geometria de cada janela é medida novamente na captura e no clique, inclusive após redimensionamento.
- O jogo continua sendo maximizado pelo fluxo existente. A janela do próprio bot também se ajusta à área útil menor.
- Não muda a resolução do Windows, não instala nada e não altera as configurações do jogo.

## Captura, reconhecimento e cliques

- Um contrato de coordenadas para o bot inteiro: a área útil do jogo é medida por janela, separada das bordas, barra de título e barra de tarefas. Captura WGC e entrada usam essa mesma geometria.
- Todos os cliques das rotinas passam pela conversão central. Os caminhos que já convertiam coordenadas foram ajustados para evitar conversão dupla.
- Capturas dos dois clientes são normalizadas antes de chegar aos leitores de pixels, imagens e OCR, incluindo diárias, diretivas, morte/lápide, descanso, T.A, Sapheras, Abadia/Anônima, Agenda, loja, artigos, correio, Boss e baú.
- Imagens existentes são reaproveitadas. Quando necessário, compara também na resolução nativa com proporção uniforme e posições relativas às bordas/centro, importante em 1440×900 (16:10). Não reduz limiares para forçar reconhecimento.
- Evidências visuais locais e recentes podem corrigir o ponto dentro do elemento encontrado; não aplicam um deslocamento global baseado em um ícone, nem são compartilhadas entre clientes. Pontos personalizados mantêm sua conversão própria sem essa correção local.
- OCR de ouro lê o recorte nativo nas resoluções menores para evitar desfocar os números com dois redimensionamentos. Valores ambíguos continuam fora do total confirmado.
- Se a janela mudar de posição/tamanho durante o movimento do mouse, o clique é cancelado. Geometria incompatível gera diagnóstico, não clique por adivinhação.
- Logs técnicos incluem área útil, coordenada de referência e coordenada final. Sem mexer nas prioridades, limites, persistência ou temporizadores das rotinas.

## Validação e limites

A regressão automatizada cobre as 124 referências cadastradas nas duas resoluções menores, cenas completas nas três resoluções, ausência de lápide/morte, diárias em andamento, preços, ancoragem de elementos e conversão de pontos. Também executa os testes visuais e de áudio anteriores.

As imagens reduzidas e os elementos reposicionados são simulações: comprovam as conversões e leitores, não reproduzem necessariamente toda mudança de layout que o próprio Night Crows possa realizar. Não é uma afirmação de validação ao vivo em todos os PCs. A experiência no jogo no PC de destino é a próxima etapa; não é necessário enviar novas imagens para instalar e experimentar.

## Teste no PC do amigo

1. Atualize o **PEXBOT Teste**, confira **v0.10.23** e a resolução exibida no rodapé. O normal não precisa ser atualizado.
2. Windows em 100%; jogo com os mesmos padrões de interface usados em 1920×1080. O bot maximiza a janela normalmente.
3. Comece acompanhando um cliente; confira abertura de menus, diárias já em andamento e entrada/retorno ao destino configurado. Depois habilite ambos.
4. Confira o ponto personalizado no mapa e a contagem da Agenda, sem provocar mortes ou compras desnecessárias só para testar.
5. Se reconhecer algo no lugar errado ou clicar fora, pare o teste e envie o diagnóstico técnico completo e uma captura inteira da situação. Não deixe a experiência sem supervisão antes dessa primeira validação.
6. Se necessário, volte ao pacote do testes v0.10.22. Não há migração destrutiva de dados nesta versão.

Detalhes de geometria seguem as distinções documentadas pela Microsoft entre [área cliente e coordenadas de tela](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-clienttoscreen) e [limites visíveis da janela](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute).
