namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private object DiagnosticState(ClientSession session) => new
    {
        run = StatisticsSessionId, actionId = session.DiagnosticActionId,
        action = session.CurrentAction, client = session.Options.Priority,
        pid = session.Options.Target.ProcessId, hwnd = session.Options.Target.Handle.ToInt64(),
        destination = ConfiguredFarmName(session), session.IsFarmingTa, session.SafeInRest,
        auto = session.OpenHudHunt.ToString(), session.RestHudVisible,
        session.InDailyCampaign, session.DailyCycle, session.DailyCompletedCycle,
        session.LoveBossInside, session.LoveBossRewardCycle, session.InAgenda,
        session.GlobalInside, session.GlobalUntil, session.GlobalCompletedDay, session.SpecialRoutineBusy, session.LastBoostBuffAt,
        session.NeedsDeathRestoration, session.HandlingDeath, session.HandlingProtection,
        session.RestorationResourcesCleared, residualHpAlertSuppressed = session.ResidualHp.IsActive,
        session.ReconnectPending, session.ReconnectAfterLogin, session.ReconnectSkillSent,
        session.SapherasExitedEarly, session.SapherasExitHits,
        deathPending = Volatile.Read(ref session.PendingVisualDeath),
        teleportPending = Volatile.Read(ref session.PendingVisualLowHp),
        teleportInFlight = Volatile.Read(ref session.EmergencyTeleportInFlight),
        session.ConsecutiveRecoveryFailures, session.RequiresHardFlowReset,
        session.NextRecoveryAttemptAt, session.NextDailyRoutineAttemptAt,
        session.UserInterfaceBusy, physicalInputBusy = _humanInteraction?.IsBusy,
        session.FarmScheduleIndex, session.FarmScheduleRemaining, session.ScheduleHuntConfirmed,
        frameAgeMs = session.LastWindowFrameAt == default ? -1 : (DateTime.UtcNow - session.LastWindowFrameAt).TotalMilliseconds,
        session.VisualCaptureFaulted, audioHealthy = session.Audio.IsHealthy, audioArmed = session.Audio.Armed
    };

    private async Task SaveActionFailureEvidenceAsync(ClientSession session, string action, Exception error, CancellationToken token)
    {
        if (error is HumanInteractionException or ProtectionTransitionException or RecoveryObservationPendingException or ReconnectTransitionException || token.IsCancellationRequested) return;
        if (!session.HandlingDeath && Volatile.Read(ref session.PendingVisualDeath) != 0) return;
        WritePersistentOnly(session, $"action_failure action={action}; error={error.GetType().Name}; state={System.Text.Json.JsonSerializer.Serialize(DiagnosticState(session))}");
        if (DateTime.UtcNow - session.LastFailureDiagnosticAt < TimeSpan.FromSeconds(30))
        {
            WritePersistentOnly(session, "action_failure_image skipped=rate_limit_30s; state_logged=true");
            return;
        }
        session.LastFailureDiagnosticAt = DateTime.UtcNow;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var frame = await CaptureClientFrameAsync(session, timeout.Token);
            var path = await recognition.SaveDiagnosticAsync($"action_failure_client{session.Options.Priority}", frame,
                DiagnosticState(session));
            WritePersistentOnly(session, $"action_failure_evidence action={action}; diagnostic={path}; frame={frame.Width}x{frame.Height}");
        }
        catch (Exception diagnosticError)
        {
            WritePersistentOnly(session, $"action_failure_evidence_unavailable error={diagnosticError.GetType().Name}; detail={diagnosticError.Message}; originalFailurePreserved=true");
        }
    }
}
