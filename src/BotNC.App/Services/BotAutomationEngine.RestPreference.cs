namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    // Applied only between workflows. Route/arrival/restoration readers retain
    // their temporary rest screen and their existing evidence requirements.
    private async Task ServiceRestPreferenceAsync(IReadOnlyList<ClientSession> sessions,
        PauseController pause, CancellationToken token)
    {
        if (HumanOwnsInterface || HasPendingProtection) return;
        foreach (var session in sessions)
        {
            if (!session.RestPreference.Pending || session.ReconnectPending || !session.StartupRestorationChecked ||
                session.NeedsDeathRestoration || session.HandlingDeath || session.HandlingProtection ||
                session.InAgenda || session.LoveBossInside || session.VisualCaptureFaulted ||
                session.UserInterfaceBusy || session.NextRecoveryAttemptAt != default ||
                !(session.IsFarmingTa || session.SapherasFarmConfirmed || session.InDailyCampaign) ||
                DateTime.UtcNow < session.NextRestPreferenceCheck) continue;
            session.NextRestPreferenceCheck = DateTime.UtcNow.AddSeconds(10);
            var previousInterruptible = _interruptibleAction.Value;
            _interruptibleAction.Value = session;
            try
            {
                // Settled farm must not keep asserting UI preference. In
                // particular an open Auto HUD outweighs a sparse rest match.
                var openHud = await OpenHudHuntReader.ReadAsync(recognition,
                    await CaptureClientFrameAsync(session, token), token);
                if (!session.Options.KeepRestMode && openHud != OpenHudHuntState.Unknown)
                {
                    session.RestPreference.Complete();
                    session.SafeInRest = false;
                    continue;
                }
                BindWorkflowClient(session);
                var rest = await FindRestStateAsync(session, token);
                if (!session.Options.KeepRestMode)
                {
                    var dailyConfirmed = session.InDailyCampaign && rest is not null &&
                        (await FindReferenceOnClientAsync(session, "daily_automatic", token, requireObservable: true)).Found;
                    if (RestPreferencePolicy.ShouldClose(false, rest?.ReferenceId, dailyConfirmed, openHud))
                    {
                        await ExitRestIfNeededAsync(session, pause, token);
                        session.SafeInRest = false;
                        session.RestPreference.Complete();
                        WritePersistentOnly(session, "rest_preference=off; hud=open; autoToggle=false");
                    }
                }
                else if (rest is not null)
                {
                    session.RestPreference.Complete();
                }
                else if (RestPreferencePolicy.ShouldOpen(true, null, openHud))
                {
                    session.SafeInRest = await TryOpenRestPanelAsync(session, pause, token) is not null;
                    if (session.SafeInRest) session.RestPreference.Complete();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                WritePersistentOnly(session, $"rest_preference_retry error={error.GetType().Name}; routeUnchanged=true");
            }
            finally { _interruptibleAction.Value = previousInterruptible; }
        }
    }

    private async Task ObserveDailyRestForOpenHudAsync(ClientSession session, PauseController pause, CancellationToken token)
    {
        var previousInterruptible = _interruptibleAction.Value;
        _interruptibleAction.Value = session;
        try
        {
            BindWorkflowClient(session);
            await TryOpenRestPanelAsync(session, pause, token);
        }
        finally { _interruptibleAction.Value = previousInterruptible; }
    }
}
