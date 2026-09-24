# PEXBOT 0.10.20 — Lápide e Baú da Guilda

## Lápide

- Duas observações de uma tela válida sem lápide liberam o fluxo, sem clique de teste. Cor vermelha isolada não caracteriza lápide.
- Referência focada na forma central do ícone; cor, posição e imagem são avaliadas na mesma captura normalizada. Clique acompanha a posição reconhecida, com confirmação antes de cada tentativa.
- Removida a busca de 6–10 segundos. A ausência é conferida antes de esperar um painel que pode não existir. Captura ilegível continua distinta de ausência confirmada.
- A log técnica registra os sinais da decisão e o tempo gasto. Não foram alterados Agenda, Sapheras ou prioridade dos clientes.

## Baú do Tesouro da Guilda

- Verificação por cliente a cada 17 horas, persistida entre reinícios. Sem histórico, confere na primeira oportunidade segura. Rotinas prioritárias podem adiar a verificação.
- Aproveita o baú visível em visitas normais à Guilda, sem aberturas extras só para isso.
- Lê quantidade e reconhece botão/notificação, sem depender do nome ou desenho do item. Abre, confirma Item Obtido, dispensa o aviso e exige redução da quantidade antes de coletar outro baú.
- Confirma caixa vazia antes de concluir. A visita programada fecha a Guilda e retoma o descanso quando aplicável; a oportunista continua a tarefa original.
- Falha não dispara cliques cegos nem reaberturas contínuas: a tentativa também respeita 17 horas, com nova oportunidade em visitas normais. Não é garantia contra expiração com bot offline ou fluxo indisponível.

## Roteiro rápido de teste — nos dois clientes

1. Iniciar sem lápide: deve seguir após leitura válida, sem ir ao ícone ou ficar procurando.
2. Iniciar com lápide existente: deve abrir e restaurar EXP/equipamentos; repetir a observação quando houver uma morte natural no jogo.
3. Guilda com um ou mais baús: deve coletar um por vez, fechar cada aviso e só encerrar a coleta ao confirmar vazio.
4. Guilda vazia: não deve clicar em Abrir.
5. Parar/iniciar o bot: não deve reiniciar o intervalo de 17 horas nem abrir a Guilda apenas por ter reiniciado. Uma visita normal por outra rotina ainda pode coletar.

Se houver falha, enviar Técnico → Copiar log completo, cliente afetado e captura da tela. Não provocar mortes ou despesas só para testar.

## Validação automatizada

Compilação e suíte de políticas, referências visuais, OCR e áudio. Lápide: quinze variantes sintéticas de escala/brilho, negativos e posição deslocada. Baú: imagens fornecidas, independência do nome/desenho, sequência de contagens, intervalo, persistência e isolamento dos clientes. Esses testes não substituem execução real nos PCs; não houve instalação local nem comandos nos personagens.
