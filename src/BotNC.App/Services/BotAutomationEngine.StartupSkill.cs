namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private async Task CheckStartupSkillAsync(ClientSession session, PauseController pause, CancellationToken token)
    {
        if (session.StartupSkillChecked || session.ReconnectPending ||
            DateTime.UtcNow < session.NextStartupSkillCheck || HumanOwnsInterface || HasPendingProtection) return;
        session.NextStartupSkillCheck = DateTime.UtcNow.AddSeconds(30);
        BindWorkflowClient(session);
        var hadRest = await FindRestStateAsync(session, token) is not null;
        if (hadRest) await ExitRestIfNeededAsync(session, pause, token);
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var frame = await CaptureClientFrameAsync(session, token);
                var active = await recognition.FindAsync("reconnect_skill_on", frame, token);
                var inactive = await recognition.FindAsync("reconnect_skill_off", frame, token);
                WritePersistentOnly(session, $"startup_skill attempt={attempt + 1}; on={active.Found}/{active.Confidence:F3}; off={inactive.Found}/{inactive.Confidence:F3}");
                if (active.Found)
                {
                    session.StartupSkillChecked = true;
                    WriteLog(session, "Inicialização: skill 6 já está ativa; nenhuma tecla enviada.");
                    return;
                }
                if (inactive.Found)
                {
                    await input.PressKeyAsync(0x36, cancellationToken: token);
                    session.StartupSkillChecked = true;
                    WriteLog(session, "Inicialização: skill 6 desligada; tecla 6 enviada uma única vez.");
                    return;
                }
                if (attempt < 2) await Task.Delay(350, token);
            }
            WriteLog(session, "Skill 6 encoberta ou não reconhecida; fluxo mantido, nova verificação em 30 segundos, sem alternar às cegas.");
        }
        finally
        {
            if (hadRest && !session.ReconnectPending)
                session.SafeInRest = await TryOpenRestPanelAsync(session, pause, token) is not null;
        }
    }

    private async Task ServiceStartupSkillsAsync(IReadOnlyList<ClientSession> sessions, PauseController pause, CancellationToken token)
    {
        foreach (var session in sessions.Where(s => s.StartupRestorationChecked && !s.NeedsDeathRestoration &&
                     !s.StartupSkillChecked && !s.ReconnectPending && DateTime.UtcNow >= s.NextStartupSkillCheck))
            await RunSessionActionSafelyAsync(session, "verificação inicial da skill 6",
                () => CheckStartupSkillAsync(session, pause, token), token);
    }
}
