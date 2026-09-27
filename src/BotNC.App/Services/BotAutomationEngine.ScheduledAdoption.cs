namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private async Task<bool> TryAdoptScheduledDungeonFarmAsync(ClientSession session, PauseController pause, CancellationToken token)
    {
        if (session.IsFarmingTa || session.FarmScheduleSteps.Count == 0 || session.FarmScheduleCompleted ||
            session.NeedsDeathRestoration || session.InDailyCampaign || session.LoveBossInside ||
            (!WantsAbbey(session) && !WantsAnonymousDungeon(session))) return false;
        await ActivateGameAsync(session, token);
        var frame = await CaptureClientFrameAsync(session, token);
        var hunting = (await recognition.FindAsync("caca_automatica", frame, token)).Found ||
            await OpenHudHuntReader.ReadAsync(recognition, frame, token) == OpenHudHuntState.Active;
        if (!hunting) return false;
        // Auto proves activity, never location. Verify the configured dungeon separately.
        var wasResting = (await recognition.FindAsync("caca_automatica", frame, token)).Found;
        await ExitRestIfNeededAsync(session, pause, token);
        var abbey = WantsAbbey(session);
        var location = abbey ? await IsAbbeyLocationVisibleAsync(token) : await IsAnonymousDungeonLocationVisibleAsync(token);
        if (!location)
        {
            await input.PressKeyAsync(KeyM, cancellationToken: token);
            location = await WaitForReferenceToAppearAsync(abbey ? "mapa_abadia" : "anonymous_map_heading",
                TimeSpan.FromSeconds(6), pause, token);
            // Close only the map opened by this observation; never select a new spot.
            if (location || await IsMapOpenAsync(session, token))
                await input.PressKeyAsync(KeyM, cancellationToken: token);
        }
        if (!location) return false;
        session.AbbeyInside = abbey;
        session.AnonymousDungeonInside = !abbey;
        if (abbey) session.AbbeyActiveSinceUtc = DateTime.UtcNow;
        if ((abbey ? session.Options.AbbeyCustomFarmCoordinate : session.Options.AnonymousDungeonCustomFarmCoordinate) is not null)
        {
            // Agenda uses the configured destination even with individual farming disabled.
            if (abbey) await TravelToAbbeySpotAsync(session, pause, token);
            else await TravelToAnonymousDungeonSpotAsync(session, pause, token);
            await SaveFarmScheduleStateAsync(session);
            return true;
        }
        session.IsFarmingTa = true;
        session.SafeInRest = false;
        session.ScheduleHuntConfirmed = true;
        session.ScheduleConfirmedStep = session.FarmScheduleIndex;
        session.Audio.Armed = session.Options.EnableAntiOverkill;
        if (abbey) session.AbbeyActiveSinceUtc = DateTime.UtcNow;
        if (wasResting)
        {
            await TryOpenRestPanelAsync(session, pause, token);
            session.SafeInRest = true;
        }
        await SaveFarmScheduleStateAsync(session);
        WriteLog(session, $"Farm já ativo na masmorra da Agenda confirmado; mantendo o ponto e descontando do saldo de {FormatDuration(session.FarmScheduleRemaining)}.");
        return true;
    }
}
