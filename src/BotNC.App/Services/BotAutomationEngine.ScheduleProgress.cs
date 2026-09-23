namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
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
        var remaining = Math.Max(0, session.FarmScheduleRemaining.TotalMinutes);
        var total = TimeSpan.FromMinutes(remaining + session.FarmScheduleSteps.Skip(session.FarmScheduleIndex + 1).Sum(item => item.Duration.TotalMinutes));
        FarmScheduleProgressChanged?.Invoke(session.Options.Label,
            session.FarmScheduleCompleted ? "Agenda concluída" :
            $"Etapa {session.FarmScheduleIndex + 1}/{session.FarmScheduleSteps.Count} · {ScheduleDestinationName(step.Destination)} · " +
            $"{Math.Ceiling(remaining):0} min restantes na etapa · Agenda restante: {FormatDuration(total)} · " +
            (IsScheduleFarmActive(session) ? "Farmando" : "Pausado — aguardando farm na masmorra"));
    }
}
