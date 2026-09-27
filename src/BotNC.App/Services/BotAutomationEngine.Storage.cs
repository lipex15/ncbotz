using System.Globalization;
using BotNC.App.Models;

namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    internal static bool AutoStorageDue(bool enabled, int minutes, DateTimeOffset now, DateTimeOffset? last) =>
        enabled && minutes is >= 1 and <= 10080 && (last is null || now - last.Value >= TimeSpan.FromMinutes(minutes));

    internal static async Task<bool> IsStorageCityAsync(VisualRecognitionService visual, PixelFrame frame, CancellationToken token)
    {
        // Artigos alone also occurs in T.A. Require the city's two distinct services.
        return (await visual.FindAsync("storage_city_npc", frame, token)).Found &&
               (await visual.FindAsync("storage_city_weapons", frame, token)).Found;
    }

    private async Task<bool> TryStoreItemsInCityAsync(ClientSession session, PauseController pause, CancellationToken token)
    {
        if (!AutoStorageDue(session.Options.EnableAutoStorage, session.Options.AutoStorageIntervalMinutes,
                DateTimeOffset.UtcNow, session.LastAutoStorageAt) || DateTime.UtcNow < session.NextAutoStorageRetryAt ||
            session.ReconnectPending || session.HandlingDeath || session.NeedsDeathRestoration ||
            session.InAgenda || session.InDailyCampaign || session.LoveBossInside ||
            session.SapherasFarmConfirmed && !session.SapherasExitedEarly || HasPendingProtection || HumanOwnsInterface)
            return false;

        BindWorkflowClient(session);
        var frame = await CaptureClientFrameAsync(session, token);
        if (!await IsStorageCityAsync(recognition, frame, token)) return false;

        // No teleport and no interruption of an active dungeon: only a city already visible.
        session.NextAutoStorageRetryAt = DateTime.UtcNow.AddMinutes(5);
        session.SafeInRest = false;
        session.IsFarmingTa = false;
        session.ScheduleHuntConfirmed = false;
        session.SchedulePreviousObservationActive = false;
        SetStatus(BotRunState.Running, $"{session.Options.Label}: guardando itens", "Visitando o Depósito na cidade");
        await AbortWorkflowIfDeathDetectedAsync(session, "antes do Depósito", token);
        var destination = gameWindows.MapReferencePoint(session.Options.Target, 148, 222);
        await input.ClickAsync(destination.X, destination.Y, token);
        try
        {
            await WaitForReferenceAsync("storage_open", "Vigia do Depósito", TimeSpan.FromSeconds(60), pause, token);
            // The panel identifies the context; a missing button match never clicks another menu.
            await WaitForReferenceAsync("storage_auto", "Armazenar Autom.", TimeSpan.FromSeconds(8), pause, token);
            var store = gameWindows.MapReferencePoint(session.Options.Target, 269, 1002);
            // Reserve before input: restart/failure must not duplicate this visit.
            session.LastAutoStorageAt = DateTimeOffset.UtcNow;
            await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.storage.lastAction",
                session.LastAutoStorageAt.Value.ToString("O", CultureInfo.InvariantCulture));
            await input.ClickAsync(store.X, store.Y, token);
            WriteLog(session, "Armazenar Autom. acionado uma vez. São usadas as regras do próprio jogo; sem venda ou descarte.");
            await input.PressKeyAsync(KeyEscape, cancellationToken: token);
            await WaitForReferenceToDisappearAsync("storage_open", TimeSpan.FromSeconds(8), pause, token);
            WriteLog(session, $"Depósito fechado; retomando o fluxo. Nova visita após {session.Options.AutoStorageIntervalMinutes} minutos, quando voltar à cidade.");
            return true;
        }
        catch
        {
            // Leave death/teleport/disconnect precedence intact. The normal recovery
            // owns any remaining panel; no unconditional Esc in a changed context.
            WritePersistentOnly(session, "storage_visit_incomplete; repeatCooldownMinutes=5; noAutomaticTeleport=true");
            throw;
        }
    }

    internal static void VerifyAutoStoragePolicy()
    {
        var now = DateTimeOffset.UtcNow;
        if (AutoStorageDue(false, 120, now, null) || AutoStorageDue(true, 0, now, null) ||
            AutoStorageDue(true, 120, now, now.AddMinutes(-119)) ||
            !AutoStorageDue(true, 120, now, now.AddMinutes(-120)) || !AutoStorageDue(true, 120, now, null))
            throw new InvalidOperationException("Intervalo ou opção desativada do Depósito não foi respeitado.");
    }
}
