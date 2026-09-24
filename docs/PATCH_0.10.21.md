# PEXBOT 0.10.21 — Lápide e Baú da Guilda

Esta versão inclui as mudanças planejadas para 0.10.20, cuja publicação foi bloqueada pelos testes antes de chegar ao atualizador. Corrige também a leitura do contador do baú no Windows com OCR em inglês, onde BAÚ foi reconhecido como BAT. Nenhuma instalação local é necessária para publicar; a atualização chega pelo aplicativo.

## O que entrou

- Lápide: duas observações de tela válida sem ícone liberam o fluxo, sem clique de teste nem busca de 6–10 segundos. Forma, cor e posição devem concordar para clicar. Captura ilegível não é tratada como ausência.
- Baú da Guilda: verificação a cada 17 horas por cliente, persistida entre reinícios; sem histórico, na primeira oportunidade segura. Rotinas prioritárias podem adiar a visita.
- Coleta também quando o baú estiver visível em visitas normais à Guilda, sem aberturas extras.
- Reconhecimento por quantidade, botão e notificação, sem ler nome ou desenho do item. Confirma Item Obtido, dispensa o aviso e exige redução da quantidade antes de abrir o próximo baú. Confirma vazio antes de concluir.
- Tentativas também respeitam o intervalo de 17 horas para não reabrir infinitamente após uma falha. Visitas normais oferecem nova oportunidade. Não há garantia contra expiração com o bot offline ou o fluxo indisponível.
- Logs técnicos de decisão e verificações automatizadas antes da publicação, incluindo diferenças do OCR do Windows. Agenda, Sapheras e prioridade dos clientes não foram reformuladas.

## Teste nos dois clientes

1. Iniciar sem lápide: seguir após leitura válida, sem clicar ou ficar procurando o ícone.
2. Com lápide existente: abrir e restaurar EXP/equipamentos. Observar também após uma morte natural; não provocar mortes só para testar.
3. Guilda com um ou mais baús: coletar um por vez, dispensar cada aviso e confirmar vazio.
4. Guilda vazia: não clicar em Abrir.
5. Parar/iniciar: não reiniciar o intervalo nem abrir a Guilda só pelo reinício; visitas normais ainda podem coletar.

Se ocorrer falha, enviar Técnico → Copiar log completo, cliente afetado e captura da tela.

## Validação

Testes automatizados de políticas, referências, OCR, áudio, escala/brilho, falsos positivos, persistência e isolamento entre clientes. Isso não substitui validação no jogo nos PCs dos usuários. Nenhum comando foi enviado aos personagens durante a publicação.
