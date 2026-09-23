# PEXBOT 0.10.18 — Agenda e masmorras individuais

- Abadia / Anônima permite ativar por cliente o farm individual de Abadia ou Anônima nos níveis 86, 97 e 110.
- Cards lado a lado por masmorra, cada um com opções para os dois clientes. Limite semanal de entradas somente no modo individual, separado por masmorra e cliente. Os contadores individuais começam separados das entradas de Agenda das versões anteriores.
- Agenda sequencial não aplica limite de entradas: segue a duração de farm configurada ou o esgotamento de tempo da masmorra confirmado pelo detector existente. As proteções contra cobrança incerta permanecem.
- Agenda antiga esperando reset por bloqueio volta a avaliar suas etapas. Tempo realmente esgotado continua impedindo a masmorra; uma Agenda normalmente concluída não reinicia automaticamente.
- Corrigida inicialização que adotava qualquer descanso como destino correto, ignorando uma Agenda ativa em outra área.
- Tempo restante da etapa e da Agenda exibidos por cliente na Visão geral e na Agenda, sem modificar a duração configurada. Contagem depende de farm ativo na masmorra correspondente; saída, recuperação e outras rotinas pausam o consumo. Saldo salvo periodicamente e ao parar.
- Campo de limite inválido no cliente inativo ou em modo Agenda não impede iniciar.

Validação: compilação, testes automatizados de políticas e referências, OCR e áudio, inspeção visual da interface. Não houve instalação nem execução do fluxo nos personagens. A confirmação de localização e farm ainda depende dos detectores; validar no jogo após atualizar.
