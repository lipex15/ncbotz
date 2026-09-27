# PEXBOT 0.10.52 — Grupo automático

- Nova configuração na página inicial, independente para cada cliente: desativado, líder ou receber convites. Vem desativada.
- O líder aceita até quatro nicks (um por linha), preservando Unicode, símbolos e espaços. A equipe comporta cinco pessoas contando o líder.
- Criação pelo painel P, confirmação com Y, convite com nome exato e clique no OK do formulário (nunca Y dentro do campo de texto).
- No máximo cinco envios por pessoa em cada execução; os contadores não são zerados por reconexão ou reconstrução. Convites alternam entre os nomes, com intervalo de 45 segundos, sem esperar aceitação bloqueando o farm.
- Recebimento exige reconhecimento do aviso de equipe e seu OK. Remetente ilegível pode ser aceito conforme solicitado; nome preferido legível e claramente diferente é ignorado. A confirmação usa o card acima das habilidades.
- Equipe existente é preservada. O painel é consultado inicialmente; grupo completo com nomes parcialmente ilegíveis também é mantido para evitar destruição por limitação de OCR.
- Após reconexão, reavalia a composição; convida faltantes. Grupo cheio de composição inconclusiva pode ser reconstruído somente pelo líder após duas leituras e com intervalo mínimo de cinco minutos. Limites de convites são preservados e uma reconstrução não se repete no mesmo episódio de reconexão.
- Localização visual do botão Dissolver em diferentes alturas; confirmação com Y. O fechamento automático do painel é respeitado.
- Observações passivas não solicitam foco. Painéis e convites usam o controle de foco existente; emergência, morte e restauração interrompem as ações. Nenhuma tecla Q é enviada pela rotina de grupo.
- Diagnóstico registra leitura do grupo, convite, tentativa, confirmação e motivo de reconstrução ou suspensão. Falha de grupo não reinicia a rota de farm.

Validação: compilação, testes automatizados das capturas fornecidas, rejeição de outros pop-ups, Unicode e limite por pessoa/reconexão. O fluxo de envio/aceite ainda requer validação ao vivo no jogo, particularmente a entrada de símbolos no cliente do jogo.

Instalação: atualização pelo canal normal. Configure um líder e os demais como membros; personagens externos precisam aceitar manualmente ou ter seu próprio bot configurado para receber.
