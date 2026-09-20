# PEXBOT

Aplicativo modular de automação visual para Night Crows.

## Ciclo disponível — v0.7.6

### Sapheras

- permite ativar só o Cliente 1, só o Cliente 2 ou ambos; com ambos, o Cliente 1 tem prioridade;
- Sapheras pode ser ligada ou desligada separadamente para cada cliente;
- se Sapheras ainda estiver distante, começa imediatamente na T.A escolhida para cada cliente;
- a janela de proximidade é configurável em minutos no aplicativo;
- ao chegar o horário, interrompe o farm e entra em Sapheras de forma sequencial;
- Sapheras tem prioridade máxima e interrompe inclusive compra, deslocamento ou recuperação de PvP em andamento;
- agenda a entrada para o horário definido ao iniciar o bot;
- sai da tela de descanso com `L` quando necessário;
- abre o menu com `=` e entra em Ruínas de Sepheras;
- confirma a chegada em **Atalaia Erodida** por reconhecimento visual;
- anda com `W` por 3,5 segundos;
- utiliza de 1 a 5 teleportes com tecla configurável;
- ativa a caça com `Q` e entra no descanso com `L`;
- confirma **Caça automática em uso** e mantém o contador da sessão.

### T.A 2, T.A 3 e proteção PvP

- cada cliente pode usar T.A 2 ou T.A 3 independentemente;
- compra o lote configurado no NPC **Artigos** dentro da própria T.A;
- usa o primeiro favorito como área de farm e o segundo como teleporte opcional;
- quando não existe o segundo favorito, segue direto para a área de farm;
- reconhece o nível do primeiro favorito e seleciona o conjunto correto de coordenadas;
- na T.A 2, reconhece os níveis 68, 72, 76, 80, 84 e 88;
- na T.A 3, reconhece os níveis 84, 88, 90, 92, 94, 96, 98 e 100;
- escolhe aleatoriamente uma das três posições do nível reconhecido, sem repetir a anterior;
- permite capturar um ponto fixo no mapa separadamente por cliente, substituindo o sorteio quando ativado;
- reconhece dinamicamente o botão **Ir** no mapa;
- acompanha `Movendo-se`, `Aguardando no ponto fixo` e a chegada ao spot;
- confirma o descanso pelo estado realmente exibido, sem repetir `L` quando ele já está aberto;
- ativa a caça e confirma a tela de descanso antes de operar o próximo cliente;
- monitora o alerta sonoro de HP baixo separadamente no processo de áudio de cada cliente;
- combina a referência curta e o alerta completo, com confirmação reforçada quando o som estiver misturado ao combate;
- mostra somente o estado essencial da proteção por cliente; os detalhes técnicos ficam no log persistente;
- somente o alerta sonoro de HP baixo pode acionar o TP; a leitura visual continua detectando morte, mas não HP baixo;
- ao reconhecer o som de HP baixo, traz a janela correta para frente e usa o TP configurado
  entre 3 e 5 vezes;
- depois do TP, confirma visualmente o **Posto de Patrulha Sul** e o menu de retorno antes de tentar reentrar na T.A; se isso falhar, não repete indefinidamente;
- após três falhas consecutivas, suspende a reentrada automática daquele cliente até reiniciar o bot, mantendo a vigilância de morte e do alerta sonoro;
- retorna à T.A configurada, compra poções no NPC interno e escolhe outro spot;
- reconhece e fecha com `Y` o aviso visual de agenda indisponível.
- reconhece qualquer tela de descanso e sai com `L` antes de abrir menus.
- quando `Comprar (Lote)` estiver apagado, fecha a loja e continua sem esperar um pop-up.

### Morte e Anti Over Kill

- confirma visualmente a tela **Você morreu** antes de agir;
- confirma a abertura do painel e restaura as duas abas de recursos separadamente após o renascimento;
- lê os contadores das abas de EXP e equipamento e só conclui cada restauração quando a lista indicar zero;
- não retoma a T.A enquanto o painel de restauração continuar aberto;
- tenta abrir a lápide em `(1537,72)` e restaura XP/equipamento nos pontos configurados antes de retomar;
- registra as mortes separadamente para cada cliente;
- ao atingir o limite configurado dentro da janela escolhida, inicia a Agenda segura;
- permanece na Agenda pelo tempo definido e depois retorna à cidade com o TP de emergência;
- fecha com `Y` o aviso de Agenda encerrada e recompõe o farm da T.A;
- Sapheras mantém prioridade máxima e pode interromper a Agenda no horário programado.

As imagens de reconhecimento e configurações são registradas em um banco SQLite
local em `%LOCALAPPDATA%\PEXBOT\pexbot.db`.
Os eventos da execução e do detector de HP também são gravados em
`%LOCALAPPDATA%\PEXBOT\Logs`.

O ciclo continua até o usuário pausar ou parar o bot.

O início pode ser programado para depois de 1 a 1440 minutos. A contagem ocorre no próprio
aplicativo; se ele for fechado, o início programado é cancelado. Ainda não existem rotinas
diárias de guilda ou missões: essas atividades precisarão de mapeamento e regras próprias.

Para usar um ponto personalizado, abra no jogo o mapa, selecione o primeiro favorito e o
zoom desejado; no PEXBOT, clique em **Capturar ponto** do cliente certo e então clique no
local do mapa. A posição é salva por cliente. Marque **Usar personalizada** para sempre
usar esse ponto em vez das três posições aleatórias. O segundo favorito continua sendo
um teleporte opcional.

## Instalação e atualizações

Baixe o instalador `PEXBOT-Setup-vX.Y.Z.exe` na seção **Releases** deste repositório. Ele instala `PEXBOT.exe`, cria atalhos PEXBOT no menu Iniciar e na área de trabalho e mantém as configurações em `%LOCALAPPDATA%\PEXBOT` durante atualizações. Na primeira abertura, as configurações da versão BOT NC anterior são copiadas automaticamente, quando existirem.

Dentro do app, abra **Atualizações**, clique em **Verificar atualizações** e, quando houver uma versão nova, em **Baixar e instalar**. O app baixa apenas os dois arquivos de uma release deste repositório, confere o SHA-256 do pacote, encerra, atualiza a instalação silenciosamente e reabre sozinho já na versão nova. O assistente de instalação não reaparece durante uma atualização. Se a verificação falhar, o pacote baixado é descartado. A primeira instalação precisa ser feita com o instalador, não com o executável portátil antigo.

O instalador é compilado para Windows x64 e inclui o .NET necessário. Os binários ainda não têm assinatura Authenticode; a assinatura requer um certificado de publicação do proprietário. A checagem SHA-256 protege a integridade do download, mas não substitui uma assinatura de código.

Uma tag `vX.Y.Z` correspondente à versão em `src/BotNC.App/BotNC.App.csproj` dispara o fluxo `.github/workflows/release.yml`, que publica o instalador e o arquivo `.sha256` na release. A versão mais recente é a que a aba do app oferece.

## Executar para desenvolvimento

```powershell
dotnet run --project .\src\BotNC.App\BotNC.App.csproj
```

## Compilar

```powershell
dotnet build .\PEXBOT.slnx --configuration Release
```

Para gerar o instalador localmente, publique o app para `win-x64` em `publish/` e compile `installer/PEXBOT.iss` com Inno Setup 6. O diretório `artifacts/` receberá o instalador.

O aplicativo foi desenvolvido para monitor principal em **1920×1080**.
