namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private async Task ObserveScheduleClockAsync(ClientSession session, PixelFrame frame, PauseController pause, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        if (now - session.ScheduleObservedAt < TimeSpan.FromSeconds(1)) return;
        var eligible = session.FarmScheduleSteps.Count > 0 && !pause.IsPaused && !session.ReconnectPending &&
                       !session.VisualCaptureFaulted && IsScheduleFarmActive(session);
        var reading = (await recognition.FindAsync("caca_automatica", frame, token)).Found
            ? OpenHudHuntState.Active : session.OpenHudHunt;
        var active = ResolveScheduleHuntObservation(session, now, eligible, reading, pause.PauseVersion);
        AccumulateScheduleObservation(session, now, active, pause.PauseVersion);
        PublishFarmScheduleProgress(session);
    }

    private static bool ResolveScheduleHuntObservation(ClientSession session, DateTime now,
        bool eligible, OpenHudHuntState reading, long pauseVersion)
    {
        // An occluded HUD is not a stopped hunt. Keep the last confirmed state
        // only within the same uninterrupted, observable dungeon farming context.
        if (!eligible || session.ScheduleConfirmedStep != session.FarmScheduleIndex ||
            session.FarmSchedulePauseVersion != pauseVersion ||
            now - session.ScheduleObservedAt >= TimeSpan.FromSeconds(10))
        {
            session.ScheduleHuntConfirmed = false;
            session.SchedulePreviousObservationActive = false;
        }
        session.ScheduleConfirmedStep = session.FarmScheduleIndex;
        if (!eligible) return false;
        if (reading == OpenHudHuntState.Active) session.ScheduleHuntConfirmed = true;
        else if (reading == OpenHudHuntState.Inactive) session.ScheduleHuntConfirmed = false;
        return session.ScheduleHuntConfirmed;
    }

    private static void AccumulateScheduleObservation(ClientSession session, DateTime now, bool active, long pauseVersion)
    {
        var elapsed = now - session.ScheduleObservedAt;
        if (active && session.SchedulePreviousObservationActive && session.FarmSchedulePauseVersion == pauseVersion &&
            elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromSeconds(10))
            Interlocked.Add(ref session.ObservedScheduleTicks, elapsed.Ticks);
        session.SchedulePreviousObservationActive = active;
        session.FarmSchedulePauseVersion = pauseVersion;
        session.ScheduleObservedAt = now;
    }

    internal static void VerifyScheduleClockPolicy()
    {
        var options = new BotNC.App.Models.AutomationClientOptions("Cliente 1",
            new BotNC.App.Models.GameWindowTarget(0, "Teste", 1, false, true),
            BotNC.App.Models.TaDestination.Ta1Codex, false, 1, null);
        var first = new ClientSession(options);
        var ta = new ClientSession(options) { IsFarmingTa = true,
            FarmScheduleSteps = [new(BotNC.App.Models.FarmScheduleDestination.Ta1, TimeSpan.FromMinutes(30))] };
        if (!IsScheduleFarmActive(ta)) throw new InvalidOperationException("Agenda T.A 1 não contabiliza farm.");
        ta.GlobalInside = true;
        if (IsScheduleFarmActive(ta)) throw new InvalidOperationException("Global descontou agenda da T.A 1.");
        ta.GlobalInside = false;
        ta.SpecialRoutineBusy = true;
        if (IsScheduleFarmActive(ta)) throw new InvalidOperationException("Buff descontou agenda da T.A 1.");
        var second = new ClientSession(options with { Label = "Cliente 2", Priority = 2 });
        var now = DateTime.UtcNow;
        for (var secondIndex = 0; secondIndex <= 60; secondIndex++)
        {
            AccumulateScheduleObservation(first, now.AddSeconds(secondIndex), true, 0);
            AccumulateScheduleObservation(second, now.AddSeconds(secondIndex), false, 0);
        }
        if (TimeSpan.FromTicks(first.ObservedScheduleTicks).TotalSeconds != 60 || second.ObservedScheduleTicks != 0)
            throw new InvalidOperationException("Agenda perdeu tempo de farm ou misturou os clientes.");
        AccumulateScheduleObservation(first, now.AddSeconds(61), false, 0);
        AccumulateScheduleObservation(first, now.AddSeconds(62), true, 0);
        AccumulateScheduleObservation(first, now.AddSeconds(63), true, 1);
        AccumulateScheduleObservation(first, now.AddMinutes(5), true, 1);
        if (TimeSpan.FromTicks(first.ObservedScheduleTicks).TotalSeconds != 60)
            throw new InvalidOperationException("Agenda contou pausa, saída ou intervalo sem observação.");

        var hidden = new ClientSession(options);
        for (var tick = 0; tick <= 300; tick++)
        {
            var at = now.AddSeconds(tick);
            var active = ResolveScheduleHuntObservation(hidden, at, true,
                tick == 0 ? OpenHudHuntState.Active : OpenHudHuntState.Unknown, 0);
            AccumulateScheduleObservation(hidden, at, active, 0);
        }
        if (TimeSpan.FromTicks(hidden.ObservedScheduleTicks).TotalSeconds != 300)
            throw new InvalidOperationException("Auto encoberto interrompeu cinco minutos de farm confirmado.");
        if (ResolveScheduleHuntObservation(hidden, now.AddSeconds(301), true, OpenHudHuntState.Inactive, 0) ||
            ResolveScheduleHuntObservation(hidden, now.AddSeconds(302), true, OpenHudHuntState.Unknown, 0) ||
            ResolveScheduleHuntObservation(second, now, true, OpenHudHuntState.Unknown, 0))
            throw new InvalidOperationException("Agenda presumiu farm sem confirmação ou após Auto desligado.");
        foreach (var interruption in new[] { "exit", "pause", "capture", "step" })
        {
            var sample = new ClientSession(options) { ScheduleObservedAt = now };
            ResolveScheduleHuntObservation(sample, now, true, OpenHudHuntState.Active, 0);
            if (interruption == "step") sample.FarmScheduleIndex++;
            if (ResolveScheduleHuntObservation(sample,
                    interruption == "capture" ? now.AddSeconds(20) : now.AddSeconds(1),
                    interruption != "exit", OpenHudHuntState.Unknown, interruption == "pause" ? 1 : 0))
                throw new InvalidOperationException($"Agenda preservou farm após interrupção: {interruption}.");
        }
    }
    private static bool IsScheduleFarmActive(ClientSession session) =>
        !session.FarmScheduleCompleted && session.IsFarmingTa &&
        !session.InDailyCampaign && !session.HandlingDeath && !session.NeedsDeathRestoration &&
        !session.LoveBossInside && !session.GlobalInside && !session.SpecialRoutineBusy && !session.InAgenda && session.NextRecoveryAttemptAt == default &&
        Volatile.Read(ref session.PendingVisualDeath) == 0 && Volatile.Read(ref session.PendingVisualLowHp) == 0 &&
        (WantsAbbey(session) && session.AbbeyInside && !session.AbbeyTimeExhausted ||
         WantsAnonymousDungeon(session) && session.AnonymousDungeonInside && !session.AnonymousDungeonExhausted ||
         CurrentFarmScheduleStep(session)?.Destination == BotNC.App.Models.FarmScheduleDestination.Ta1);

    private void PublishFarmScheduleProgress(ClientSession session)
    {
        if (session.FarmScheduleSteps.Count == 0) return;
        var step = session.FarmScheduleSteps[session.FarmScheduleIndex];
        var remaining = Math.Max(0, (session.FarmScheduleRemaining - TimeSpan.FromTicks(Interlocked.Read(ref session.ObservedScheduleTicks))).TotalMinutes);
        var total = TimeSpan.FromMinutes(remaining + session.FarmScheduleSteps.Skip(session.FarmScheduleIndex + 1).Sum(item => item.Duration.TotalMinutes));
        FarmScheduleProgressChanged?.Invoke(session.Options.Label,
            session.FarmScheduleCompleted ? "Agenda concluída" :
            $"Etapa {session.FarmScheduleIndex + 1}/{session.FarmScheduleSteps.Count} · {ScheduleDestinationName(step.Destination)} · " +
            $"{Math.Ceiling(remaining):0} min restantes na etapa · Agenda restante: {FormatDuration(total)} · " +
            (IsScheduleFarmActive(session) && session.SchedulePreviousObservationActive ? "Farmando" : "Pausado — aguardando farm na masmorra"));
    }
}
