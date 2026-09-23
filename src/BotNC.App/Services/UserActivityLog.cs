using System.Text.RegularExpressions;

namespace BotNC.App.Services;

public sealed record UserActivityEntry(string Time, string Client, string Summary, string Tone);

public static class UserActivityLog
{
    public static UserActivityEntry? Describe(string message)
    {
        if (message.Contains("Proteção de HP ARMADA", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Proteção de HP DESARMADA", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("telemetria", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("amostra", StringComparison.OrdinalIgnoreCase)) return null;
        var lower = message.ToLowerInvariant();
        var client = lower.Contains("cliente 1:") ? "CLIENTE 1" : lower.Contains("cliente 2:") ? "CLIENTE 2" : "SISTEMA";
        string? summary = null;
        var tone = "#9BB5D5";
        if (lower.Contains("falha") || lower.Contains("inconclus") || lower.Contains("não foi possível") ||
            lower.Contains("atenção necessária") || lower.Contains("não confirmado") || lower.Contains("temporariamente indisponível"))
        {
            summary = lower.Contains("restaura") || lower.Contains("lápide")
                ? "Restauração precisa de atenção · combate não liberado"
                : lower.Contains("diária") ? "Diárias não confirmadas · progresso preservado"
                : lower.Contains("diretiva") ? "Diretiva não confirmada · aguardando nova leitura"
                : "Recuperando a etapa atual";
            var wait = Regex.Match(lower, @"(?:por|em) (\d+) (minutos?|segundos?|s)\b");
            if (wait.Success) summary += $" · nova tentativa em {wait.Groups[1]} {wait.Groups[2]}";
            tone = "#F0B84B";
        }
        else if (lower.Contains("morte confirmada") || lower.Contains("personagem morreu"))
        { summary = "Morte detectada · iniciando recuperação"; tone = "#FF8290"; }
        else if (lower.Contains("restauração concluída") || lower.Contains("contadores de restauração zerados"))
        { summary = "Recursos restaurados · preparando retomada"; tone = "#38D59A"; }
        else if (lower.Contains("restaurando") || lower.Contains("restauração pendente"))
            summary = "Restaurando EXP e equipamentos";
        else if (lower.Contains("checagem inicial concluída"))
            summary = "Verificação inicial de restauração concluída";
        else if (lower.Contains("diárias") && lower.Contains("concluídas"))
        { summary = "Diárias concluídas · registro salvo"; tone = "#38D59A"; }
        else if (lower.Contains("cinco diretivas concluídas") || lower.Contains("diretivas 5/5") && lower.Contains("concluídas"))
        { summary = "Diretivas concluídas · sem recarga"; tone = "#38D59A"; }
        else if (lower.Contains("diretiva aceita") || lower.Contains("diretiva já está em andamento"))
            summary = "Diretiva aceita · execução em andamento";
        else if (lower.Contains("diretiva adiada"))
        { summary = "Diretiva não confirmada · nova leitura programada"; tone = "#F0B84B"; }
        else if (lower.Contains("campanha") && (lower.Contains("andamento") || lower.Contains("retomad") || lower.Contains("iniciad")))
            summary = "Diárias em andamento · acompanhando a campanha";
        else if (lower.Contains("pausando as diárias")) summary = "Diárias pausadas · preparando Boss do Amor";
        else if (lower.Contains("farm detectado") || lower.Contains("farm confirmado") ||
                 lower.Contains("farm da t.a") && lower.Contains("confirmado") || lower.Contains("mantendo o farm") ||
                 lower.Contains("caça automática ativada e confirmada") || lower.Contains("caça recuperada no spot"))
        { summary = "Farm ativo · mantendo o ponto atual"; tone = "#38D59A"; }
        else if (lower.Contains("vitória do boss"))
        { summary = "Boss vencido · conferindo recompensas"; tone = "#38D59A"; }
        else if (lower.Contains("boss do amor:") || lower.Contains("entrada no berço")) summary = "Boss do Amor · preparando participação";
        else if (lower.Contains("recompensa") && (lower.Contains("recebida") || lower.Contains("coletada")))
        { summary = "Recompensa recebida e confirmada"; tone = "#38D59A"; }
        else if (lower.Contains("correio") && (lower.Contains("conferido") || lower.Contains("recebidas"))) summary = "Correio conferido · atividade retomada";
        else if (lower.Contains("compra diária") && lower.Contains("concluída"))
        { summary = "Compras diárias concluídas"; tone = "#38D59A"; }
        else if (lower.Contains("check-in") && lower.Contains("concluíd"))
        { summary = "Check-in e doações em ouro concluídos"; tone = "#38D59A"; }
        else if (lower.Contains("execução encerrada")) summary = "Execução encerrada";
        else if (lower.Contains("execução interrompida")) summary = "Execução interrompida · progresso salvo nas etapas confirmadas";
        else if (lower.Contains("bot pausado")) summary = "Execução pausada";
        else if (lower.Contains("bot retomado")) summary = "Execução retomada";
        else if (lower.Contains("pexbot iniciado")) summary = "Pronto · configure os clientes e clique em Iniciar";
        else if (lower.Contains("alerta de hp confirmado") || lower.Contains("hp visual crítico"))
        { summary = "HP baixo detectado · proteção acionada"; tone = "#FF8290"; }
        else if (lower.Contains("áudio seletivo caiu") || lower.Contains("áudio de hp indisponível"))
        { summary = "Áudio indisponível · proteção visual de contingência ativa"; tone = "#F0B84B"; }
        else if (lower.Contains("monitoramento contínuo")) summary = "Monitoramento ativo · clientes acompanhados";
        else if (lower.Contains("sapheras está") || lower.Contains("sapheras desativada"))
            summary = message;
        else if (lower.Contains("chegada à t.a") || lower.Contains("selecionando o primeiro favorito")) summary = "Preparando o ponto de farm";
        return summary is null ? null : new UserActivityEntry(DateTime.Now.ToString("HH:mm:ss"), client, summary, tone);
    }
}
