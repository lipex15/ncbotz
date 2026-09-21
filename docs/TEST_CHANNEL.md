# Canal de testes do PEXBOT

## Isolamento

| Item | Estável | Testes |
| --- | --- | --- |
| Código publicado | `lipex15/ncbotz` (`main`) | `lipex15/ncbotz-testing` (`main`) e `lipex15/ncbotz` (`testing`) |
| Release/atualizador | `github.com/lipex15/ncbotz/releases/latest` | `github.com/lipex15/ncbotz-testing/releases/latest` |
| Nome instalado | PEXBOT | PEXBOT Teste |
| Executável | `PEXBOT.exe` | `PEXBOT-Teste.exe` |
| Dados e logs | `%LOCALAPPDATA%\PEXBOT` | `%LOCALAPPDATA%\PEXBOT-Teste` |
| Pasta do app | `%LOCALAPPDATA%\Programs\PEXBOT` | `%LOCALAPPDATA%\Programs\PEXBOT-Teste` |
| Atalho e desinstalação | PEXBOT | PEXBOT Teste |

O canal de testes não importa automaticamente o banco de dados estável. Configure seus clientes e destinos nele separadamente. As duas instalações podem coexistir, mas **não rode os dois bots controlando a mesma janela do jogo ao mesmo tempo**.

## Trabalho e publicação

1. Faça as alterações na branch `testing`. Não publique uma tag da versão estável para testar.
2. Compile os dois canais e execute o autoteste visual e de áudio. Valide também o instalador de teste sem alterar a instalação estável.
3. Envie `testing` para a branch homônima no repositório estável e para `main` no repositório de testes.
4. Ao criar uma tag `test-vX.Y.Z` **somente no repositório de testes**, o workflow gera `PEXBOT-Teste-Setup-vX.Y.Z.exe` e seu `.sha256` na release de testes. O app de teste detecta a nova versão e a instala sem substituir o estável. Tags estáveis usam `vX.Y.Z`, sem o prefixo `test-`.
5. Só após validação no jogo e aprovação explícita, promova o código aprovado para `main` do repositório estável, ajuste a versão **e o canal padrão de compilação de `Testing` para `Stable`**, e publique uma tag estável. Isso não ocorre automaticamente.

O repositório de testes é público para permitir que o atualizador baixe os instaladores sem exigir token do GitHub. A publicação lá **não gera notificação de atualização na instalação estável**.

## Mock-up visual futuro

O redesenho será feito primeiro na branch/canal de testes. A referência enviada pelo usuário deve guiar layout, cores, cards, hierarquia e estados. Use **emojis onde o mock-up usa emojis**, não substitutos geométricos. Inclua animações discretas de navegação, hover, feedback e status, respeitando acessibilidade e desempenho em computadores mais lentos. Nenhuma dessas mudanças visuais deve chegar ao canal estável antes da validação e promoção explícita.
