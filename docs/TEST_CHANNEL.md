# Canal de testes do PEXBOT

## Isolamento

| Item | Estável | Testes |
| --- | --- | --- |
| Código privado | `lipex15/pexbot-source` (`main`) | `lipex15/pexbot-source` (`testing`) |
| Release/atualizador | `github.com/lipex15/ncbotz/releases/latest` | `github.com/lipex15/ncbotz-testing/releases/latest` |
| Nome instalado | PEXBOT | PEXBOT Teste |
| Executável | `PEXBOT.exe` | `PEXBOT-Teste.exe` |
| Dados e logs | `%LOCALAPPDATA%\PEXBOT` | `%LOCALAPPDATA%\PEXBOT-Teste` |
| Pasta do app | `%LOCALAPPDATA%\Programs\PEXBOT` | `%LOCALAPPDATA%\Programs\PEXBOT-Teste` |
| Atalho e desinstalação | PEXBOT | PEXBOT Teste |

O canal de testes não importa automaticamente o banco de dados estável. Configure seus clientes e destinos nele separadamente. As duas instalações podem coexistir, mas **não rode os dois bots controlando a mesma janela do jogo ao mesmo tempo**.

## Trabalho e publicação

1. Faça e valide as alterações no repositório privado `pexbot-source`. Não envie o código novo aos repositórios públicos de download.
2. Compile os dois canais e execute os autotestes visual e de áudio. Valide o instalador de teste sem alterar a instalação estável; registre separadamente a validação no jogo e o que ainda não foi observado.
3. Mantenha as branches privadas `main` e `testing` alinhadas ao código aprovado para cada canal.
4. A tag `test-vX.Y.Z` no **repositório privado** dispara o workflow que publica `PEXBOT-Teste-Setup-vX.Y.Z.exe` e `.sha256` no repositório público `ncbotz-testing`. A tag `vX.Y.Z`, também privada, publica o canal normal em `ncbotz`. O workflow seleciona `BuildChannel` automaticamente.
5. A promoção ao uso normal exige autorização explícita; não ocorre automaticamente por publicar no canal de testes. Não apresente autotestes como garantia de funcionamento integral no jogo.

O repositório de testes é público para permitir que o atualizador baixe os instaladores sem exigir token do GitHub. A publicação lá **não gera notificação de atualização na instalação estável**.

## Interface

O patch 0.10.14 inclui cards e controles arredondados, legibilidade durante execução, atividade por cliente e resumo de início opcional. Os canais usam as mesmas melhorias quando sua publicação é autorizada. Capturas renderizadas revisam o layout, mas não substituem testes de uma sessão real no jogo.
