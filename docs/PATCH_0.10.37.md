# PEXBOT 0.10.37

## Restauração sem repetição desnecessária

- Removida a espera fixa de 1,8–2,6 segundos antes de observar as perdas. A espera passa a depender do carregamento real da tela.
- Reaproveita a lápide já confirmada em dois quadros do mesmo cliente, sem iniciar outra rodada de confirmação. Mantém apenas a leitura atual da posição antes do clique.
- HUD incompleto após renascer não confirma ausência de perdas. Evidência clara continua liberando a decisão no segundo quadro; somente casos inconclusivos recebem até seis amostras locais, separadas por 250 ms, além do tempo de captura/leitura.
- A melhor variante visual da lápide também pode participar da inspeção contextual após morte, sempre exigindo sinal vermelho e posição compatível. Não foram reduzidos indiscriminadamente os limites de reconhecimento.
- Contadores de EXP e equipamento continuam obrigatórios para concluir a restauração. Perda inconclusiva permanece pendente, sem retorno cego ao farm.
- Recuperação após TP de proteção resolve uma restauração pendente antes de iniciar outra viagem ao farm.

## Ajustes dos fluxos observados

- Boss do Amor: revela o HUD quando o descanso encobre a luta. A ausência momentânea do título não apaga uma entrada já confirmada nem dispara ESC/retorno ao farm na recuperação de erro.
- Artigos dentro da T.A: até três tentativas locais para abrir o NPC, verificando se a loja já abriu e se o contexto de chegada continua presente. Não repete imediatamente toda a viagem nem envia compra sem loja confirmada.

## Diagnóstico e verificação

- Log técnico identifica reaproveitamento de evidência da lápide e tentativas locais de interação com Artigos.
- Regressões offline cobrem HUD incompleto, perda clara, ausência, reconhecimento visual e os fluxos já existentes.
- Validação offline não substitui teste no jogo e não garante ausência de lag ou falhas externas.
