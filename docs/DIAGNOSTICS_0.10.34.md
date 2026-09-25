# Diagnóstico e regressões — 0.10.34

## Origem e limites

Auditoria das duas execuções longas da 0.10.33 (um cliente no computador do amigo e dois no computador principal). As diferenças 1920×1032 e 1920×1040 de captura são preservadas nas evidências; não foi adicionada compatibilidade com outras resoluções.

Houve morte sinalizada às 21:28:23 e atendimento às 21:43:28. O intervalo é comprovado, mas as logs antigas não determinam a causa exata da demora. Esta versão antecipa o atendimento de pendências no monitor de farm, evita que uma retomada normal preceda o TP pendente e mede fila, pausa e ação atual. Não se afirma que a hipótese de origem foi reproduzida no PC remoto. Pausa explicitamente acionada pelo usuário continua respeitada; interação manual não é equivalente a pausa.

## Referências selecionadas

| Arquivo de regressão | Diagnóstico de origem | Uso |
| --- | --- | --- |
| regression_loading_city.png | 20260924-173007-startup_observation_2.png | Carregamento após ressurreição, não erro de restauração |
| regression_loading_landscape.png | 20260924-184438-startup_observation_1.png | Segunda cena real de carregamento com a mesma decisão |

As imagens foram inspecionadas e copiadas sem alterações; contêm cenas de carregamento, não os dados de inventário ou a interface de contas dos outros diagnósticos. A leitura usa o indicador genérico Carregando, não o cenário. Há teste negativo com HUD de farm. Outras capturas não foram promovidas a referência sem confirmação do estado correto.

O teste de preço usa a referência já aprovada statistics_article_purchase.png com a região da moeda removida em memória, em três posições verticais. Deve continuar lendo 244.100 pelo total, nunca pelo saldo ou quantidade. Telas alheias e números ambíguos continuam rejeitados.

## Como correlacionar uma falha

1. Localizar `action_failure`, a exceção ou a decisão que não avançou.
2. Usar `trace[run=...; action=...]` para reunir início, etapas, reconhecimento e fim da ação.
3. Abrir o PNG indicado por `diagnostic_saved`, `action_failure_evidence` ou `statistics_price_unresolved` e o arquivo `.png.json` adjacente.
4. Comparar horário, cliente, janela, destino, farm, Auto, descanso, restauração, diárias, boss, agenda, captura e proteção pendente.
5. Para esperas longas, verificar `monitor_heartbeat`, `paused`, `pauseVersion`, `pendingAgeMs` e `protection_service queueMs`.

`recognition` registra pontuação, limiar, posição, tamanho bruto/normalizado, região de busca e dimensões do recorte de referência. Mudanças de resultado ou de pelo menos 0,05 na pontuação geram novo registro; resultados estáveis são limitados a um por minuto por referência. O heartbeat é limitado a um por 30 segundos por cliente. Evidência adicional de falha de ação é limitada a uma imagem por 30 segundos por cliente; diagnósticos específicos de fluxos permanecem disponíveis.

Arquivos permanecem no diretório local Diagnosticos do PEXBOT. O bot não envia imagens ao GitHub nem altera suas referências automaticamente. Novas referências devem ter estado confirmado, recorte sem informação desnecessária e testes positivos e negativos antes de serem publicadas. Nunca tratar uma falha visual isolada como autorização para comprar, teleportar ou alternar Auto.
