using BotNC.App.Models;

namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private ClientSession? _workflowClient;

    private void BindWorkflowClient(ClientSession session)
    {
        if (_workflowClient != session)
            WritePersistentOnly(session, $"workflow_owner hwnd={session.Options.Target.Handle}; pid={session.Options.Target.ProcessId}; daily={session.InDailyCampaign}; farm={session.IsFarmingTa}; restoration={session.NeedsDeathRestoration}");
        _workflowClient = session;
        recognition.WorkflowFrameProvider = token => CaptureClientFrameAsync(session, token);
        recognition.WorkflowContextId = $"{session.Options.Priority}:{session.Options.Target.Handle}";
        recognition.WorkflowTrace = message => WritePersistentOnly(session, message);
        input.WorkflowTrace = message => WritePersistentOnly(session, message);
        input.ValidateWorkflowTarget = () =>
        {
            if (!gameWindows.IsForeground(session.Options.Target))
                throw new InvalidOperationException($"{session.Options.Label}: foco mudou antes do comando; ação cancelada sem clicar na outra janela.");
        };
    }

    private async Task InitializeClientActivityAsync(
        IReadOnlyList<ClientSession> sessions, ClientSession session, SapherasOptions sapheras,
        AntiOverkillOptions antiOverkill, DailyRoutineOptions routines, PauseController pause,
        CancellationToken token, DateTime? nextSapheras)
    {
        if (await RunStartupRestorationSafelyAsync(session, sapheras, antiOverkill, pause, token)) return;
        // Adopt an existing automatic campaign BEFORE opening mail, guild or the TA selector.
        if (await RunLoveBossSafelyAsync(session, sessions, routines, pause, token, nextSapheras)) return;
        if (session.ResumeDailyAfterLoveBoss)
        {
            await ResumeAfterLoveBossAsync(session, pause, token);
            return;
        }
        if (await TryAdoptRunningDailyAsync(session, routines, token)) return;
        if (await RecoverOpenRoutinePanelsSafelyAsync(session, routines, pause, token)) return;
        if (await TryStartVisibleDailyCampaignSafelyAsync(session, routines, pause, token)) return;
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
            session.DailyCycle != cycle && FindPurpleDailyMissionY(frame, 2) is null)
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

    private async Task ResumeStableFarmAsync(ClientSession session, PauseController pause, CancellationToken token)
    {
        if (session.NeedsDeathRestoration || !session.StartupRestorationChecked)
            throw new InvalidOperationException("Restauração ainda não liberou o retorno ao combate.");
        await ActivateGameAsync(session, token);
        var hunt = await FindReferenceOnClientAsync(session, "caca_automatica", token, requireObservable: true);
        if (session.IsFarmingTa && hunt.Found)
        {
            session.SafeInRest = true;
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
        3 => TimeSpan.FromSeconds(20),
        _ => TimeSpan.FromMinutes(5)
    };

    internal static void VerifyWorkflowStabilityPolicy()
    {
        var first = new ClientSession(new AutomationClientOptions("Cliente 1", new GameWindowTarget(0, "Teste 1", 1, false, true), TaDestination.Ta2, false, 1, null));
        var second = new ClientSession(new AutomationClientOptions("Cliente 2", new GameWindowTarget(0, "Teste 2", 2, false, true), TaDestination.Ta3, false, 2, null));
        first.InDailyCampaign = true;
        second.DailyCompletedCycle = "2026-09-23";
        second.DirectiveCycle = "2026-09-23";
        if (second.InDailyCampaign || first.DailyCompletedCycle is not null || SessionSettingPrefix(first) == SessionSettingPrefix(second))
            throw new InvalidOperationException("Estados dos clientes não estão isolados.");
        if (RecoveryBackoff(4) < TimeSpan.FromMinutes(5) || RecoveryBackoff(50) < RecoveryBackoff(4))
            throw new InvalidOperationException("Falhas persistentes não podem repetir a cada 30 segundos.");
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
        if (FindPurpleDailyMissionY(sample) is not >= 180 or > 186 || FindPurpleDailyMissionY(sample, 4) is not null)
            throw new InvalidOperationException("Missão roxa no topo da lista não reconhecida ou contada várias vezes.");
        var blank = new PixelFrame(frame.Width, frame.Height, frame.Stride, new byte[pixels.Length]);
        if (HasVisibleQuestRows(blank) || FindPurpleDailyMissionY(blank) is not null)
            throw new InvalidOperationException("Quadro vazio não pode confirmar lista de missões.");
    }
}
