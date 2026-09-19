# PEXBOT

Aplicativo modular de automação visual para Night Crows.

## Ciclo disponível — v0.7.3

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
- escolhe aleatoriamente um entre três spots da T.A escolhida, sem repetir o anterior;
- reconhece dinamicamente o botão **Ir** no mapa;
- acompanha `Movendo-se`, `Aguardando no ponto fixo` e a chegada ao spot;
- confirma o descanso pelo estado realmente exibido, sem repetir `L` quando ele já está aberto;
- ativa a caça e confirma a tela de descanso antes de operar o próximo cliente;
- monitora o Som 2 de HP separadamente no processo de áudio de cada cliente;
- combina a referência curta e o alerta completo, com confirmação reforçada quando o som estiver misturado ao combate;
- mostra somente o estado essencial da proteção por cliente; os detalhes técnicos ficam no log persistente;
- ao reconhecer HP baixo, traz a janela correta para frente e usa o TP configurado
  entre 3 e 5 vezes;
- retorna à T.A configurada, compra poções no NPC interno e escolhe outro spot;
- reconhece e fecha com `Y` o aviso visual de agenda indisponível.
- reconhece qualquer tela de descanso e sai com `L` antes de abrir menus.
- quando `Comprar (Lote)` estiver apagado, fecha a loja e continua sem esperar um pop-up.

### Morte e Anti Over Kill

- confirma visualmente a tela **Você morreu** antes de agir;
- restaura preventivamente as duas abas de recursos após o renascimento;
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
