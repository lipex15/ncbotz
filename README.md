# PEXBOT

Aplicativo modular de automação visual para Night Crows.

## Canal de testes

O PEXBOT Teste é instalado em paralelo à versão estável, usa dados e atualização próprios e recebe versões pelo repositório [ncbotz-testing](https://github.com/lipex15/ncbotz-testing). As mudanças passam por esse canal antes de uma promoção explícita para a versão usada pelos demais. Consulte [o procedimento do canal de testes](docs/TEST_CHANNEL.md).

## Ciclo disponível — v0.10.4 (testes)

- A Diretiva passa a ser decidida primeiro pela lista lateral: **Disponível** em verde abre o atalho direto; nome verde com `/500`, `/50` ou `/55` significa que ela já está em andamento; nenhuma linha verde significa ciclo concluído.
- O atalho verde é clicado diretamente em `(1628, 210)`, sem percorrer `= → Guilda → Diretiva`.
- O painel da Guilda não é mais aberto para consultar se existe Diretiva. Uma leitura lateral incerta não gera clique nem abertura de painel e continua sujeita ao limite seguro de verificações.
- O nome sorteado da Diretiva é ignorado; a detecção da execução usa cor e contador.

## Ciclo anterior — v0.10.3 (testes)

- Ao iniciar dentro de uma T.A em descanso, o bot preserva o farm atual. A chegada já reconhecida ou o botão **Entrar** apagado impedem nova entrada e novo gasto de gold.
- A compra diária da Loja reconhece os contadores já completos (`3/3` em Comum e `1/1` em Invocação), registra cada categoria separadamente e não procura novamente o popup quando os itens estão esgotados.
- O novo ciclo da Loja começa às 13:01, com um minuto de segurança após o reset das 13:00.
- Diretivas 5/5 e Diretivas em andamento são encerradas sem usar recarga nem tocar em **Desistir**. Estados incertos usam espera progressiva e no máximo três verificações por ciclo.
- Falhas de painéis diários agora liberam a interface com `Esc`, restauram o descanso quando possível e usam espera progressiva, evitando cliques infinitos e personagem parado.
- O reconhecimento das imagens reais confirmou as duas formas de Diretiva completa e os dois estados de Loja esgotada enviados para o teste.

## Ciclo anterior — v0.10.2 (testes)

- Limite semanal de entradas da Agenda por cliente, compartilhado entre Abadia e Estreito; novas entradas são bloqueadas até segunda às 04:00 quando o limite é atingido. O farm já pago continua.
- Tempo zero confirmado em duas leituras bloqueia somente a masmorra esgotada até o reset semanal. Um minuto restante continua disponível; a estimativa local de dez horas não bloqueia saldo real.
- Etapas esgotadas são ignoradas; se não restarem opções, o cliente segue à T.A e a Agenda pode retomar após o reset semanal.
- Compra diária da Loja usa ciclo próprio, independente das Diárias e Diretivas.

## Ciclo anterior — v0.10.1 (testes)

- Página unificada **Abadia / Masmorra Anônima**, com acesso direto à configuração do Estreito pela Agenda.
- O nível 86/97/110 aparece somente para o Estreito de Tenerys; etapas da Abadia não exibem nível.
- Nova compra diária da Loja, opcional por cliente e com horário configurável: sai do descanso, abre a Loja, compra os lotes predefinidos de Comum e Invocação e retorna ao estado anterior.
- Compra em Lote somente é confirmada com `Y` depois do reconhecimento visual do popup correto; execução persistida até o reset diário das 04:00.

## Ciclo anterior — v0.10.0 (testes)

- Interface renovada em azul-marinho, com cards arredondados, navegação por emojis, estados interativos e hierarquia visual mais clara.
- Agenda individual limitada à Abadia da Lembrança e ao Estreito de Tenerys; ao concluir, retorna à T.A configurada para o cliente.
- Estreito de Tenerys (também chamado de Masmorra Anônima) nos níveis 86, 97 ou 110, com confirmação visual de cada tela, cinco spots e fuga para pontos inválidos.
- A Agenda agora é sequencial e termina: não reinicia implicitamente nem altera a escolha de Sapheras.
- Leitura do tempo semanal aceita durações acima de dez horas e evita nova entrada quando restar 00:01/00:00.
- Proteção contra cobrança repetida quando a chegada da masmorra atrasar ou a captura visual estiver temporariamente indisponível.
- Referências reais do Estreito incluídas no autoteste do pacote.

## Ciclo anterior — v0.9.15

- Diretivas: diferencia Aceitar, Em andamento/Desistir e 5/5; não cancela uma diretiva ativa.
- Coordenadas de farm independentes por T.A e cliente; a T.A 1 continua exigindo ponto capturado.
- Reconhecimento da loja Artigos da T.A 1 calibrado com capturas de chegada e loja aberta.
- Agenda, Diárias, Diretivas, Correio e Anti Over Kill selecionáveis por cliente; opção Ambos disponível.
- TP de emergência enviado uma vez por evento, sem sequência aleatória de consumo; recuperação de lápide com tentativas limitadas.
- Esperas visuais mantêm retorno rápido quando reconhecem a tela e dão tolerância adicional a máquinas lentas.
- Plano do futuro mock-up em `docs/MOCKUP_VISUAL_PLAN.md`; o novo visual não faz parte desta versão.

## Ciclo anterior — v0.9.14

### Rotinas Diárias e Diretivas

- horários configuráveis por ciclo, cujo reset diário ocorre às 04:00;
- missões roxas já aceitas e visíveis iniciam a rotina ao ligar o bot ou durante o farm, sem aguardar o horário; após concluí-las, retoma o farm configurado;
- executa as rotinas em todos os clientes ativos e mantém Sapheras com prioridade máxima;
- aceita as 30 Missões Diárias, encontra a missão roxa, teleporta e inicia a Campanha automática;
- abre a lista de missões em `(1879,154)`, tolera variações do tom roxo e usa a primeira linha diária encontrada;
- fecha avisos de Agenda encerrada com `Y` e tenta executar as Diárias em descanso, mantendo o fluxo ativo mesmo se o descanso não abrir;
- confirma a aceitação pelo aviso ou pelo contador `30/30` e fecha completamente o painel de Campanha antes de continuar;
- confirma a conclusão pela ausência estável das missões roxas e retorna ao farm anterior;
- observa a lista antes de usar o botão de mostrar/ocultar; aceita também uma única missão comum remanescente e nunca alterna a pena repetidamente no mesmo ciclo 30/30;
- quando o ciclo 30/30 está aceito e a lista aberta não tem missão roxa, registra a conclusão e volta ao farm, inclusive após reiniciar o bot;
- confirma o teleporte pelo botão e pela área de recursos, independentemente do nome do mapa; lê o popup na janela do cliente e recupera falhas transitórias sem encerrar o bot;
- se a captura independente não reconhecer o popup, confere novamente na tela do cliente em primeiro plano antes de enviar Y;
- aceita Diretiva de Guilda em Mapa Aberto, T.A ou Masmorras; o jogo conclui as cinco do local escolhido;
- reconhece Diretivas já concluídas (5/5), registra o ciclo antes de fechar a Guilda e não tenta aceitar de novo mesmo se o fechamento precisar ser recuperado; só volta a executar no próximo ciclo, no horário configurado;
- consulta o contador das Diárias antes de clicar em Aceitar Tudo: se já estiver 30/30, verifica as missões roxas pendentes e, na ausência delas, volta ao farm até o próximo ciclo;
- confirma a Diretiva pelo aviso ou pelo estado permanente `Em andamento` e fecha completamente a tela da Guilda;
- Diretiva de Mapa Aberto é aceita junto das Diárias;
- a execução realizada é persistida por cliente para não repetir após reiniciar o aplicativo;
- aceitação, início e conclusão são persistidos separadamente; após falha ou reinício, uma campanha já aceita é retomada em vez de ser ignorada;
- Diretiva de Mapa Aberto pendente continua elegível mesmo quando as Diárias já foram aceitas anteriormente;
- sair da Abadia para uma rotina programada preserva o limite configurado de retornos.

### Correio automático

- confere o Correio do Servidor diariamente às 01:00 e às 07:00, por cliente, inclusive após iniciar o bot mais tarde;
- abre pelo menu `=`, usa **Receber Tudo** somente quando há notificação vermelha, fecha **Item Obtido** e retorna ao jogo;
- reabre o descanso quando o cliente estava farmando, inclusive em Diária automática; espera a caixa carregar e, se a entrega atrasar alguns minutos, reavalia antes de encerrar o horário;
- não abre o Correio sobre um popup de teleporte diário ainda pendente;
- a conferência de cada horário é persistida para não repetir a coleta no mesmo dia.

### Atualizações

- consulta a versão pública mais recente pelo redirecionamento de lançamentos do GitHub, sem depender do limite da API que causava HTTP 403;
- mantém a verificação SHA-256 antes de instalar o pacote.

### T.A 1 (Codex)

- entra em Kildebat e compra Artigos pelo mesmo fluxo das outras T.A;
- exige coordenada personalizada capturada no mapa com zoom mínimo;
- recolhe as laterais, reduz o zoom automaticamente ao máximo e seleciona o ponto sem Favoritos;
- acompanha deslocamentos longos pela tela de descanso antes de ativar a caça.

### Abadia da Lembrança

- opção independente por cliente, com T.A 2/3 configurada como destino de reserva;
- Sapheras permanece obrigatória e prioritária quando a Abadia está ativa;
- confirma a aba Especial, o cartão da Abadia, o popup de entrada paga e um dos dois locais possíveis de chegada;
- usa um dos quatro pontos fixos informados ou uma coordenada personalizada capturada no mapa, sem abrir Favoritos;
- limita os retornos pagos após a primeira entrada; quando o limite acaba, segue para a T.A escolhida;
- após Y, não tenta uma segunda entrada paga sem antes verificar se a chegada ocorreu;
- na morte sem lápide de restauração, segue o fluxo sem insistir em restaurar recursos inexistentes;
- o agendamento de início passa a começar em zero minuto por padrão.
- reconhece diretamente o mapa aberto da Abadia antes de selecionar a coordenada de farm.
- restaura uma lápide pendente mesmo quando o bot é iniciado depois da morte; exige confirmação forte do ícone na inicialização e, se não houver painel, registra a ausência por cliente para não reiniciar a busca ao reiniciar o bot no mesmo ciclo.
- Anti Over Kill fica na aba **Proteção**; os quadros informativos “Etapas protegidas” e “Base modular” foram removidos da interface.

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

### T.A 1, T.A 2, T.A 3 e proteção PvP

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
- lê o nível diretamente da linha do primeiro favorito com mais de uma preparação da imagem; se essa leitura falhar, retoma no mapa/Favoritos já abertos sem começar a T.A novamente;
- se o jogo fechar o descanso durante um trajeto longo, reabre com `L` e continua observando a chegada ao mesmo spot antes de ativar a caça;
- confirma o descanso pelo estado realmente exibido, sem repetir `L` quando ele já está aberto;
- reafirma o foco antes de cada `L` e `Q`, repetindo localmente a ativação da caça sem abandonar o spot quando uma tecla for ignorada;
- ativa a caça e confirma a tela de descanso antes de operar o próximo cliente;
- monitora o alerta sonoro de HP baixo separadamente no processo de áudio de cada cliente;
- combina a referência curta e o alerta completo, com confirmação reforçada quando o som estiver misturado ao combate;
- mostra somente o estado essencial da proteção por cliente; os detalhes técnicos ficam no log persistente;
- somente o alerta sonoro de HP baixo pode acionar o TP; a leitura visual continua detectando morte, mas não HP baixo;
- ao reconhecer o som de HP baixo, traz a janela correta para frente e usa o TP configurado
  entre 3 e 5 vezes;
- depois do TP, não tenta deduzir a cidade por um nome de mapa; a recuperação só é declarada concluída quando a chegada à T.A ou Sapheras for confirmada visualmente;
- se uma retomada falhar, repete o fluxo automaticamente com espera progressiva de até 30 segundos, sem martelar cliques e sem deixar o cliente bloqueado;
- após duas falhas consecutivas, fecha apenas sobreposições reconhecidas com `Esc`, usa uma vez o TP de emergência configurado e reinicia a rota completa do cliente;
- retorna à T.A configurada, compra poções no NPC interno e escolhe outro spot;
- reconhece e fecha com `Y` o aviso visual de agenda indisponível.
- a atividade recente tem **Copiar log**, que copia o registro completo da execução atual para compartilhar um erro;
- confirma o foco sem uma pausa fixa longa e lê o botão de compra assim que sua aparência estabiliza, mantendo esperas visuais para PCs mais lentos;
- verifica a oferta eventual **Novos Produtos Disponíveis** antes das ações, fecha no `X` e confirma que ela desapareceu antes de retomar a mesma etapa.
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
aplicativo; se ele for fechado, o início programado é cancelado. Missões Diárias e Diretivas
possuem horários próprios e persistem o ciclo já executado.

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
