# PEXBOT 0.10.34

- Proteção pendente atendida antes das tarefas de rotina no monitor de farm. O TP em segundo plano invalida comandos e esperas da rota anterior, evitando continuar procurando o NPC após sair do local.
- Carregamento após ressurreição recebe uma espera limitada, sem ser tratado imediatamente como erro de restauração.
- Recuperação de interface exige HUD confirmado e ausência de painéis conhecidos antes de anunciar sucesso e reabrir a rota.
- Auto é conferido novamente após fechar o descanso: Q só é enviado com duas leituras de Auto desligado. Auto ativo é preservado.
- Recompensa diária e semanal do Boss do Amor têm confirmação persistente separada do fechamento do aviso. O fechamento pode ser repetido somente enquanto o aviso estiver reconhecido, sem repetir a coleta.
- Interação manual durante a leitura das diárias preserva o farm e o progresso; não aplica penalidade de dez minutos por uma falsa falha.
- Total de compras em lote pode ser lido pelo rótulo Preço e pelo popup confirmado quando a moeda não corresponde à referência. OCR continua exigindo concordância entre leituras, sem inventar valores.
- Logs técnicos incluem identificação da execução e da ação, duração, estado por cliente, fila de proteção, pausa explícita, evidências de reconhecimento, limiar, região procurada e caminho das imagens. Imagens novas recebem metadados locais em JSON.
- Duas capturas reais de carregamento foram selecionadas como testes de regressão. Não há aprendizado automático indiscriminado nem envio automático de imagens.

Validação: compilação e testes offline de reconhecimento, estados, estatísticas e proteção. O comportamento completo em cada computador ainda depende do teste em jogo.
