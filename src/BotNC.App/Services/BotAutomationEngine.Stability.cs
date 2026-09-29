using BotNC.App.Models;

namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private ClientSession? _workflowClient;
    private readonly AsyncLocal<ClientSession?> _interruptibleAction = new();
    private HumanInteractionMonitor? _humanInteraction;
    private IReadOnlyList<ClientSession> _manualSessions = [];
    private bool HumanOwnsInterface => _humanInteraction?.IsBusy == true || _manualSessions.Any(s =>
        ManualInterfacePolicy.Blocks(s.UserInterfaceBusy, s.VisualCaptureFaulted, s.ReconnectPending,
            gameWindows.IsForeground(s.Options.Target)));
    private void RespectHumanInteraction(ClientSession session)
    {
        YieldToOtherEmergency(session);
        ThrowIfDeathPending(session);
        if (!session.HandlingDeath && !session.HandlingProtection && HumanOwnsInterface)
            throw new HumanInteractionException();
    }

    private void YieldToOtherEmergency(ClientSession session)
    {
        if (_manualSessions.Any(other => ShouldYieldToEmergency(session, other)))
            throw new ProtectionTransitionException();
    }

    private static bool ShouldYieldToEmergency(ClientSession current, ClientSession other) =>
        current != other && !current.ServicingEmergencyInput && !other.ReconnectPending && !other.HandlingDeath &&
        Volatile.Read(ref other.PendingVisualDeath) == 0 &&
        Volatile.Read(ref other.PendingVisualLowHp) != 0 &&
        Volatile.Read(ref other.EmergencyTeleportInFlight) == 0;

    private void BindWorkflowClient(ClientSession session)
    {
        if (_workflowClient != session)
            WritePersistentOnly(session, $"workflow_owner hwnd={session.Options.Target.Handle}; pid={session.Options.Target.ProcessId}; daily={session.InDailyCampaign}; farm={session.IsFarmingTa}; restoration={session.NeedsDeathRestoration}");
        _workflowClient = session;
        recognition.WorkflowFrameProvider = token => session.ReconnectPending && !session.HandlingReconnect
            ? throw new ReconnectTransitionException() : CaptureClientFrameAsync(session, token);
        recognition.WorkflowContextId = $"{session.Options.Priority}:{session.Options.Target.Handle}";
        recognition.WorkflowTrace = message => WritePersistentOnly(session, message);
        recognition.WorkflowDiagnosticContext = () => DiagnosticState(session);
        input.WorkflowTrace = message => WritePersistentOnly(session, message);
        input.ValidateNormalInteraction = () => RespectHumanInteraction(session);
        input.PrepareWorkflowTargetAsync = async token =>
        {
            RespectHumanInteraction(session);
            if (gameWindows.IsForeground(session.Options.Target)) return;
            WritePersistentOnly(session, "focus_request reason=workflow_input; observation=false");
            if (!gameWindows.Activate(session.Options.Target))
                throw new InvalidOperationException($"Não foi possível ativar {session.Options.Label} para executar o comando.");
            await Task.Delay(180, token);
            // No input is sent if focus/ownership changed during activation.
        };
        input.ValidateWorkflowTarget = () =>
        {
            ThrowIfDeathPending(session);
            if (!gameWindows.IsForeground(session.Options.Target))
                throw new InvalidOperationException($"{session.Options.Label}: foco mudou antes do comando; ação cancelada sem clicar na outra janela.");
        };
    }

    private async Task InitializeClientActivityAsync(
        IReadOnlyList<ClientSession> sessions, ClientSession session, SapherasOptions sapheras,
        AntiOverkillOptions antiOverkill, DailyRoutineOptions routines, PauseController pause,
        CancellationToken token, DateTime? nextSapheras)
    {
        var previous = session.InitialPreparationActive;
        session.InitialPreparationActive = true;
        session.RestPreference.Request();
        try
        {
            await InitializeClientActivityCoreAsync(sessions, session, sapheras, antiOverkill, routines, pause, token, nextSapheras);
        }
        finally
        {
            session.InitialPreparationActive = previous;
            session.RestPreference.Request();
        }
    }

    private async Task InitializeClientActivityCoreAsync(
        IReadOnlyList<ClientSession> sessions, ClientSession session, SapherasOptions sapheras,
        AntiOverkillOptions antiOverkill, DailyRoutineOptions routines, PauseController pause,
        CancellationToken token, DateTime? nextSapheras)
    {
        if (session.ReconnectPending && !session.HandlingReconnect) return;
        if (await RunStartupRestorationSafelyAsync(session, sapheras, antiOverkill, pause, token)) return;
        if (GlobalHasPriority(session) && (session.GlobalInside || DateTime.UtcNow >= session.NextGlobalAttemptAt))
        {
            await EnterConfiguredFarmAsync(session, pause, token, isEmergency: false);
            return;
        }
        if (await TryAdoptScheduledDungeonFarmAsync(session, pause, token)) return;
        // Adopt an existing automatic campaign BEFORE opening mail, guild or the TA selector.
        if (await RunLoveBossSafelyAsync(session, sessions, routines, pause, token, nextSapheras)) return;
        if (session.ResumeDailyAfterLoveBoss)
        {
            await ResumeAfterLoveBossAsync(session, pause, token);
            return;
        }
        await ReconcileVisiblePendingDailyAsync(session, routines, token);
        if (await TryAdoptRunningDailyAsync(session, routines, token)) return;
        if (await RecoverOpenRoutinePanelsSafelyAsync(session, routines, pause, token)) return;
        if (await TryStartVisibleDailyCampaignSafelyAsync(session, routines, pause, token)) return;
        await TryCollectDueGuildTreasureAsync(session, pause, token);
        await TryCollectDueMailSafelyAsync(session, pause, token);
        if (await RunDueDailyRoutinesSafelyAsync(session, routines, pause, token) &&
            (session.InDailyCampaign || session.IsFarmingTa || session.NextRecoveryAttemptAt != default)) return;
        await PrepareClientForFarmAsync(sessions, session, sapheras, antiOverkill, pause, token, "após conferir as rotinas");
    }

    private async Task<bool> TryAdoptRunningDailyAsync(ClientSession session, DailyRoutineOptions options, CancellationToken token)
    {
        var cycle = DailyCycleKey(DateTime.Now);
        if (!options.EnableDailyMissions || !session.Options.EnableDailyMissions ||
            session.DailyCompletedCycle == cycle || session.NeedsDeathRestoration || session.InAgenda)
            return false;
        var frame = await CaptureDailyMissionFrameAsync(session, token);
        if (!(await recognition.FindAsync("daily_automatic", frame, token)).Found ||
            FindPurpleDailyMissionY(frame) is null && !(session.InDailyCampaign && session.DailyStartedCycle == cycle))
            return false;
        session.DailyCycle = cycle;
        session.DailyStartedCycle = cycle;
        session.InDailyCampaign = true;
        session.DailyNeedsTeleport = false;
        session.IsFarmingTa = false;
        session.SafeInRest = true;
        session.NextDailyMissionCheckAt = DateTime.UtcNow.AddMinutes(2);
        await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyCycle", cycle);
        await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyStartedCycle", cycle);
        WriteLog(session, "Diárias em andamento reconhecidas na partida; mantendo a campanha, sem viagem à T.A.");
        return true;
    }

    private async Task ReconcileVisiblePendingDailyAsync(ClientSession session, DailyRoutineOptions options, CancellationToken token)
    {
        if (session.StartupDailyReconciled || !options.EnableDailyMissions || !session.Options.EnableDailyMissions ||
            session.DailyCompletedCycle == DailyCycleKey(DateTime.Now)) return;
        for (var sample = 0; sample < 2; sample++)
        {
            var frame = await CaptureDailyMissionFrameAsync(session, token);
            if (FindPurpleDailyMissionY(frame) is null) { session.StartupDailyReconciled = true; return; }
            await Task.Delay(250, token);
        }
        var cycle = DailyCycleKey(DateTime.Now);
        session.DailyCycle = cycle;
        session.DailyCompletedCycle = null;
        session.NextVisibleDailyScanAt = default;
        session.NextDailyRoutineAttemptAt = default;
        await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyCycle", cycle);
        await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyCompletedCycle", "");
        session.StartupDailyReconciled = true;
        WriteLog(session, "Diárias pela metade confirmadas em duas leituras da lista; retomando o progresso antes do farm, sem aceitar novamente.");
    }

    private static bool HasQuestIconNear(PixelFrame frame, int rowY)
    {
        var hits = 0;
        for (var y = Math.Max(0, rowY - 18); y < Math.Min(frame.Height, rowY + 19); y++)
        for (var x = 1514; x < Math.Min(frame.Width, 1544); x++)
        {
            var offset = y * frame.Stride + x * 4;
            var b = frame.Pixels[offset]; var g = frame.Pixels[offset + 1]; var r = frame.Pixels[offset + 2];
            if (r >= 115 && g >= 95 && b >= 65 && r >= g && g > b && r - b >= 20 && ++hits >= 18) return true;
            if (r >= 115 && b >= 125 && g <= 180 && b >= g + 25 && b >= r + 8 && ++hits >= 18) return true;
        }
        return false;
    }

    private async Task ResumeStableFarmAsync(ClientSession session, PauseController pause, CancellationToken token)
    {
        if (session.NeedsDeathRestoration || !session.StartupRestorationChecked)
            throw new InvalidOperationException("Restauração ainda não liberou o retorno ao combate.");
        await ActivateGameAsync(session, token);
        var hunt = await FindReferenceOnClientAsync(session, "caca_automatica", token, requireObservable: true);
        var openHunt = await OpenHudHuntReader.ReadAsync(recognition, await CaptureClientFrameAsync(session, token), token);
        if (session.IsFarmingTa && (hunt.Found || openHunt == OpenHudHuntState.Active))
        {
            session.SafeInRest = hunt.Found;
            WriteLog(session, "Farm confirmado no ponto atual; nenhuma nova entrada necessária.");
            return;
        }
        if (session.IsFarmingTa)
            await ResumeHuntAtCurrentSpotAsync(session, true, pause, token);
        else
            await EnterConfiguredFarmAsync(session, pause, token, isEmergency: false);
    }

    internal static TimeSpan RecoveryBackoff(int failures) => failures switch
    {
        1 => TimeSpan.FromSeconds(5),
        2 => TimeSpan.FromSeconds(10),
        _ => TimeSpan.FromSeconds(15)
    };

    internal static void VerifyWorkflowStabilityPolicy()
    {
        foreach (var firstState in Enum.GetValues<OpenHudHuntState>())
        foreach (var secondState in Enum.GetValues<OpenHudHuntState>())
        {
            var expected = firstState == OpenHudHuntState.Inactive && secondState == OpenHudHuntState.Inactive;
            if (ShouldEnableBossAuto(firstState, secondState, false) != expected ||
                ShouldEnableBossAuto(firstState, secondState, true))
                throw new InvalidOperationException("Auto do Boss só pode ser ligado com duas leituras de desligado e sem comando anterior.");
        }
        var first = new ClientSession(new AutomationClientOptions("Cliente 1", new GameWindowTarget(0, "Teste 1", 1, false, true), TaDestination.Ta2, false, 1, null));
        var second = new ClientSession(new AutomationClientOptions("Cliente 2", new GameWindowTarget(0, "Teste 2", 2, false, true), TaDestination.Ta3, false, 2, null));
        first.IsFarmingTa = true;
        first.OpenHudHunt = OpenHudHuntState.Active;
        second.LoveBossInside = true;
        if (!DescribeCurrentClientActivity(first).Contains("Auto ligado") ||
            !DescribeCurrentClientActivity(second).Contains("Boss do Amor"))
            throw new InvalidOperationException("Atividades dos clientes não refletem farm aberto e Boss independentes.");
        first.OpenHudHunt = OpenHudHuntState.Unknown;
        first.UserInterfaceBusy = true;
        if (!DescribeCurrentClientActivity(first).Contains("uso manual") || !first.IsFarmingTa)
            throw new InvalidOperationException("Interação manual apagou o estado de farm.");
        first.UserInterfaceBusy = false;
        first.RestHudVisible = true;
        if (!DescribeCurrentClientActivity(first).Contains("modo descanso"))
            throw new InvalidOperationException("Descanso não representado no estado do farm.");
        first.IsFarmingTa = false;
        second.LoveBossInside = false;
        first.InDailyCampaign = true;
        second.DailyCompletedCycle = "2026-09-23";
        second.DirectiveCycle = "2026-09-23";
        if (second.InDailyCampaign || first.DailyCompletedCycle is not null || SessionSettingPrefix(first) == SessionSettingPrefix(second))
            throw new InvalidOperationException("Estados dos clientes não estão isolados.");
        if (RecoveryBackoff(4) > TimeSpan.FromSeconds(15) || RecoveryBackoff(50) > TimeSpan.FromSeconds(15))
            throw new InvalidOperationException("Recuperação não pode deixar o personagem bloqueado por minutos.");
        if (new DailyMissionListReading(false, null).ListVisible)
            throw new InvalidOperationException("Leitura desconhecida não pode confirmar conclusão.");
        if (UserActivityLog.Describe("Cliente 2: telemetria_audio healthy=True") is not null ||
            UserActivityLog.Describe("Cliente 2: Farm da T.A 2 iniciado e confirmado.")?.Client != "CLIENTE 2" ||
            UserActivityLog.Describe("Cliente 1: Falha na restauração")?.Tone != "#F0B84B" ||
            UserActivityLog.Describe("Cliente 1: Alerta de HP confirmado.")?.Tone != "#FF8290" ||
            UserActivityLog.Describe("Cliente 2: Proteção de HP ARMADA") is not null)
            throw new InvalidOperationException("Separação de logs técnicos e atividade falhou.");
    }

    internal static void VerifyDailyListFixture(PixelFrame campaignFrame)
    {
        var frame = VisualRecognitionService.NormalizeForReferenceMatching(campaignFrame);
        if (!HasVisibleQuestRows(frame) || FindPurpleDailyMissionY(frame) is not null)
            throw new InvalidOperationException("A lista de referência com missões comuns não foi distinguida de Diárias roxas.");
        var pixels = (byte[])frame.Pixels.Clone();
        void PaintPurple(int y0)
        {
            for (var y = y0; y < y0 + 7; y++)
            for (var x = 1600; x < 1620; x++)
            {
                var offset = y * frame.Stride + x * 4;
                pixels[offset] = 220;
                pixels[offset + 1] = 100;
                pixels[offset + 2] = 180;
            }
        }
        PaintPurple(800);
        var sample = new PixelFrame(frame.Width, frame.Height, frame.Stride, pixels);
        if (FindPurpleDailyMissionY(sample) is not null)
            throw new InvalidOperationException("Cor roxa fora da lista não pode reabrir Diárias concluídas.");
        PaintPurple(180);
        void PaintIcon(int row)
        {
            for (var y = row; y < row + 7; y++)
            for (var x = 1527; x < 1534; x++)
            {
                var offset = y * frame.Stride + x * 4;
                pixels[offset] = 100; pixels[offset + 1] = 160; pixels[offset + 2] = 185;
            }
        }
        PaintIcon(180);
        if (FindPurpleDailyMissionY(sample) is not >= 180 or > 186 || FindPurpleDailyMissionY(sample, 4) is not null)
            throw new InvalidOperationException("Missão roxa no topo da lista não reconhecida ou contada várias vezes.");
        PaintIcon(800);
        if (FindPurpleDailyMissionY(sample, 2) is null)
            throw new InvalidOperationException("Missão abaixo de y=650 foi ignorada.");
        var blank = new PixelFrame(frame.Width, frame.Height, frame.Stride, new byte[pixels.Length]);
        if (HasVisibleQuestRows(blank) || FindPurpleDailyMissionY(blank) is not null)
            throw new InvalidOperationException("Quadro vazio não pode confirmar lista de missões.");
    }
}
