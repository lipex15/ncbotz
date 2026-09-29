# PEXBOT Teste 0.11.3 — responsividade

- Fluxo de automação executado fora da fila da interface; pausa, cancelamento e proteções preservados.
- Inicialização das referências, descoberta de janelas e leitura de estatísticas fora da interface. Descobertas não se sobrepõem e não ativam o jogo.
- Estados visuais consolidados por cliente, sem acumular atualizações antigas nem redesenhar áudio inalterado.
- Diagnóstico limitado aos últimos 256 KiB/1500 linhas; sem reler arquivos inteiros ou redesenhar texto inalterado.
- Mantidos os novos visuais e as referências de reconhecimento. Incluída a correção conservadora de reconhecimento inicial da versão normal 0.10.59.
- Testes: regressões de fluxos/detectores, carga de 100 mil eventos e log de 8 MiB, captura visual e medição da resposta da interface sem comandos ao jogo.

Validação real de farm, reconexão e diárias nas contas do usuário ainda é necessária; testes offline não garantem todos os cenários do jogo.
