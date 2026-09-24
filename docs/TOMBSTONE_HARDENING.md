# Reforço localizado da lápide — v0.10.20

Objetivo: decidir rapidamente se existe lápide, sem manter o personagem procurando um ícone ausente. Nenhuma mudança de Agenda, Sapheras, prioridade de clientes ou detector de morte.

- Referência pequena da forma central da lápide, excluindo o fundo animado; forma e cor são avaliadas na mesma posição da mesma captura normalizada.
- Marca vermelha isolada não caracteriza lápide. Com HUD/HP válido e sem cabeçalho de restauração, duas observações de ausência liberam o fluxo sem clique.
- Quadro inválido ou evidência forte contraditória permanece inconclusivo: não significa perda confirmada nem ausência confirmada.
- Removido o laço de procura de 6–10 segundos após morte. Reconfirmação do ícone tem no máximo três amostras, separadas por 250 ms. A latência de captura e OCR depende do PC.
- A ausência é avaliada antes de aguardar um painel que pode não existir. Painel já aberto continua exigindo contadores estáveis para restaurar EXP/equipamentos.
- Clique usa a posição reconhecida e exige estabilidade da posição antes de cada tentativa. Não há clique de sondagem em ícone ausente.
- Diagnóstico registra decisão, posição, tamanho da captura, quantidade de amostras e tempo da decisão.

Validação local: compilação sem avisos/erros e suíte completa de autotestes aprovada. Testes adicionais: quinze combinações de escala/brilho (67%, 75%, 100%, 125%, 150%; brilho 70%, 100%, 120%), cinco capturas negativas, deslocamento do ícone, rejeição de posição instável e de marca vermelha sem forma.

As variantes são transformações sintéticas da referência, não testes em quinze PCs. Não houve teste ao vivo nos personagens nem instalação local. Incluído no patch 0.10.20; a validação em jogo permanece necessária.
