# PEXBOT 0.10.29

- Corrige a regressão da 0.10.28 no seletor com T.A 1 já ocupada: o botão apagado pode ser reconhecido sem corresponder à imagem do botão iluminado. Exige cartão correto, OCR de Entrar e contraste relativo com os outros cartões, em leituras estáveis.
- Essa alternativa só autoriza retomar sem nova entrada; não autoriza clique de compra. Serviços/Artigos e caça automática continuam sem servir isoladamente como prova de localização.
- Interação manual durante a checagem inicial não incrementa falhas de lápide nem gera mensagem de perda: mantém a checagem pendente com retomada automática.

Validar: iniciar já farmando na T.A 1 nos dois clientes; deve reconhecer o seletor ocupado, fechá-lo e preservar a caça. Iniciar na cidade deve continuar seguindo a entrada normal.
