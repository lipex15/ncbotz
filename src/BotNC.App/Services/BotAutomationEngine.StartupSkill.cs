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
            var inactiveHits = 0;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var frame = await CaptureClientFrameAsync(session, token);
                var skill = await SkillSixReader.ReadAsync(recognition, frame, token, evidence => WritePersistentOnly(session, evidence));
                inactiveHits = skill == SkillSixState.Inactive ? inactiveHits + 1 : 0;
                if (skill == SkillSixState.Active)
                {
                    session.StartupSkillChecked = true;
                    WriteLog(session, "Inicialização: skill 6 já está ativa; nenhuma tecla enviada.");
                    return;
                }
                if (inactiveHits >= 2)
                {
                    await input.PressKeyAsync(0x36, cancellationToken: token);
                    session.StartupSkillChecked = true;
                    WriteLog(session, "Inicialização: skill 6 desligada; tecla 6 enviada uma única vez.");
                    return;
                }
                if (attempt < 2) await Task.Delay(350, token);
            }
            session.StartupSkillChecked = true;
            WriteLog(session, "Skill 6 encoberta ou não reconhecida nesta inicialização; fluxo mantido sem alternar às cegas.");
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
                     !s.StartupSkillChecked && !s.ReconnectPending))
            await RunSessionActionSafelyAsync(session, "verificação inicial da skill 6",
                () => CheckStartupSkillAsync(session, pause, token), token);
    }
}
