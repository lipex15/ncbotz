using System.Globalization;

namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    internal static readonly TimeSpan GuildTreasureInterval = TimeSpan.FromHours(17);

    internal static bool GuildTreasureDue(DateTimeOffset now, DateTimeOffset? lastVisit) =>
        lastVisit is null || now - lastVisit.Value >= GuildTreasureInterval;

    internal static bool GuildTreasureCollectionProgressed(int before, int? after) =>
        before > 0 && after is >= 0 && after < before;

    private async Task RecordGuildTreasureVisitAsync(ClientSession session, bool confirmedEmpty)
    {
        session.LastGuildTreasureVisit = DateTimeOffset.UtcNow;
        await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.routines.guildTreasure.lastVisit",
            session.LastGuildTreasureVisit.Value.ToString("O", CultureInfo.InvariantCulture));
        if (confirmedEmpty)
            await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.routines.guildTreasure.lastEmpty",
                session.LastGuildTreasureVisit.Value.ToString("O", CultureInfo.InvariantCulture));
    }

    private async Task<GuildTreasureReading> ReadGuildTreasureStableAsync(ClientSession session,
        PauseController pause, CancellationToken token)
    {
        GuildTreasureReading? previous = null;
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, token);
            ThrowIfDeathPending(session);
            var reading = await GuildTreasureReader.ReadAsync(recognition, await CaptureClientFrameAsync(session, token), token);
            WritePersistentOnly(session, $"guild_treasure state={reading.State}; {reading.Evidence}");
            if (reading.State != GuildTreasureState.Unknown && previous?.State == reading.State && previous.Count == reading.Count)
                return reading;
            previous = reading;
            await Task.Delay(200, token);
        }
        throw new TimeoutException("Baú da Guilda ilegível; nenhum clique sem confirmação.");
    }

    private async Task CollectVisibleGuildTreasureAsync(ClientSession session, PauseController pause, CancellationToken token)
    {
        if (session.GuildTreasureCheckedThisVisit) return;
        var frame = await CaptureClientFrameAsync(session, token);
        if (!(await recognition.FindAsync("guild_treasure_panel", frame, token)).Found) return;
        session.GuildTreasureCheckedThisVisit = true;
        await RecordGuildTreasureVisitAsync(session, false);
        try
        {
            var deadline = DateTime.UtcNow.AddMinutes(3);
            var collected = 0;
            int? awaitingDecreaseFrom = null;
            while (DateTime.UtcNow < deadline)
            {
                var reading = await ReadGuildTreasureStableAsync(session, pause, token);
                if (reading.State == GuildTreasureState.Reward)
                {
                    await EnsureGameForegroundAsync(session, token);
                    var point = gameWindows.MapReferencePoint(session.Options.Target, 957, 440);
                    await input.MoveAndClickAsync(point.X, point.Y, TimeSpan.FromMilliseconds(300), token,
                        cooldown: TimeSpan.FromMilliseconds(200));
                    await WaitForReferenceToDisappearAsync("guild_treasure_reward", TimeSpan.FromSeconds(8), pause, token);
                    continue;
                }
                if (awaitingDecreaseFrom is { } before)
                {
                    if (!GuildTreasureCollectionProgressed(before, reading.Count))
                        throw new InvalidOperationException("A quantidade do baú não diminuiu após a coleta; sem repetir Abrir.");
                    awaitingDecreaseFrom = null;
                    collected++;
                    await RecordStatisticAsync(session, "reward", "Baú da Guilda coletado");
                    WriteLog(session, $"Baú da Guilda: recompensa coletada; {reading.Count} restante(s).");
                }
                if (reading.State == GuildTreasureState.Empty)
                {
                    await RecordGuildTreasureVisitAsync(session, true);
                    WriteLog(session, $"Baú da Guilda conferido: vazio; {collected} coleta(s). Próxima verificação em 17 horas ou na próxima visita à Guilda.");
                    return;
                }
                if (reading.State != GuildTreasureState.Available) throw new InvalidOperationException("Estado do baú não confirmado.");
                awaitingDecreaseFrom = reading.Count;
                await EnsureGameForegroundAsync(session, token);
                var open = gameWindows.MapReferencePoint(session.Options.Target, 1627, 942);
                await input.MoveAndClickAsync(open.X, open.Y, TimeSpan.FromMilliseconds(320), token,
                    cooldown: TimeSpan.FromMilliseconds(200));
                await WaitForReferenceAsync("guild_treasure_reward", "Item Obtido do baú da Guilda",
                    TimeSpan.FromSeconds(10), pause, token);
            }
            throw new TimeoutException("Coleta de baús ultrapassou o limite desta visita; sem cliques adicionais.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            ThrowIfDeathPending(session);
            WritePersistentOnly(session, exception.ToString());
            WriteLog(session, $"Baú da Guilda não confirmado: {exception.Message} Sem reabrir a Guilda fora do intervalo; nova oportunidade na próxima visita.");
            // Safely dismiss a known reward before continuing the original Guild routine.
            if ((await recognition.FindAsync("guild_treasure_reward", token)).Found)
            {
                var point = gameWindows.MapReferencePoint(session.Options.Target, 957, 440);
                await input.MoveAndClickAsync(point.X, point.Y, TimeSpan.FromMilliseconds(300), token);
                await WaitForReferenceToDisappearAsync("guild_treasure_reward", TimeSpan.FromSeconds(8), pause, token);
            }
        }
    }

    private async Task<bool> TryCollectDueGuildTreasureAsync(ClientSession session, PauseController pause, CancellationToken token)
    {
        if (!GuildTreasureDue(DateTimeOffset.UtcNow, session.LastGuildTreasureVisit) ||
            session.InAgenda || session.HandlingDeath || session.NeedsDeathRestoration || !session.StartupRestorationChecked ||
            session.InDailyCampaign || session.LoveBossInside || session.NextRecoveryAttemptAt != default ||
            Volatile.Read(ref session.PendingVisualDeath) != 0 || Volatile.Read(ref session.PendingVisualLowHp) != 0 || session.Audio.HasPendingAlert)
            return false;
        BindWorkflowClient(session);
        var resumeRest = session.SafeInRest;
        session.GuildTreasureCheckedThisVisit = false;
        try
        {
            await ActivateGameAsync(session, token);
            resumeRest |= await FindRestStateAsync(session, token) is not null;
            await ExitRestIfNeededAsync(session, pause, token);
            await AbortWorkflowIfDeathDetectedAsync(session, "antes do baú da Guilda", token);
            // Reserve this scheduled opening durably, even if recognition later fails.
            // Do not reopen repeatedly after a stop/start or a failed frame.
            await RecordGuildTreasureVisitAsync(session, false);
            if (!(await recognition.FindAsync("guild_treasure_panel", token)).Found)
            {
                if (!(await recognition.FindAsync("menu_guild", token)).Found)
                {
                    await input.PressKeyAsync(KeyEquals, cancellationToken: token);
                    await WaitForSafeMenuNavigationAsync("menu_guild", "menu da Guilda", TimeSpan.FromSeconds(8), pause, token);
                }
                var guild = gameWindows.MapReferencePoint(session.Options.Target, 1600, 340);
                await input.MoveAndClickAsync(guild.X, guild.Y, TimeSpan.FromMilliseconds(300), token);
                await WaitForReferenceAsync("guild_page", "Guilda", TimeSpan.FromSeconds(12), pause, token);
            }
            if (!(await recognition.FindAsync("guild_treasure_panel", token)).Found)
            {
                var info = gameWindows.MapReferencePoint(session.Options.Target, 135, 138);
                await input.ClickAsync(info.X, info.Y, token);
                await WaitForReferenceAsync("guild_treasure_panel", "Baú do Tesouro", TimeSpan.FromSeconds(8), pause, token);
            }
            await CollectVisibleGuildTreasureAsync(session, pause, token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            RecoverSessionAfterActionFailure(session, exception, "verificação do baú da Guilda");
        }
        finally
        {
            if (!token.IsCancellationRequested && Volatile.Read(ref session.PendingVisualDeath) == 0)
            {
                try
                {
                    if ((await recognition.FindAsync("guild_page", token)).Found ||
                        (await recognition.FindAsync("guild_treasure_panel", token)).Found)
                        await CloseGuildScreenAsync(session, pause, token);
                    if ((await recognition.FindAsync("guild_treasure_panel", token)).Found)
                        throw new TimeoutException("Guilda continua aberta após conferir o baú; descanso não será presumido.");
                    if (resumeRest && !session.InitialPreparationActive && session.NextRecoveryAttemptAt == default)
                        session.SafeInRest = await TryOpenRestPanelAsync(session, pause, token) is not null;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { RecoverSessionAfterActionFailure(session, exception, "fechamento do baú da Guilda"); }
            }
            session.GuildTreasureCheckedThisVisit = false;
        }
        return true;
    }
}
