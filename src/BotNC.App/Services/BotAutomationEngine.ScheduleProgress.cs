namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private async Task ObserveScheduleClockAsync(ClientSession session, PixelFrame frame, PauseController pause, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        if (now - session.ScheduleObservedAt < TimeSpan.FromSeconds(1)) return;
        var active = session.FarmScheduleSteps.Count > 0 && !pause.IsPaused && IsScheduleFarmActive(session) &&
                     (await recognition.FindAsync("caca_automatica", frame, token)).Found;
        AccumulateScheduleObservation(session, now, active, pause.PauseVersion);
        PublishFarmScheduleProgress(session);
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
    }
    private static bool IsScheduleFarmActive(ClientSession session) =>
        !session.FarmScheduleCompleted && session.SafeInRest && session.IsFarmingTa &&
        !session.InDailyCampaign && !session.HandlingDeath && !session.NeedsDeathRestoration &&
        !session.LoveBossInside && !session.InAgenda && session.NextRecoveryAttemptAt == default &&
        Volatile.Read(ref session.PendingVisualDeath) == 0 && Volatile.Read(ref session.PendingVisualLowHp) == 0 &&
        (WantsAbbey(session) && session.AbbeyInside && !session.AbbeyTimeExhausted ||
         WantsAnonymousDungeon(session) && session.AnonymousDungeonInside && !session.AnonymousDungeonExhausted);

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
            (IsScheduleFarmActive(session) ? "Farmando" : "Pausado — aguardando farm na masmorra"));
    }
}
