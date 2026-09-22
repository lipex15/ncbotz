# Interface renovada do PEXBOT

O plano visual foi aplicado inicialmente no canal de testes na versão 0.10.0. A automação permanece separada da apresentação, preservando as regras e configurações existentes.

## Estrutura

- Cabeçalho com identidade PEXBOT/Night Crows, versão e estado do bot.
- Barra lateral: Visão geral, Sapheras, T.A, Abadia, Rotinas diárias, Agenda de farm, Proteção, Atualizações e Configurações.
- Visão geral: clientes, destino de farm, Sapheras por cliente, captura de ponto, início/pausa/parada, status e atividade recente.
- Configurações especializadas permanecem em páginas próprias, sem esconder controles críticos.

## Comportamento a preservar

- Botão Iniciar deve validar cada cliente, destino, ponto obrigatório da T.A 1 e etapas da agenda antes de iniciar. O resumo da validação deve indicar qual cliente precisa de atenção.
- Escolhas por cliente devem oferecer Ambos, Cliente 1, Cliente 2 e Nenhum onde fizer sentido. Sapheras mantém seleção explícita na página principal.
- Agenda sequencial por cliente: adicionar/remover/reordenar destinos, duração individual e tempo total, salvamento, retomada da etapa após rotinas prioritárias. Não ativa Sapheras implicitamente.
- Proteção Anti Over Kill, Diárias, Diretivas e Correio devem refletir o estado efetivo de cada cliente.
- Notificação de atualização deve permanecer visível até o usuário abrir a página correspondente.

## Implementação aplicada

1. Estilos, paleta, cantos, emojis e estados interativos foram centralizados nos recursos WPF.
2. A navegação lateral e os cards foram modernizados sem alterar a validação do motor.
3. Iniciar, programar, pausar e parar continuam visíveis no painel de controle, ao lado do estado e da atividade recente.
4. Agenda ganhou cards próprios por cliente e configuração do nível do Estreito de Tenerys.
5. Atualizações mantêm o indicador vermelho até a página ser aberta.

O mock-up permanece como referência de identidade; os controles reais do PEXBOT foram preservados para não sacrificar funcionalidade.
