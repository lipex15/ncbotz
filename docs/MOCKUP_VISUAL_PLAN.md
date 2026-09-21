# Plano da futura interface do PEXBOT

Este documento prepara a implementação do mock-up enviado pelo usuário. **Não altera a interface visual nesta entrega.**

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

## Implementação futura

1. Isolar estilos, cores, cantos, ícones e estados interativos em recursos WPF reutilizáveis.
2. Migrar uma página por vez sem mudar as regras do motor; manter compatibilidade das configurações existentes.
3. Implementar o novo botão Iniciar e painel de status com a mesma validação do motor.
4. Conferir 100%, 125% e 150% de escala do Windows e janela pequena, além de teclado e contraste.
5. Testar navegação, persistência, dois clientes, pausa/parada e instalação de atualização em build empacotado.

O mock-up é referência visual, não uma licença para renomear ou alterar o fluxo de automação nesta entrega.
