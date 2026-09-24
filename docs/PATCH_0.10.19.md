# PEXBOT 0.10.19 — Morte, Anônima e retomada do fluxo

- Morte sinalizada pela vigilância bloqueia comandos normais e interrompe a espera de telas. A interrupção não aplica espera de recuperação antes de Ressuscitar. Artigos verifica morte antes e depois do teleporte.
- Vigilância verifica morte antes de acionar TP por áudio ou HP visual. A proteção contra acionamentos simultâneos é compartilhada por cliente; removida a sequência de três teclas sem verificar resultado. Uma nova tentativa com foco continua permitida quando a chegada do TP em segundo plano não foi confirmada.
- A vigilância volta a atender novas mortes durante a viagem de retorno após restaurar recursos.
- Removida a sondagem da lápide por coordenada sem reconhecimento. Ausência estável em tela válida permite seguir; captura incerta não é registrada como restauração concluída. Cada tentativa de clique exige novamente o ícone vermelho e sua imagem de referência.
- Chegada à Anônima aceita também OCR de Posto Aéreo da Ilusão, tempo da masmorra e HUD, juntos. Duas observações confirmam a chegada. Mantida a proteção contra repetir cobrança quando a chegada é desconhecida.
- Confirmação específica para sair do Estreito de Tenerys antes do Boss. Consulta de recompensas recupera abertura do menu lateral com tentativas limitadas, sem clicar no atalho quando o menu não apareceu.

## Validação

Testes automatizados de isolamento entre clientes, bloqueio após morte e concorrência de emergências; capturas reais das falhas de chegada às 17:15 e 22:05 (incluindo inventário aberto); controle negativo na seleção de T.A; referências visuais, OCR e detector de áudio.

Não houve instalação nem execução dos comandos nos personagens durante o desenvolvimento. Testes com imagens e políticas não substituem validação em jogo, especialmente em outros PCs. O relato de lápide ausente no PC do amigo não possui log desta execução; o caminho de clique cego encontrado no código foi removido.
