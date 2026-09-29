# PEXBOT 11.6.3

Correção pontual da restauração de equipamento:

- Dois cliques seguidos no botão fixo quando há equipamento danificado confirmado, sem esperar outra leitura de texto entre os cliques.
- Reconhecimento do zero verde corrigido para capturas com variação de um ou dois pixels nas dimensões.
- Confirmação alternativa pelo indicador inferior de equipamento e pelo texto desativado do reparo quando o contador superior não é legível.
- Painel reconhecido, mas ilegível, pode ser fechado uma vez para uma nova avaliação da lápide; o bot não presume que o reparo aconteceu.
- As verificações de EXP e equipamento continuam obrigatórias antes de retomar o farm.

Regressões incluem equipamento pendente, equipamento restaurado, tela sem painel e captura restaurada com o contador superior oculto. A tentativa 11.6.2 não foi disponibilizada porque não passou nos testes.
