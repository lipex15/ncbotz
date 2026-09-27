# 0.10.45 — limpeza de diagnósticos e skill 6

- A skill 6 é verificada uma vez por execução de cada cliente. Depois de ativada (ou de uma tentativa inconclusiva), não fica reabrindo descanso nem repetindo a checagem. Uma nova verificação só é armada quando uma desconexão confirmada inicia o fluxo de reconexão.
- Diagnósticos antigos do diretório local do bot são removidos automaticamente a cada inicialização, no máximo uma vez a cada 12 horas: arquivos `.png` e `.json` com mais de 3 dias e, como limite de segurança, os mais antigos quando o conjunto passar de 512 MB. Configurações, referências de reconhecimento e a pasta externa de Downloads não são tocadas.
