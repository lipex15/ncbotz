using BotNC.App.Models;

namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private async Task<bool> RunLoveBossSafelyAsync(
        ClientSession session,
        IReadOnlyList<ClientSession> allSessions,
        DailyRoutineOptions options,
        PauseController pause,
        CancellationToken cancellationToken,
        DateTime? nextSapheras)
    {
        if (!options.EnableLoveBoss || !session.Options.EnableLoveBoss ||
            session.HandlingDeath || session.NeedsDeathRestoration || !session.StartupRestorationChecked ||
            session.InAgenda || DateTime.UtcNow < session.NextLoveBossAttemptAt)
            return false;

        if (session.LoveBossInside && DateTime.UtcNow < session.NextLoveBossRoomScanAt)
            return false;

        var now = DateTimeOffset.UtcNow;
        var day = LoveBossSchedule.DailyKey(now);
        var week = LoveBossSchedule.WeeklyKey(now);
        if (session.LoveBossWeek != week)
        {
            session.LoveBossWeek = week;
            session.LoveBossWeeklyCount = 0;
        }

        if (!session.LoveBossStartupChecked)
        {
            session.LoveBossStartupChecked = true;
            session.LoveBossInside = (await FindReferenceOnClientAsync(
                session, "boss_room", cancellationToken, requireObservable: true)).Found;
        }

        var rewardPending = session.LoveBossCompletedCycle == day &&
                            session.LoveBossRewardCycle != day;
        var slot = LoveBossSchedule.EntrySlot(now);
        if (!session.LoveBossInside && !session.LoveBossReturnToFarmPending && !rewardPending &&
            (session.LoveBossCompletedCycle == day ||
             session.LoveBossWeeklyCount >= 5 ||
             slot is null ||
             session.LoveBossLastSlot == LoveBossSchedule.SlotKey(slot.Value)))
            return false;

        // Uma luta pode ocupar até 30 minutos. Sapheras continua prioritária.
        if (!session.LoveBossInside && !session.LoveBossReturnToFarmPending && session.Options.UseSapheras && nextSapheras.HasValue &&
            nextSapheras.Value - DateTime.Now < TimeSpan.FromMinutes(38))
            return false;

        try
        {
            await ActivateGameAsync(session, cancellationToken);
            if (session.LoveBossReturnToFarmPending)
            {
                await ResumeAfterLoveBossAsync(session, pause, cancellationToken);
                session.LoveBossReturnToFarmPending = false;
                WriteLog(session, "Raide encerrada neste cliente; farm retomado.");
                return true;
            }
            await AbortWorkflowIfDeathDetectedAsync(session, "antes do Boss do Amor", cancellationToken);
            if (session.LoveBossInside)
            {
                await MonitorLoveBossInsideStepAsync(session, pause, cancellationToken, day);
                return true;
            }

            if (rewardPending)
            {
                await ExitRestIfNeededAsync(session, pause, cancellationToken);
                await ClaimLoveBossRewardsAsync(session, pause, cancellationToken, day);
                await ResumeAfterLoveBossAsync(session, pause, cancellationToken);
                session.NextLoveBossAttemptAt = DateTime.UtcNow.AddMinutes(15);
                return true;
            }

            if (session.InDailyCampaign || session.DailyCycle == DailyCycleKey(DateTime.Now) &&
                session.DailyCompletedCycle != session.DailyCycle)
            {
                session.ResumeDailyAfterLoveBoss = true;
                await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.routines.loveBoss.resumeDaily", "true");
                session.InDailyCampaign = false;
                session.DailyNeedsTeleport = false;
                WriteLog(session, "Pausando as Diárias para o Boss do Amor; o progresso será retomado após a raide.");
            }
            session.IsFarmingTa = false;
            await EnterAndCompleteLoveBossAsync(session, pause, cancellationToken, day, slot!.Value);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HumanInteractionException)
        {
            // Mouse ownership is not a failed raid and must not start recovery.
            return false;
        }
        catch (Exception exception)
        {
            session.NextLoveBossAttemptAt = DateTime.UtcNow.Add(session.LoveBossInside
                ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(10));
            WritePersistentOnly(session, exception.ToString());
            WriteLog(session,
                $"Boss do Amor interrompido com segurança: {exception.GetBaseException().Message}. " +
                $"Próxima verificação em {(session.LoveBossInside ? "15 segundos na sala" : "10 minutos")}; sem repetir a entrada.");
            try
            {
                session.LoveBossInside = (await FindReferenceOnClientAsync(
                    session, "boss_room", cancellationToken, requireObservable: true)).Found;
                if (!session.LoveBossInside)
                {
                    await ExitRestIfNeededAsync(session, pause, cancellationToken);
                    await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
                    await ResumeAfterLoveBossAsync(session, pause, cancellationToken);
                }
            }
            catch (Exception recoveryException)
            {
                WritePersistentOnly(session, $"Recuperação do Boss do Amor: {recoveryException}");
                session.NextRecoveryAttemptAt = DateTime.UtcNow.AddSeconds(5);
                session.RequiresHardFlowReset = true;
            }
            return true;
        }
    }

    private async Task EnterAndCompleteLoveBossAsync(
        ClientSession session, PauseController pause, CancellationToken cancellationToken,
        string day, DateTimeOffset slot)
    {
        await ExitRestIfNeededAsync(session, pause, cancellationToken);
        // A entrada começa pelo ícone ao lado do minimapa. O menu (=) serve
        // apenas para ler 1/5...5/5 e resgatar prêmios após a luta.
        if (session.AbbeyInside || session.AnonymousDungeonInside)
            await LeaveFarmDungeonForLoveBossAsync(session, pause, cancellationToken);

        if (LoveBossSchedule.EntrySlot(DateTimeOffset.UtcNow) != slot)
        {
            WriteLog(session, "Janela de entrada do boss terminou durante a preparação; sem clique tardio.");
            await ResumeAfterLoveBossAsync(session, pause, cancellationToken);
            return;
        }

        if (await IsTaSelectorContextVisibleAsync(
                session, EntryReadyReference(EffectiveTaDestination(session)), cancellationToken))
        {
            WriteLog(session, "Fechando o seletor da T.A antes de usar o ícone do Boss do Amor.");
            await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
            await Task.Delay(400, cancellationToken);
        }
        WriteLog(session, "Boss do Amor: abrindo a entrada pelo ícone ao lado do minimapa (413, 127).");
        var entryPoint = gameWindows.MapReferencePoint(session.Options.Target, 413, 127);
        await input.MoveAndClickAsync(entryPoint.X, entryPoint.Y, TimeSpan.FromMilliseconds(300),
            cancellationToken, cooldown: TimeSpan.FromMilliseconds(160));
        await WaitForReferenceAsync("boss_entry_panel", "convite da Raide de Chefe",
            TimeSpan.FromSeconds(10), pause, cancellationToken);
        await WaitForReferenceAsync("boss_entry_button", "botão Entrar na Raide",
            TimeSpan.FromSeconds(8), pause, cancellationToken);
        await input.MoveAndClickAsync(972, 827, TimeSpan.FromMilliseconds(320),
            cancellationToken, cooldown: TimeSpan.FromMilliseconds(160));
        await WaitForReferenceAsync("boss_room", "Berço da Chama Vermelha",
            TimeSpan.FromSeconds(55), pause, cancellationToken);
        session.LoveBossLastSlot = LoveBossSchedule.SlotKey(slot);
        await database.SaveSettingAsync(
            $"{SessionSettingPrefix(session)}.routines.loveBoss.lastSlot",
            session.LoveBossLastSlot);
        session.LoveBossInside = true;
        session.LoveBossAutoCommandSent = false;
        session.LoveBossRoomDeadline = DateTime.UtcNow.AddMinutes(36);
        session.NextLoveBossRoomScanAt = DateTime.UtcNow;
        WriteLog(session, "Entrada no Berço da Chama Vermelha confirmada.");
        session.LoveBossAutoConfirmed = await EnsureLoveBossAutoAsync(session, pause, cancellationToken);
        session.LoveBossRoomDeadline = DateTime.UtcNow.AddMinutes(36);
        session.NextLoveBossRoomScanAt = DateTime.UtcNow.AddSeconds(3);
        SetStatus(BotRunState.Running, $"{session.Options.Label}: Boss do Amor",
            session.LoveBossAutoConfirmed ? "Auto confirmado; acompanhando a sala" : "Auto ainda não confirmado; acompanhando a sala sem alternar o botão");
    }

    private async Task MonitorLoveBossInsideStepAsync(
        ClientSession session, PauseController pause, CancellationToken cancellationToken,
        string day)
    {
        if (!session.LoveBossAutoConfirmed)
        {
            session.LoveBossAutoConfirmed = await EnsureLoveBossAutoAsync(session, pause, cancellationToken);
        }
        if (DateTime.UtcNow < session.NextLoveBossRoomScanAt)
            return;
        session.LoveBossRoomDeadline = session.LoveBossRoomDeadline == default
            ? DateTime.UtcNow.AddMinutes(36) : session.LoveBossRoomDeadline;
        if (DateTime.UtcNow >= session.LoveBossRoomDeadline)
            throw new TimeoutException("A sala do boss não confirmou vitória nem saída em 36 minutos.");

        await CheckpointAsync(pause, cancellationToken);
        await AbortWorkflowIfDeathDetectedAsync(session, "durante o Boss do Amor", cancellationToken);
        var frame = await CaptureClientFrameAsync(session, cancellationToken);
        var room = await recognition.FindAsync("boss_room", frame, cancellationToken);
        session.LoveBossRoomMissingHits = room.Found ? 0 : session.LoveBossRoomMissingHits + 1;
        if (session.LoveBossRoomMissingHits >= 3)
            throw new InvalidOperationException("A sala do boss desapareceu antes da vitória confirmada.");

        var victory = await recognition.FindAsync("boss_victory", frame, cancellationToken);
        var exitTimer = await recognition.FindAsync("boss_exit_timer", frame, cancellationToken);
        var phase = await _loveBossReader.ReadPhaseAsync(frame, cancellationToken);
        session.LoveBossVictoryHits = victory.Found || exitTimer.Found ||
                                      phase.Phase == LoveBossPhase.Leaving
            ? session.LoveBossVictoryHits + 1 : 0;
        session.NextLoveBossRoomScanAt = DateTime.UtcNow.AddSeconds(3);
        if (session.LoveBossVictoryHits < 2)
            return;

        WriteLog(session, $"Vitória do Boss do Amor confirmada pelo HUD ({phase.Evidence}).");
        await MarkLoveBossCompletedAsync(session, day);
        await LeaveLoveBossRoomAsync(session, pause, cancellationToken);
        session.LoveBossAutoConfirmed = false;
        await ClaimLoveBossRewardsAsync(session, pause, cancellationToken, day);
        session.LoveBossReturnToFarmPending = true;
        WriteLog(session, "Recompensa tratada; retorno deste cliente ao farm preparado, sem depender da outra janela.");
    }

    private async Task<bool> EnsureLoveBossAutoAsync(
        ClientSession session, PauseController pause, CancellationToken cancellationToken)
    {
        var first = await OpenHudHuntReader.ReadAsync(recognition,
            await CaptureClientFrameAsync(session, cancellationToken), cancellationToken);
        await Task.Delay(350, cancellationToken);
        var second = await OpenHudHuntReader.ReadAsync(recognition,
            await CaptureClientFrameAsync(session, cancellationToken), cancellationToken);
        if (first == OpenHudHuntState.Active && second == OpenHudHuntState.Active)
        {
            WriteLog(session, "Auto já está ligado na sala do boss.");
            return true;
        }
        // A falta do template não prova Auto desligado. Nunca alternar novamente
        // um comando já enviado só porque uma animação prejudicou a confirmação.
        if (!ShouldEnableBossAuto(first, second, session.LoveBossAutoCommandSent)) return false;
        await CheckpointAsync(pause, cancellationToken);
        var point = gameWindows.MapReferencePoint(session.Options.Target, 1875, 672);
        await input.MoveAndClickAsync(point.X, point.Y, TimeSpan.FromMilliseconds(300),
            cancellationToken, cooldown: TimeSpan.FromMilliseconds(170));
        session.LoveBossAutoCommandSent = true;
        var deadline = DateTime.UtcNow.AddSeconds(8);
        var hits = 0;
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            hits = await OpenHudHuntReader.ReadAsync(recognition,
                await CaptureClientFrameAsync(session, cancellationToken), cancellationToken) == OpenHudHuntState.Active ? hits + 1 : 0;
            if (hits >= 2)
            {
                WriteLog(session, "Auto ligado e confirmado visualmente.");
                return true;
            }
            await Task.Delay(350, cancellationToken);
        }
        WriteLog(session, "Auto do boss não confirmado após o comando; mantendo a leitura da sala, sem alternar o botão às cegas.");
        return false;
    }

    internal static bool ShouldEnableBossAuto(OpenHudHuntState first, OpenHudHuntState second, bool commandSent) =>
        !commandSent && first == OpenHudHuntState.Inactive && second == OpenHudHuntState.Inactive;

    private async Task LeaveFarmDungeonForLoveBossAsync(
        ClientSession session, PauseController pause, CancellationToken cancellationToken)
    {
        WriteLog(session, "Saindo da masmorra atual para o Boss do Amor, conforme configurado.");
        await input.MoveAndClickAsync(354, 131, TimeSpan.FromMilliseconds(300),
            cancellationToken, cooldown: TimeSpan.FromMilliseconds(160));
        await WaitForReferenceAsync(session.AnonymousDungeonInside ? "anonymous_exit_confirmation" : "boss_exit_ok", "confirmação de saída da masmorra",
            TimeSpan.FromSeconds(8), pause, cancellationToken);
        await input.PressKeyAsync(KeyY, cancellationToken: cancellationToken);
        var deadline = DateTime.UtcNow.AddSeconds(50);
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            var stillInside = session.AbbeyInside
                ? await IsAbbeyLocationVisibleAsync(cancellationToken)
                : await IsAnonymousDungeonLocationVisibleAsync(cancellationToken);
            if (!stillInside)
            {
                await RecordAbbeyExitAsync(session);
                await RecordAnonymousDungeonExitAsync(session);
                return;
            }
            await Task.Delay(700, cancellationToken);
        }
        throw new TimeoutException("A saída da masmorra para o Boss do Amor não foi confirmada.");
    }

    private async Task LeaveLoveBossRoomAsync(
        ClientSession session, PauseController pause, CancellationToken cancellationToken)
    {
        await input.MoveAndClickAsync(353, 130, TimeSpan.FromMilliseconds(310),
            cancellationToken, cooldown: TimeSpan.FromMilliseconds(160));
        await WaitForReferenceAsync("boss_exit_popup", "popup de saída da Raide",
            TimeSpan.FromSeconds(9), pause, cancellationToken);
        await input.PressKeyAsync(KeyY, cancellationToken: cancellationToken);
        await WaitForReferenceToDisappearAsync("boss_room", TimeSpan.FromSeconds(50),
            pause, cancellationToken);
        session.LoveBossInside = false;
        WriteLog(session, "Saída do Berço da Chama Vermelha confirmada.");
    }

    private async Task OpenLoveBossMissionPanelAsync(
        ClientSession session, PauseController pause, CancellationToken cancellationToken)
    {
        if ((await recognition.FindAsync("boss_reward_panel", cancellationToken)).Found)
            return;
        await ActivateGameAsync(session, cancellationToken);
        await ExitRestIfNeededAsync(session, pause, cancellationToken);
        var menuConfirmed = false;
        for (var attempt = 0; attempt < 3 && !menuConfirmed; attempt++)
        {
            await AbortWorkflowIfDeathDetectedAsync(session, "antes de consultar a recompensa do Boss", cancellationToken);
            menuConfirmed = (await recognition.FindAsync("menu_guild", cancellationToken)).Found ||
                (await recognition.FindAsync("menu_ta", cancellationToken)).Found;
            if (menuConfirmed) break;
            if (attempt > 0) await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
            await EnsureGameForegroundAsync(session, cancellationToken);
            await input.PressKeyAsync(KeyEquals, cancellationToken: cancellationToken);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && !menuConfirmed)
            {
                await CheckpointAsync(pause, cancellationToken);
                ThrowIfDeathPending(session);
                menuConfirmed = (await recognition.FindAsync("menu_guild", cancellationToken)).Found ||
                    (await recognition.FindAsync("menu_ta", cancellationToken)).Found;
                if (!menuConfirmed) await Task.Delay(250, cancellationToken);
            }
        }
        if (!menuConfirmed) throw new TimeoutException("Menu da Raide não abriu após três tentativas; recompensa permanece pendente.");
        await input.MoveAndClickAsync(1880, 513, TimeSpan.FromMilliseconds(300),
            cancellationToken, cooldown: TimeSpan.FromMilliseconds(150));
        await WaitForReferenceAsync("boss_reward_panel", "painel Raide de Chefe",
            TimeSpan.FromSeconds(12), pause, cancellationToken);
    }

    private async Task CloseLoveBossMissionPanelAsync(
        PauseController pause, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2 &&
             (await recognition.FindAsync("boss_reward_panel", cancellationToken)).Found; attempt++)
        {
            await CheckpointAsync(pause, cancellationToken);
            await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
            await Task.Delay(300, cancellationToken);
        }
    }

    private async Task<LoveBossMissionStatus> ReadLoveBossMissionStableAsync(
        ClientSession session, PauseController pause, CancellationToken cancellationToken)
    {
        LoveBossMissionStatus? previous = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await CheckpointAsync(pause, cancellationToken);
            var reading = await _loveBossReader.ReadMissionAsync(
                await CaptureClientFrameAsync(session, cancellationToken), cancellationToken);
            if (previous is not null &&
                reading.DailyCompleted == previous.DailyCompleted &&
                reading.WeeklyCompleted == previous.WeeklyCompleted &&
                reading.DailyCompleted.HasValue && reading.WeeklyCompleted.HasValue)
                return reading;
            previous = reading;
            await Task.Delay(350, cancellationToken);
        }
        WriteLog(session, $"Leitura instável da missão do boss: {previous?.Evidence}.");
        return new LoveBossMissionStatus(null, null, previous?.Evidence ?? "nenhuma leitura");
    }

    private async Task ClaimLoveBossRewardsAsync(
        ClientSession session, PauseController pause, CancellationToken cancellationToken,
        string day)
    {
        await OpenLoveBossMissionPanelAsync(session, pause, cancellationToken);
        try
        {
            var mission = await ReadLoveBossMissionStableAsync(session, pause, cancellationToken);
            if (mission.WeeklyCompleted is { } count)
            {
                session.LoveBossWeeklyCount = count;
                await SaveLoveBossWeeklyStateAsync(session);
            }
            await ClaimLoveBossRewardsFromOpenPanelAsync(
                session, pause, cancellationToken, mission, day);
        }
        finally
        {
            await CloseLoveBossMissionPanelAsync(pause, cancellationToken);
        }
    }

    private async Task ClaimLoveBossRewardsFromOpenPanelAsync(
        ClientSession session, PauseController pause, CancellationToken cancellationToken,
        LoveBossMissionStatus mission, string day)
    {
        if (mission.DailyCompleted != 1)
            throw new InvalidOperationException(
                $"Vitória sem contador diário 1/1 no painel da Raide: {mission.Evidence}.");

        var dailyButton = await recognition.FindAsync(
            "boss_reward_button", 1260, 340, 210, 105, cancellationToken);
        if (dailyButton.Found)
        {
            await input.MoveAndClickAsync(1350, 391, TimeSpan.FromMilliseconds(300),
                cancellationToken, cooldown: TimeSpan.FromMilliseconds(160));
            await WaitForReferenceAsync("boss_reward_received", "recompensa diária obtida",
                TimeSpan.FromSeconds(10), pause, cancellationToken);
            await input.MoveAndClickAsync(955, 328, TimeSpan.FromMilliseconds(290),
                cancellationToken, cooldown: TimeSpan.FromMilliseconds(160));
            await WaitForReferenceToDisappearAsync("boss_reward_received",
                TimeSpan.FromSeconds(8), pause, cancellationToken);
            WriteLog(session, "Recompensa diária do Boss do Amor recebida.");
        }
        else
        {
            var claimed = await _loveBossReader.IsRewardClaimedAsync(
                await CaptureClientFrameAsync(session, cancellationToken), false, cancellationToken);
            if (!claimed)
                throw new InvalidOperationException("Recompensa diária não confirmada: botão não encontrado não significa prêmio recebido.");
            WriteLog(session, "Recompensa diária da Raide confirmada como já recebida.");
        }

        if (mission.WeeklyCompleted == 5)
        {
            var weeklyButton = await recognition.FindAsync(
                "boss_reward_button", 1260, 560, 210, 105, cancellationToken);
            if (weeklyButton.Found)
            {
                await input.MoveAndClickAsync(1350, 609, TimeSpan.FromMilliseconds(300),
                    cancellationToken, cooldown: TimeSpan.FromMilliseconds(160));
                await WaitForReferenceAsync("boss_reward_received", "recompensa semanal obtida",
                    TimeSpan.FromSeconds(10), pause, cancellationToken);
                await input.MoveAndClickAsync(955, 328, TimeSpan.FromMilliseconds(290),
                    cancellationToken, cooldown: TimeSpan.FromMilliseconds(160));
                await WaitForReferenceToDisappearAsync("boss_reward_received",
                    TimeSpan.FromSeconds(8), pause, cancellationToken);
                WriteLog(session, "Recompensa semanal de 5/5 Raides recebida.");
            }
            else if (!await _loveBossReader.IsRewardClaimedAsync(
                         await CaptureClientFrameAsync(session, cancellationToken), true, cancellationToken))
                throw new InvalidOperationException("Recompensa semanal 5/5 ainda não teve coleta confirmada.");
        }
        session.LoveBossRewardCycle = day;
        await database.SaveSettingAsync(
            $"{SessionSettingPrefix(session)}.routines.loveBoss.rewardCycle", day);
    }

    private async Task MarkLoveBossCompletedAsync(ClientSession session, string day)
    {
        await RecordStatisticAsync(session, "boss", "Boss do Amor concluído", $"boss.{day}");
        session.LoveBossCompletedCycle = day;
        await database.SaveSettingAsync(
            $"{SessionSettingPrefix(session)}.routines.loveBoss.completedCycle", day);
    }

    private async Task ResumeAfterLoveBossAsync(ClientSession session, PauseController pause, CancellationToken token)
    {
        if (session.ResumeDailyAfterLoveBoss)
        {
            var resumed = await ResumeDailyCampaignAsync(session, pause, token);
            if (resumed == DailyResumeResult.Started)
            {
                session.InDailyCampaign = true;
                session.NextDailyMissionCheckAt = DateTime.UtcNow.AddMinutes(2);
                WriteLog(session, "Diárias retomadas após o Boss do Amor.");
            }
            else if (resumed == DailyResumeResult.NoMission)
                await MarkDailyCampaignCompletedAsync(session, "Diárias já concluídas ao retornar do boss.");
            else
                await YieldUncertainDailyToFarmAsync(session, pause, token);
            session.ResumeDailyAfterLoveBoss = false;
            await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.routines.loveBoss.resumeDaily", "false");
            if (resumed != DailyResumeResult.NoMission) return;
        }
        await ResumeStableFarmAsync(session, pause, token);
    }

    private async Task SaveLoveBossWeeklyStateAsync(ClientSession session)
    {
        var prefix = $"{SessionSettingPrefix(session)}.routines.loveBoss";
        await database.SaveSettingAsync($"{prefix}.week", session.LoveBossWeek ?? "");
        await database.SaveSettingAsync($"{prefix}.weeklyCount",
            session.LoveBossWeeklyCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
