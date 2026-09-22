using BotNC.App.Models;

namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private async Task<bool> RunLoveBossSafelyAsync(
        ClientSession session,
        DailyRoutineOptions options,
        PauseController pause,
        CancellationToken cancellationToken,
        DateTime? nextSapheras)
    {
        if (!options.EnableLoveBoss || !session.Options.EnableLoveBoss ||
            session.HandlingDeath || session.InAgenda || session.InDailyCampaign ||
            session.NextRecoveryAttemptAt != default ||
            DateTime.UtcNow < session.NextLoveBossAttemptAt)
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
        if (!session.LoveBossInside && !rewardPending &&
            (session.LoveBossCompletedCycle == day ||
             session.LoveBossWeeklyCount >= 5 ||
             slot is null ||
             session.LoveBossLastSlot == LoveBossSchedule.SlotKey(slot.Value)))
            return false;

        // Uma luta pode ocupar até 30 minutos. Sapheras continua prioritária.
        if (!session.LoveBossInside && nextSapheras.HasValue &&
            nextSapheras.Value - DateTime.Now < TimeSpan.FromMinutes(38))
            return false;

        try
        {
            await ActivateGameAsync(session, cancellationToken);
            await AbortWorkflowIfDeathDetectedAsync(session, "antes do Boss do Amor", cancellationToken);
            if (session.LoveBossInside)
            {
                await CompleteLoveBossInsideAsync(session, pause, cancellationToken, day);
                return true;
            }

            if (rewardPending)
            {
                await ExitRestIfNeededAsync(session, pause, cancellationToken);
                await ClaimLoveBossRewardsAsync(session, pause, cancellationToken, day);
                await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
                session.NextLoveBossAttemptAt = DateTime.UtcNow.AddMinutes(15);
                return true;
            }

            await EnterAndCompleteLoveBossAsync(session, pause, cancellationToken, day, slot!.Value);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            session.NextLoveBossAttemptAt = DateTime.UtcNow.AddMinutes(10);
            WritePersistentOnly(session, exception.ToString());
            WriteLog(session,
                $"Boss do Amor interrompido com segurança: {exception.GetBaseException().Message}. " +
                "Não repetirei a entrada às cegas; nova verificação em 10 minutos.");
            try
            {
                session.LoveBossInside = (await FindReferenceOnClientAsync(
                    session, "boss_room", cancellationToken, requireObservable: true)).Found;
                if (!session.LoveBossInside)
                {
                    await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
                    await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
                }
            }
            catch (Exception recoveryException)
            {
                WritePersistentOnly(session, $"Recuperação do Boss do Amor: {recoveryException}");
            }
            return true;
        }
    }

    private async Task EnterAndCompleteLoveBossAsync(
        ClientSession session, PauseController pause, CancellationToken cancellationToken,
        string day, DateTimeOffset slot)
    {
        await ExitRestIfNeededAsync(session, pause, cancellationToken);
        await OpenLoveBossMissionPanelAsync(session, pause, cancellationToken);
        LoveBossMissionStatus mission;
        try
        {
            mission = await ReadLoveBossMissionStableAsync(session, pause, cancellationToken);
            WriteLog(session, $"Boss do Amor: {mission.Evidence}.");
            if (mission.WeeklyCompleted is { } count)
            {
                session.LoveBossWeeklyCount = count;
                await SaveLoveBossWeeklyStateAsync(session);
            }
            if (mission.WeeklyCompleted is null || mission.DailyCompleted is null)
                throw new InvalidOperationException("Contadores diário/semanal da Raide ilegíveis; entrada ignorada para evitar repetição.");
            if (mission.DailyCompleted >= 1 || mission.WeeklyCompleted >= 5)
            {
                if (mission.DailyCompleted >= 1)
                    await MarkLoveBossCompletedAsync(session, day);
                await ClaimLoveBossRewardsFromOpenPanelAsync(session, pause, cancellationToken, mission, day);
                await CloseLoveBossMissionPanelAsync(pause, cancellationToken);
                await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
                return;
            }
        }
        finally
        {
            await CloseLoveBossMissionPanelAsync(pause, cancellationToken);
        }

        // Só abandona a masmorra após confirmar que a missão ainda pode render
        // recompensa; isso evita uma reentrada paga desnecessária.
        if (session.AbbeyInside || session.AnonymousDungeonInside)
            await LeaveFarmDungeonForLoveBossAsync(session, pause, cancellationToken);

        if (LoveBossSchedule.EntrySlot(DateTimeOffset.UtcNow) != slot)
        {
            WriteLog(session, "Janela de entrada do boss terminou durante a preparação; sem clique tardio.");
            await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
            return;
        }

        session.LoveBossLastSlot = LoveBossSchedule.SlotKey(slot);
        await database.SaveSettingAsync(
            $"{SessionSettingPrefix(session)}.routines.loveBoss.lastSlot",
            session.LoveBossLastSlot);

        await input.MoveAndClickAsync(354, 131, TimeSpan.FromMilliseconds(300),
            cancellationToken, cooldown: TimeSpan.FromMilliseconds(160));
        await WaitForReferenceAsync("boss_entry_panel", "convite da Raide de Chefe",
            TimeSpan.FromSeconds(10), pause, cancellationToken);
        await WaitForReferenceAsync("boss_entry_button", "botão Entrar na Raide",
            TimeSpan.FromSeconds(8), pause, cancellationToken);
        await input.MoveAndClickAsync(972, 827, TimeSpan.FromMilliseconds(320),
            cancellationToken, cooldown: TimeSpan.FromMilliseconds(160));
        await WaitForReferenceAsync("boss_room", "Berço da Chama Vermelha",
            TimeSpan.FromSeconds(55), pause, cancellationToken);
        session.LoveBossInside = true;
        WriteLog(session, "Entrada no Berço da Chama Vermelha confirmada.");
        await CompleteLoveBossInsideAsync(session, pause, cancellationToken, day);
    }

    private async Task CompleteLoveBossInsideAsync(
        ClientSession session, PauseController pause, CancellationToken cancellationToken,
        string day)
    {
        await EnsureLoveBossAutoAsync(session, pause, cancellationToken);
        SetStatus(BotRunState.Running, $"{session.Options.Label}: Boss do Amor",
            "Auto ligado; acompanhando Trashi e o temporizador da sala");
        var deadline = DateTime.UtcNow.AddMinutes(36);
        var successHits = 0;
        var roomMissingHits = 0;
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            await AbortWorkflowIfDeathDetectedAsync(session, "durante o Boss do Amor", cancellationToken);
            var frame = capture.CapturePrimaryScreen();
            var room = await recognition.FindAsync("boss_room", frame, cancellationToken);
            roomMissingHits = room.Found ? 0 : roomMissingHits + 1;
            if (roomMissingHits >= 3)
                throw new InvalidOperationException("A sala do boss desapareceu antes da vitória confirmada.");

            var victory = await recognition.FindAsync("boss_victory", frame, cancellationToken);
            var exitTimer = await recognition.FindAsync("boss_exit_timer", frame, cancellationToken);
            var phase = await _loveBossReader.ReadPhaseAsync(frame, cancellationToken);
            successHits = victory.Found || exitTimer.Found || phase.Phase == LoveBossPhase.Leaving
                ? successHits + 1 : 0;
            if (successHits >= 2)
            {
                WriteLog(session, $"Vitória do Boss do Amor confirmada pelo HUD ({phase.Evidence}).");
                await MarkLoveBossCompletedAsync(session, day);
                await LeaveLoveBossRoomAsync(session, pause, cancellationToken);
                await ClaimLoveBossRewardsAsync(session, pause, cancellationToken, day);
                await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
        throw new TimeoutException("A sala do boss não confirmou vitória nem saída em 36 minutos.");
    }

    private async Task EnsureLoveBossAutoAsync(
        ClientSession session, PauseController pause, CancellationToken cancellationToken)
    {
        if ((await recognition.FindAsync("boss_auto_on", cancellationToken)).Found)
        {
            WriteLog(session, "Auto já está ligado na sala do boss.");
            return;
        }
        await CheckpointAsync(pause, cancellationToken);
        await input.MoveAndClickAsync(1875, 672, TimeSpan.FromMilliseconds(300),
            cancellationToken, cooldown: TimeSpan.FromMilliseconds(170));
        await WaitForReferenceAsync("boss_auto_on", "Auto ligado na Raide",
            TimeSpan.FromSeconds(8), pause, cancellationToken);
        WriteLog(session, "Auto ligado e confirmado visualmente.");
    }

    private async Task LeaveFarmDungeonForLoveBossAsync(
        ClientSession session, PauseController pause, CancellationToken cancellationToken)
    {
        WriteLog(session, "Saindo da masmorra atual para o Boss do Amor, conforme configurado.");
        await input.MoveAndClickAsync(354, 131, TimeSpan.FromMilliseconds(300),
            cancellationToken, cooldown: TimeSpan.FromMilliseconds(160));
        await WaitForReferenceAsync("boss_exit_ok", "confirmação de saída da masmorra",
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
        if (!(await recognition.FindAsync("menu_guild", cancellationToken)).Found)
        {
            await input.PressKeyAsync(KeyEquals, cancellationToken: cancellationToken);
            await WaitForReferenceAsync("menu_guild", "menu lateral",
                TimeSpan.FromSeconds(8), pause, cancellationToken);
        }
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
                capture.CapturePrimaryScreen(), cancellationToken);
            if (previous is not null &&
                reading.DailyCompleted == previous.DailyCompleted &&
                reading.WeeklyCompleted == previous.WeeklyCompleted &&
                reading.DailyCompleted.HasValue && reading.WeeklyCompleted.HasValue)
                return reading;
            previous = reading;
            await Task.Delay(350, cancellationToken);
        }
        WriteLog(session, $"Leitura instável da missão do boss: {previous?.Evidence}.");
        return previous ?? new LoveBossMissionStatus(null, null, "nenhuma leitura");
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
            WriteLog(session, "Recompensa diária da Raide já aparece como recebida.");

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
        }
        session.LoveBossRewardCycle = day;
        await database.SaveSettingAsync(
            $"{SessionSettingPrefix(session)}.routines.loveBoss.rewardCycle", day);
    }

    private async Task MarkLoveBossCompletedAsync(ClientSession session, string day)
    {
        session.LoveBossCompletedCycle = day;
        await database.SaveSettingAsync(
            $"{SessionSettingPrefix(session)}.routines.loveBoss.completedCycle", day);
    }

    private async Task SaveLoveBossWeeklyStateAsync(ClientSession session)
    {
        var prefix = $"{SessionSettingPrefix(session)}.routines.loveBoss";
        await database.SaveSettingAsync($"{prefix}.week", session.LoveBossWeek ?? "");
        await database.SaveSettingAsync($"{prefix}.weeklyCount",
            session.LoveBossWeeklyCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
