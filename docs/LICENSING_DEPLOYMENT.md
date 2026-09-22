# Ativação por máquina e publicação privada

## Estado seguro antes de liberar aos usuários

O aplicativo só exige login quando o build recebe **os dois** parâmetros
`LicenseServerUrl` e `LicensePublicKey`. Não publique um instalador com apenas um
deles. O fluxo de publicação do repositório privado rejeita ambos ausentes.
Versões antigas do aplicativo continuam sem essa trava; ativação offline não
desativa um executável antigo que o usuário já guardou.

O código novo pertence ao repositório privado `lipex15/pexbot-source`. Os
repositórios públicos `lipex15/ncbotz` e `lipex15/ncbotz-testing` continuam
servindo **instaladores** para preservar o endereço de atualização das versões
que os usuários já têm. Não tornar esses dois repositórios privados enquanto
existirem versões instaladas que consultam suas Releases.

O histórico de código já publicado nesses repositórios pode ter sido copiado.
Tornar um repositório privado ou reescrever seu histórico não apaga cópias.

## Servidor no PC do administrador

1. Publicar `src/BotNC.LicenseServer/BotNC.LicenseServer.csproj` para Windows.
2. Executar o servidor no PC do administrador, inicialmente só em
   `http://127.0.0.1:5927`, com `PEXBOT_LICENSE_DATA` apontando para uma pasta
   persistente protegida pelo usuário do Windows. Na **primeira** execução,
   definir `PEXBOT_ADMIN_INITIAL_PASSWORD` com uma senha forte de no mínimo
   16 caracteres. Depois que a senha for armazenada em hash, retirar a variável
   do ambiente. Nunca colocar essa senha no GitHub, no instalador ou em scripts
   compartilhados.
3. Fazer backup criptografado do arquivo `licenses.db` e guardar a cópia fora
   do PC. Ele contém as contas, vínculos e a chave que assina as ativações.
   Perder a chave quebra novas ativações e novas compilações com a chave antiga.
4. Configurar um endereço HTTPS estável que aponte para o serviço local.
   Não expor diretamente a porta do Windows na internet. Idealmente usar um
   hostname para a API de login e outro para o painel, com proteção adicional
   no hostname administrativo. Definir `PEXBOT_ADMIN_HOST` com o hostname do
   painel para impedir acesso ao caminho `/admin` pelo hostname da API.
5. Ler `GET /api/public-key` no servidor e configurar a chave pública como
   variável do repositório privado `PEXBOT_LICENSE_PUBLIC_KEY`. Configurar o
   endereço HTTPS como `PEXBOT_LICENSE_URL`.
6. Configurar o segredo `PEXBOT_RELEASE_TOKEN` no repositório privado. Ele deve
   ter permissão **somente de escrita nos dois repositórios públicos de
   distribuição**. Nunca incluí-lo no aplicativo.
7. Publicar primeiro no canal de testes e validar o primeiro login, reinício
   offline, atualização, recusa da segunda máquina e painel. Só depois criar a
   versão estável.

### Alertas opcionais no Telegram

Com `PEXBOT_TELEGRAM_BOT_TOKEN` e `PEXBOT_TELEGRAM_CHAT_ID` configurados **só no
servidor**, uma nova tentativa de segunda máquina envia aviso. Sem essas
variáveis, a tentativa ainda aparece no painel. Tentativas repetidas da mesma
máquina num intervalo de 15 minutos não geram uma sequência de alertas. Se o
servidor estiver desligado, ele não recebe a tentativa naquele momento.

## Política de uso offline

A ativação assinada fica protegida no perfil do Windows e amarrada a uma
identidade local mais a impressão da instalação do Windows. Depois de ativado,
o cliente **não consulta o servidor** para iniciar ou continuar o farm. O PC do
administrador pode estar desligado.

Esta escolha tem uma consequência: bloquear uma conta ou liberar uma troca de
PC no painel **não revoga a autorização offline já entregue** ao computador
anterior. Uma liberação de máquina pode deixar o computador antigo e o novo
capazes de usar o bot. Revogação imediata exigiria verificações periódicas,
contrárias à política de uso offline escolhida.

O nome do PC exibido no alerta é fornecido pelo próprio cliente e pode ser
alterado. Ele ajuda a investigar, mas não é prova forte de identidade. A
proteção por dispositivo aumenta o custo do compartilhamento; não torna um
executável local impossível de modificar.

## Atualizador

O app continua consultando as Releases públicas atuais, sem senha ou token de
GitHub embutido. O fluxo de release do repositório privado compila o código e
envia **somente** o instalador e o hash ao repositório público do canal correto.
Assim o código continua privado e os links antigos de atualização não mudam.
