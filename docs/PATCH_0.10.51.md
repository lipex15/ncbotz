# PEXBOT 0.10.51

- Lápide: após morte confirmada ou pendência salva, o fluxo tenta abrir o atalho conhecido com a tela de jogo exposta, em vez de exigir repetidamente a mesma correspondência do ícone. A leitura também considera a distribuição vermelha em toda a região do atalho.
- A pendência só é encerrada depois do contador do painel e das listas vazias; a área não é usada para declarar “sem perdas”.
- Abadia: a coordenada personalizada continua valendo quando a etapa vem da Agenda.
- Anônima/Estreito: adicionada coordenada personalizada própria; ela também é usada quando a entrada é feita pela Agenda.

- Skill 6: leitura independente da borda branca, preservação do estado ativo e duas observações de estado desligado antes da única tecla. A rotina não fica verificando a skill durante o farm.
- Serviços da cidade: corrigido o arquivo da referência de Armas, cujo recorte excedia o tamanho da imagem e interrompia ações de reposição/proteção.
- HUD: a barra de habilidades é uma evidência adicional de tela de jogo aberta, evitando depender apenas do ícone decorativo do menu.
- Agenda: ao adotar farm já em andamento na masmorra, respeita o ponto personalizado, sem exigir ativação do modo individual.

Não há espera por HP cheio. Uma tela realmente ilegível preserva a pendência, reinicia a captura e permite reavaliar a ação/reconexão; não é declarada restauração bem-sucedida sem confirmação do painel. Instalação em E: e dados no AppData de C: continuam separados, sem mudança de pastas.

Validação: compilação e testes de regressão com capturas de referência, incluindo skill ligada/desligada, reconexão, lápide em escalas/brilhos diferentes, falsos ícones e referências da cidade. Isso não equivale a um teste ao vivo em todos os PCs.
