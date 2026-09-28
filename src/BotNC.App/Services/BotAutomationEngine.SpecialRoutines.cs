using System.Globalization;
using BotNC.App.Models;

namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private static bool GlobalHasPriority(ClientSession session) =>
        session.Options.GlobalDungeon?.Enabled == true && SpecialRoutinePolicy.GlobalOpen(DateTimeOffset.UtcNow) &&
        session.GlobalCompletedDay != SpecialRoutinePolicy.Day(DateTimeOffset.UtcNow) &&
        (session.GlobalUntil is null || session.GlobalUntil > DateTimeOffset.UtcNow);

    private async Task LoadSpecialRuntimeAsync(ClientSession session)
    {
        var prefix = SessionSettingPrefix(session);
        if (DateTimeOffset.TryParse(await database.GetSettingAsync($"{prefix}.boost.confirmedAt"), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var last)) session.LastBoostBuffAt = last;
        session.GlobalCompletedDay = await database.GetSettingAsync($"{prefix}.global.completedDay");
        session.GlobalRunDay = await database.GetSettingAsync($"{prefix}.global.runDay");
        if (session.GlobalRunDay == SpecialRoutinePolicy.Day(DateTimeOffset.UtcNow) &&
            DateTimeOffset.TryParse(await database.GetSettingAsync($"{prefix}.global.until"), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var until)) session.GlobalUntil = until;
    }

    private async Task ServiceSpecialRoutinesAsync(IReadOnlyList<ClientSession> sessions, SapherasOptions sapheras, PauseController pause, CancellationToken token)
    {
        if (HumanOwnsInterface || HasPendingProtection) return;
        foreach (var session in sessions)
        {
            if (session.ReconnectPending || session.HandlingDeath || session.NeedsDeathRestoration ||
                !session.StartupRestorationChecked || session.HandlingProtection || session.InAgenda ||
                session.VisualCaptureFaulted || session.UserInterfaceBusy || session.NextRecoveryAttemptAt != default) continue;
            var now = DateTimeOffset.UtcNow;
            if (session.GlobalRunDay != SpecialRoutinePolicy.Day(now))
            {
                session.GlobalUntil = null;
                session.GlobalRunDay = SpecialRoutinePolicy.Day(now);
            }
            var buffDue = session.Options.EnableBoostBuff && SpecialRoutinePolicy.BuffDue(now, session.LastBoostBuffAt) &&
                DateTime.UtcNow >= session.NextBoostAttemptAt;
            var globalDue = GlobalHasPriority(session) && (!session.GlobalInside || !session.IsFarmingTa) &&
                DateTime.UtcNow >= session.NextGlobalAttemptAt;
            var globalEnded = session.Options.GlobalDungeon?.Enabled == true &&
                session.GlobalCompletedDay != SpecialRoutinePolicy.Day(now) &&
                (session.GlobalInside && !GlobalHasPriority(session) || session.GlobalUntil is { } end && now >= end);
            if (!buffDue && !globalDue && !globalEnded) continue;

            var previous = _interruptibleAction.Value;
            _interruptibleAction.Value = session;
            session.SpecialRoutineBusy = true;
            try
            {
                BindWorkflowClient(session);
                await ActivateGameAsync(session, token);
                if (buffDue)
                {
                    var resumeSapheras = session.SapherasFarmConfirmed && !session.SapherasExitedEarly;
                    session.NextBoostAttemptAt = DateTime.UtcNow.AddMinutes(10);
                    await RenewBoostBuffAsync(session, pause, token);
                    if (resumeSapheras && !GlobalHasPriority(session) && DateTime.Now < sapheras.ScheduledAt + sapheras.Duration)
                    {
                        session.SapherasExitedEarly = false;
                        await EnterSapherasAsync(session, sapheras, pause, token);
                    }
                    else await EnterConfiguredFarmAsync(session, pause, token, false);
                }
                else if (globalEnded)
                {
                    if (session.GlobalInside) await ReturnToSpecialCityAsync(session, pause, token);
                    session.GlobalInside = false;
                    session.GlobalCompletedDay = SpecialRoutinePolicy.Day(now);
                    await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.global.completedDay", session.GlobalCompletedDay);
                    WriteLog(session, "Global encerrada; retomando o farm e liberando as diárias pendentes.");
                    await EnterConfiguredFarmAsync(session, pause, token, false);
                }
                else
                {
                    session.NextGlobalAttemptAt = DateTime.UtcNow.AddMinutes(2);
                    await EnterGlobalAndFarmAsync(session, pause, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (HumanInteractionException) { }
            catch (Exception error)
            {
                WriteLog(session, $"Rotina {(buffDue ? "Buff Boost" : "Global")} não concluída: {error.Message}. Mantendo proteção e retomada do farm.");
                session.NextRecoveryAttemptAt = DateTime.UtcNow.AddSeconds(5);
                session.IsFarmingTa = false;
                session.Audio.Armed = true;
            }
            finally { session.SpecialRoutineBusy = false; _interruptibleAction.Value = previous; }
        }
    }

    private async Task SpecialClickAsync(ClientSession session, int x, int y, CancellationToken token)
    {
        await EnsureGameForegroundAsync(session, token);
        var point = gameWindows.MapReferencePoint(session.Options.Target, x, y);
        await input.MoveAndClickAsync(point.X, point.Y, TimeSpan.FromMilliseconds(200), token);
    }

    private async Task ReturnToSpecialCityAsync(ClientSession session, PauseController pause, CancellationToken token)
    {
        await ExitRestIfNeededAsync(session, pause, token);
        // Close only a recognized map, never blindly confirm an unknown popup.
        if ((await recognition.FindAsync("global_map", token)).Found || await IsMapOpenAsync(session, token))
            await input.PressKeyAsync(KeyEscape, cancellationToken: token);
        if (!await IsSpecialCityAsync(session, token))
        {
            await input.PressKeyAsync(Key7, cancellationToken: token);
            var deadline = DateTime.UtcNow.AddSeconds(35);
            while (DateTime.UtcNow < deadline)
            {
                await CheckpointAsync(pause, token);
                await AbortWorkflowIfDeathDetectedAsync(session, "retorno para rotina de cidade", token);
                var frame = await CaptureClientFrameAsync(session, token);
                if (await IsStorageCityAsync(recognition, frame, token) || (await recognition.FindAsync("boost_npc", frame, token)).Found) break;
                if ((await recognition.FindAsync("anonymous_exit_confirmation", frame, token)).Found)
                    await input.PressKeyAsync(KeyY, cancellationToken: token);
                await Task.Delay(500, token);
            }
        }
        if (!await IsSpecialCityAsync(session, token))
            throw new InvalidOperationException("Cidade não confirmada; nenhum clique no NPC enviado.");
        await RecordAbbeyExitAsync(session);
        await RecordAnonymousDungeonExitAsync(session);
        session.GlobalInside = false;
        session.NextGlobalAttemptAt = default;
        session.SapherasFarmConfirmed = false;
        session.SapherasExitedEarly = true;
        session.InDailyCampaign = false; // persisted pending missions are resumed by the daily flow
        session.DailyNeedsTeleport = false;
        session.LoveBossInside = false;
        session.IsFarmingTa = false;
        session.AwaitingHuntActivationAtSpot = false;
        session.ScheduleHuntConfirmed = false;
        session.SchedulePreviousObservationActive = false;
    }

    private async Task<bool> IsSpecialCityAsync(ClientSession session, CancellationToken token)
    {
        var frame = await CaptureClientFrameAsync(session, token);
        return await IsStorageCityAsync(recognition, frame, token) || (await recognition.FindAsync("boost_npc", frame, token)).Found;
    }

    private async Task RenewBoostBuffAsync(ClientSession session, PauseController pause, CancellationToken token)
    {
        SetStatus(BotRunState.Running, $"{session.Options.Label}: buff Boost", "Renovando gratuitamente no Patrocinador");
        await ReturnToSpecialCityAsync(session, pause, token);
        RecognitionResult? npc = null;
        for (var scroll = 0; scroll < 8; scroll++)
        {
            await CheckpointAsync(pause, token);
            var found = await recognition.FindAsync("boost_npc", await CaptureClientFrameAsync(session, token), token);
            if (found.Found) { npc = found; break; }
            // Drag the list content upwards to reach its final NPC rows.
            var start = gameWindows.MapReferencePoint(session.Options.Target, 180, 424);
            var end = gameWindows.MapReferencePoint(session.Options.Target, 180, 150);
            await input.DragAsync(start.X, start.Y, end.X, end.Y, token);
            await Task.Delay(450, token);
        }
        if (npc is null) throw new InvalidOperationException("Patrocinador não reconhecido na lista; confirme servidor Boost.");
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var current = await recognition.FindAsync("boost_npc", await CaptureClientFrameAsync(session, token), token);
            if (!current.Found) throw new InvalidOperationException("Lista do Patrocinador deixou de estar visível.");
            await SpecialClickAsync(session, current.X, current.Y, token);
            // The click starts NPC pathing. Never call it success from the click alone.
            await Task.Delay(2500, token);
            var deadline = DateTime.UtcNow.AddSeconds(45);
            var consecutive = 0;
            while (DateTime.UtcNow < deadline)
            {
                await CheckpointAsync(pause, token);
                await AbortWorkflowIfDeathDetectedAsync(session, "visita ao Patrocinador", token);
                var present = (await recognition.FindAsync("boost_buff", await CaptureClientFrameAsync(session, token), token)).Found;
                consecutive = present ? consecutive + 1 : 0;
                if (consecutive >= 2)
                {
                    session.LastBoostBuffAt = DateTimeOffset.UtcNow;
                    await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.boost.confirmedAt", session.LastBoostBuffAt.Value.ToString("O", CultureInfo.InvariantCulture));
                    WriteLog(session, $"Buff Boost confirmado; próxima renovação em 24h. Acionamento {attempt}/3.");
                    return;
                }
                await Task.Delay(700, token);
            }
        }
        throw new InvalidOperationException("Buff não confirmado após três acionamentos gratuitos; horário não atualizado.");
    }

    private async Task<bool> GlobalLocationAsync(ClientSession session, CancellationToken token)
    {
        var frame = await CaptureClientFrameAsync(session, token);
        foreach (var id in new[] { "global_map", "global_spawn_north", "global_spawn_south" })
            if ((await recognition.FindAsync(id, frame, token)).Found) return true;
        return false;
    }

    private async Task EnterGlobalAndFarmAsync(ClientSession session, PauseController pause, CancellationToken token)
    {
        if (!GlobalHasPriority(session)) return;
        var options = session.Options.GlobalDungeon!;
        session.AwaitingHuntActivationAtSpot = false;
        var coordinate = options.Coordinate ?? throw new InvalidOperationException("Global sem coordenada personalizada.");
        await ActivateGameAsync(session, token);
        await ExitRestIfNeededAsync(session, pause, token);
        // Opening M proves the dungeon even when the character is past either spawn.
        var inside = await GlobalLocationAsync(session, token);
        if (!inside)
        {
            await input.PressKeyAsync(KeyM, cancellationToken: token);
            inside = await WaitForReferenceOnClientAsync(session, "global_map", TimeSpan.FromSeconds(3), pause, token);
            if (!inside) await input.PressKeyAsync(KeyEscape, cancellationToken: token);
        }
        if (!inside)
        {
            await OpenDungeonMenuAsync(session, pause, token);
            await SpecialClickAsync(session, 1744, 273, token);
            await WaitForReferenceAsync("tela_masmorras", "página Masmorra", TimeSpan.FromSeconds(15), pause, token);
            await SpecialClickAsync(session, 532, 143, token);
            await WaitForReferenceAsync("global_page", "Grande Deserto Candellium", TimeSpan.FromSeconds(10), pause, token);
            await WaitForReferenceAsync("global_enter", "Entrar Global disponível", TimeSpan.FromSeconds(6), pause, token);
            if (!await GlobalEntryReader.ReadyAsync(recognition, await CaptureClientFrameAsync(session, token), token))
                throw new InvalidOperationException("Entrar Global está apagado; nenhuma tentativa de confirmação enviada.");
            if (!GlobalHasPriority(session)) throw new InvalidOperationException("Janela da Global terminou antes da entrada.");
            await SpecialClickAsync(session, 1781, 989, token);
            await WaitForReferenceAsync("global_confirm", "confirmação de Candellium", TimeSpan.FromSeconds(8), pause, token);
            await input.PressKeyAsync(KeyY, cancellationToken: token);
            var deadline = DateTime.UtcNow.AddSeconds(55);
            while (!await GlobalLocationAsync(session, token) && DateTime.UtcNow < deadline)
            {
                await CheckpointAsync(pause, token);
                await Task.Delay(500, token);
            }
            if (!await GlobalLocationAsync(session, token)) throw new InvalidOperationException("Chegada à Global ainda não confirmada.");
        }
        session.GlobalInside = true;
        session.GlobalRunDay = SpecialRoutinePolicy.Day(DateTimeOffset.UtcNow);
        session.GlobalUntil ??= SpecialRoutinePolicy.GlobalDeadline(DateTimeOffset.UtcNow, options.DurationMinutes);
        await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.global.until", session.GlobalUntil.Value.ToString("O", CultureInfo.InvariantCulture));
        await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.global.runDay", session.GlobalRunDay);
        session.InDailyCampaign = false;
        session.DailyNeedsTeleport = false;
        session.SapherasFarmConfirmed = false;
        session.SapherasExitedEarly = true;
        await RecordAbbeyExitAsync(session);
        await RecordAnonymousDungeonExitAsync(session);
        session.IsFarmingTa = false;
        if (!(await recognition.FindAsync("global_map", token)).Found)
        {
            await input.PressKeyAsync(KeyM, cancellationToken: token);
            await WaitForReferenceAsync("global_map", "mapa da Global", TimeSpan.FromSeconds(12), pause, token);
        }
        await SpecialClickAsync(session, coordinate.X, coordinate.Y, token);
        string[] goReferences = ["botao_ir", "botao_ir_ta2", "botao_ir_legado"];
        var x = Math.Max(0, coordinate.X - 220);
        var y = Math.Max(0, coordinate.Y - 210);
        var go = await WaitForAnyReferenceInRegionAsync(goReferences, x, y, 470, 230, TimeSpan.FromSeconds(8), pause, token);
        if (go is null) throw new InvalidOperationException("Global: botão Ir não reconhecido no ponto capturado.");
        var point = gameWindows.MapReferencePoint(session.Options.Target, go.X, go.Y);
        await ClickGoButtonWithConfirmationAsync(session, goReferences, x, y, point.X, point.Y, pause, token);
        await input.PressKeyAsync(KeyM, cancellationToken: token);
        await WaitForReferenceToDisappearAsync("global_map", TimeSpan.FromSeconds(8), pause, token);
        if ((await recognition.FindAsync("global_map", token)).Found)
            throw new InvalidOperationException("Mapa Global ainda aberto após Ir.");
        await OpenRestForTravelAsync(session, pause, token);
        await WaitForFarmArrivalAsync(session, pause, token, "Global");
        if (!GlobalHasPriority(session)) throw new InvalidOperationException("Tempo da Global terminou durante deslocamento.");
        session.AwaitingHuntActivationAtSpot = true;
        await StartAutomaticHuntAsync(session, pause, token);
        session.IsFarmingTa = true;
        session.Audio.Armed = true;
        session.NextRecoveryAttemptAt = default;
        WriteLog(session, $"Farm Global confirmado até {SpecialRoutinePolicy.Local(session.GlobalUntil.Value):HH:mm}; diárias ficam para depois.");
    }
}
