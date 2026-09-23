# PEXBOT 0.10.16 — estabilidade de rotinas e diagnóstico

## Atividade clara, diagnóstico separado

- Atividade recente com horário, identificação do cliente, mensagens curtas e cores para progresso, atenção e proteção acionada.
- Detalhes técnicos ficam ocultos por padrão. O botão **Técnico** abre uma área própria; **Copiar diagnóstico** copia o arquivo completo da execução, mesmo quando a visualização mostra apenas as últimas 1.500 linhas.
- Diagnóstico inclui versão, ambiente, janela/processo, mudanças de responsável pela ação, comandos enviados, leituras visuais relevantes, confiança, estado das rotinas, tentativas, prazos e exceções.
- Telemetria rotineira de áudio passa a ser registrada a cada cinco minutos ou quando a saúde da captura muda. Alertas e falhas continuam registrados quando ocorrem.

## Fluxo por cliente

- Capturas das verificações comuns e esperas visuais passam a usar a janela do cliente responsável pela ação. Comandos globais verificam a janela exata antes do envio; a vigilância em segundo plano não toma o foco de outro fluxo.
- A inicialização verifica restauração e pode preservar uma campanha automática de Diárias já reconhecida antes de abrir Correio ou seletor da T.A.
- Diárias em andamento não são mais marcadas internamente como farm da T.A. Aceitação, início e conclusão permanecem separados e salvos por usuário do PEXBOT e posição do cliente, sem associação ao apelido do personagem.
- Diárias registradas como concluídas não são reabertas por uma simples leitura de cor roxa. A área de busca é limitada à lista lateral; uma leitura desconhecida não é conclusão nem confirmação de teleporte pendente.
- Leitura inconclusiva tem tentativas limitadas e devolve ao farm quando possível, preservando o progresso e aguardando dez minutos para reavaliar. O intervalo não é mais apagado pelo retorno da rotina.
- Diretiva ativa e Diretiva concluída têm estados separados. Reiniciar o bot não força a reabertura de uma Diretiva já registrada no ciclo. A rotina de Diretiva isolada pode retomar o farm confirmado no ponto atual sem refazer a entrada da T.A.
- Falhas repetidas de recuperação têm espera ampliada e deixam o outro cliente disponível. Nenhuma espera autoriza retorno ao combate quando restauração continua pendente.

## Restauração, captura e Boss do Amor

- Na checagem inicial, ausência da lápide exige HUD válido em três capturas. Contadores de EXP/equipamento precisam repetir a mesma aba e quantidade antes de serem usados; leitura instável não libera restauração.
- Cliques da lápide e de restauração consideram as dimensões da janela. A captura se ajusta ao redimensionamento; reconexão não substitui silenciosamente um processo fechado por outro cliente.
- Boss do Amor usa o ícone próximo ao minimapa em (413, 127), com preparação nos três minutos anteriores, respeitando Sapheras quando habilitada naquele cliente.
- Diárias interrompidas pelo Boss têm retomada persistida. Correio, farm e outras rotinas não disputam ações enquanto o cliente está na sala.
- O reconhecimento do Auto usa um recorte menor, sem o entorno animado. Após um comando sem confirmação, o botão não é alternado repetidamente às cegas; a sala continua monitorada.
- Botão de recompensa não encontrado não significa prêmio recebido: exige evidência de coleta. Contadores do Boss precisam de leituras estáveis.

## Verificação e limites

Compilação, testes locais de políticas, imagens de referência, OCR e áudio; inspeção da interface renderizada. A publicação também executa os testes sobre os dois pacotes gerados.

Não houve instalação local nem execução de rotinas nos personagens para validar este patch. O teste real no jogo continua necessário. Não há garantia de ausência de falhas, nem recuperação de conexão do jogo após DC.

Para relatar um problema: abra **Técnico → Copiar log completo** logo após o ocorrido, informe qual cliente foi afetado e envie uma captura da tela do jogo. O diagnóstico contém caminhos locais e identificadores de processo; compartilhe apenas com quem fará a análise.
