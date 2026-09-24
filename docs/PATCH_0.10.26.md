# PEXBOT 0.10.26

- Boss do Amor: o botão Auto só recebe clique após duas leituras positivas de desligado. Ligado ou desconhecido não autoriza alternar o botão. Reutiliza o detector do halo ligado/desligado das referências fornecidas.
- O cliente sem Sapheras continua acompanhando o Boss durante a janela de Sapheras do outro cliente. O fim da janela não o envia para farm se ainda estiver na raide.
- Após concluir a raide e tratar a recompensa, cada cliente pode retornar ao farm sem esperar a outra janela terminar.
- Interação manual não é falha do Boss nem causa recuperação da raide. Uma preparação normal interrompida mantém uma retomada automática pendente, sem incrementar falhas ou reiniciar o farm já ativo.
- Descanso permanece o padrão das rotas. Abrir a tela manualmente não desfaz o farm; Auto ligado com tela aberta continua aceito. Monitoramento e proteção permanecem ativos enquanto comandos normais cedem o mouse ao usuário. Não há botão de retomada.
- Clientes em tempo real recebe estado individual do motor, separado do histórico de mensagens: Boss, retorno, Diárias, Sapheras, farm individual/Agenda, restauração e etapa pendente. Indica descanso ou Auto ligado quando observados.

## Testar no jogo

1. Entrar no Boss já com Auto ligado: não deve desligar. Com Auto comprovadamente apagado, deve ligar uma vez.
2. Acompanhar um cliente no Boss enquanto o outro participa de Sapheras; confirmar resultado, saída, recompensa e retorno.
3. Deixar o farm em descanso, abrir manualmente e usar inventário: não deve reiniciar T.A nem disputar cliques. Fechar inventário deve liberar naturalmente as próximas ações normais.
4. Conferir os estados individuais na tela inicial.

Testes automatizados não substituem validação no jogo. Mantidos 1920×1080 e escala Windows 100%. Não altera estatísticas já registradas nem configurações dos clientes.
