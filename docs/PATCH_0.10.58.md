# 0.10.58

- Corrigido bloqueio da reconexão de uma conta por marca antiga de interação manual em outra janela indisponível.
- Uma interface manual lembrada só bloqueia ações enquanto sua janela válida estiver em primeiro plano; interação física recente continua protegida.
- Falhas de captura limpam a marca antiga, sem assumir outra conta ou outro processo automaticamente.
- Testes de isolamento entre clientes e regressão do fluxo de login.
- Alternativa por leitura do aviso explícito de inatividade e botão OK(Y), validada com a captura reduzida recebida; botão ou texto isolados não autorizam confirmação.
