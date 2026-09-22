using System.Globalization;
using System.IO;
using BotNC.App.Models;

namespace BotNC.App.Services;

public sealed class BotAutomationEngine(
    GameWindowService gameWindows,
    WindowsInputService input,
    VisualRecognitionService recognition,
    ScreenCaptureService capture,
    AppDatabase database)
{
    private const int KeyEscape = 0x1B;
    private const int KeyW = 0x57;
    private const int KeyQ = 0x51;
    private const int KeyL = 0x4C;
    private const int KeyM = 0x4D;
    private const int KeyV = 0x56;
    private const int KeyY = 0x59;
    private const int KeyEquals = 0xBB;
    private const double FullDeathConfidence = 0.66;
    private const double FixedDeathElementConfidence = 0.82;
    private const double RestDeathConfidence = 0.68;
    private const double TaContextConfidence = 0.48;
    private readonly Random _random = new();
    private readonly RestorationCounterReader _restorationCounterReader = new();
    private readonly object _logFileSync = new();
    private readonly string _runtimeLogPath = CreateRuntimeLogPath();
    private readonly SpotLevelRecognitionService _spotLevelRecognition = new();
    private readonly AbbeyTimeReader _abbeyTimeReader = new();
    private readonly TaEntryTextReader _taEntryTextReader = new();
    private readonly DailyShopStatusReader _dailyShopStatusReader = new();

    private static readonly IReadOnlyDictionary<TaDestination, (int X, int Y)> TaEntryPoints =
        new Dictionary<TaDestination, (int X, int Y)>
        {
            [TaDestination.Ta1Codex] = (574, 773),
            [TaDestination.Ta2] = (842, 772),
            [TaDestination.Ta3] = (1126, 775)
        };

    private static readonly IReadOnlyDictionary<TaDestination, IReadOnlyDictionary<int, (int X, int Y)[]>> FarmSpots =
        new Dictionary<TaDestination, IReadOnlyDictionary<int, (int X, int Y)[]>>
        {
            [TaDestination.Ta2] = new Dictionary<int, (int X, int Y)[]>
            {
                [68] = [(825, 230), (856, 570), (636, 229)],
                [72] = [(410, 488), (874, 649), (1125, 488)],
                [76] = [(774, 426), (1246, 219), (1135, 710)],
                [80] = [(878, 317), (748, 684), (791, 493)],
                [84] = [(458, 319), (1229, 428), (880, 416)],
                [88] = [(989, 744), (1477, 163), (1262, 730)]
            },
            [TaDestination.Ta3] = new Dictionary<int, (int X, int Y)[]>
            {
                [84] = [(1446, 343), (1341, 452), (1086, 635)],
                [88] = [(1657, 641), (1067, 662), (928, 331)],
                [90] = [(411, 138), (1104, 392), (897, 826)],
                [92] = [(1196, 452), (864, 534), (1337, 611)],
                [94] = [(994, 331), (983, 445), (501, 669)],
                [96] = [(719, 254), (1069, 550), (867, 705)],
                [98] = [(607, 784), (968, 713), (1101, 595)],
                [100] = [(1168, 502), (1422, 745), (1096, 683)]
            }
        };

    private static readonly (int X, int Y)[] AbbeySpots =
        [(869, 823), (1007, 814), (1147, 589), (771, 442)];

    private static readonly (int X, int Y)[] AnonymousDungeonSpots =
        [(667, 706), (812, 259), (827, 733), (1141, 989), (1123, 809)];

    private static readonly string[] RestStateReferences =
    [
        "caca_automatica",
        "descanso_ponto_fixo",
        "descanso_movendo",
        "descanso_aguardando_spot",
        "descanso_morte",
        "tela_descanso"
    ];

    public event Action<string>? Log;
    public event Action<BotRunState, string, string>? StatusChanged;
    public event Action<AudioClientStatus>? AudioStatusChanged;

    public string RuntimeLogPath => _runtimeLogPath;

    public async Task RunAsync(
        BotRunOptions runOptions,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        if (runOptions.Clients.Count == 0)
        {
            throw new InvalidOperationException("Selecione ao menos um cliente do Night Crows.");
        }

        if (runOptions.Clients.Select(client => client.Target.ProcessId).Distinct().Count() != runOptions.Clients.Count)
        {
            throw new InvalidOperationException("O mesmo cliente não pode ocupar as duas posições.");
        }

        var sessions = runOptions.Clients
            .OrderBy(client => client.Priority)
            .Select(client => new ClientSession(client))
            .ToArray();

        foreach (var session in sessions)
        {
            session.DailyCycle = await database.GetSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyCycle");
            session.DailyStartedCycle = await database.GetSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyStartedCycle");
            session.DailyCompletedCycle = await database.GetSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyCompletedCycle");
            session.DirectiveCycle = await database.GetSettingAsync($"{SessionSettingPrefix(session)}.routines.directiveCycle");
            session.DirectiveAttemptCycle = await database.GetSettingAsync($"{SessionSettingPrefix(session)}.routines.directiveAttemptCycle");
            _ = int.TryParse(
                await database.GetSettingAsync($"{SessionSettingPrefix(session)}.routines.directiveAttemptCount"),
                out var directiveAttemptCount);
            session.DirectiveAttemptCount = Math.Max(0, directiveAttemptCount);
            session.RestorationAbsentCycle =
                await database.GetSettingAsync($"{SessionSettingPrefix(session)}.restoration.absentCycle");
            session.Mail01Date = await database.GetSettingAsync($"{SessionSettingPrefix(session)}.mail.01Date");
            session.Mail07Date = await database.GetSettingAsync($"{SessionSettingPrefix(session)}.mail.07Date");
            session.DailyShopCycle = await database.GetSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyShopCycle");
            session.DailyShopCommonCycle = await database.GetSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyShopCommonCycle");
            session.DailyShopSummonCycle = await database.GetSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyShopSummonCycle");
            session.DailyShopAttemptCycle = await database.GetSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyShopAttemptCycle");
            _ = int.TryParse(
                await database.GetSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyShopAttemptCount"),
                out var dailyShopAttemptCount);
            session.DailyShopAttemptCount = Math.Max(0, dailyShopAttemptCount);
            await LoadFarmScheduleStateAsync(session, runOptions.FarmSchedule);
            await LoadAbbeyBudgetStateAsync(session);

            if (runOptions.DailyRoutines.EnableDailyMissions && session.Options.EnableDailyMissions)
            {
                var cycle = DailyCycleKey(DateTime.Now);
                if (session.DailyCompletedCycle == cycle)
                {
                    WriteLog(session, "Missões Diárias deste ciclo já foram concluídas; aguardando o próximo reset das 04:00.");
                }
                else if (session.DailyCycle == cycle)
                {
                    WriteLog(session, "Missões Diárias já aceitas neste ciclo; serão retomadas assim que forem vistas na lista lateral, sem esperar o horário configurado.");
                }
            }
        }

        EnsureResolution();
        using var watchdogCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            foreach (var session in sessions)
            {
                try
                {
                    session.WindowCapture = new GameWindowCaptureSession(session.Options.Target);
                    WriteLog(session, "Vigilância visual independente conectada à janela (funciona em segundo plano).");
                }
                catch (Exception exception)
                {
                    session.VisualCaptureFaulted = true;
                    WriteLog(session, $"Vigilância visual indisponível para este cliente: {exception.GetBaseException().Message}.");
                    WritePersistentOnly(session, exception.ToString());
                }
                session.Audio.Status += message => HandleAudioStatus(session, message);
                session.Audio.Telemetry += snapshot =>
                {
                    AudioStatusChanged?.Invoke(
                        new AudioClientStatus(
                            session.Options.Priority,
                            session.Options.Label,
                            snapshot.IsHealthy,
                            snapshot.IsArmed,
                            snapshot.CurrentConfidence,
                            snapshot.RecentPeakConfidence,
                            snapshot.LastAlertAt,
                            snapshot.CapturedBufferCount));
                    if (DateTime.UtcNow - session.LastAudioTelemetryLogAt >= TimeSpan.FromSeconds(5))
                    {
                        session.LastAudioTelemetryLogAt = DateTime.UtcNow;
                        WritePersistentOnly(
                            session,
                            $"telemetria_audio healthy={snapshot.IsHealthy}; armed={snapshot.IsArmed}; " +
                            $"current={snapshot.CurrentConfidence:F4}; peak5s={snapshot.RecentPeakConfidence:F4}; " +
                            $"buffers={snapshot.CapturedBufferCount}; lastAlert={snapshot.LastAlertAt:O}");
                    }
                };
                WriteLog(session, "Conectando proteção de HP ao áudio exclusivo deste cliente.");
                try
                {
                    await session.Audio.StartAsync(session.Options.Target.ProcessId, cancellationToken);
                }
                catch (Exception exception)
                {
                    session.AudioStartFaulted = true;
                    session.NextAudioRestartAt = DateTime.UtcNow + TimeSpan.FromSeconds(8);
                    WriteLog(session, $"Áudio não pôde ser iniciado para este cliente; a vigilância visual continua: {exception.GetBaseException().Message}.");
                    WritePersistentOnly(session, exception.ToString());
                }
                // O watchdog existe mesmo se a primeira conexão visual ou de
                // áudio falhar. Ele começa somente depois da tentativa inicial
                // para não disputar a mesma sessão de áudio no arranque.
                session.WindowWatchdogTask = MonitorClientWindowAsync(
                    session,
                    runOptions.Sapheras.EmergencyTeleportVirtualKey,
                    watchdogCancellation.Token);
            }

            WriteLog($"Log persistente desta execução: {_runtimeLogPath}");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RunCoreAsync(
                        sessions,
                        runOptions.Sapheras,
                        runOptions.AntiOverkill,
                        runOptions.DailyRoutines,
                        pause,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    WriteLog($"FALHA GLOBAL RECUPERÁVEL: {exception.GetBaseException().Message}. O motor permanecerá ativo e reconstruirá os fluxos em 15 segundos.");
                    WritePersistentOnly(exception.ToString());
                    foreach (var session in sessions)
                    {
                        session.InDailyCampaign = false;
                        session.HandlingDeath = false;
                        session.NextRecoveryAttemptAt = DateTime.UtcNow.AddSeconds(15);
                        session.RequiresHardFlowReset = true;
                        session.Audio.Armed = !session.InAgenda;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WriteLog("Execução encerrada pelo usuário.");
            throw;
        }
        catch (Exception exception)
        {
            WriteLog($"ERRO NÃO RECUPERADO: {exception.GetType().Name}: {exception.Message}");
            WritePersistentOnly(exception.ToString());
            throw;
        }
        finally
        {
            watchdogCancellation.Cancel();
            var watchdogTasks = sessions
                .Select(session => session.WindowWatchdogTask)
                .Where(task => task is not null)
                .Cast<Task>()
                .ToArray();
            try
            {
                await Task.WhenAll(watchdogTasks);
            }
            catch (OperationCanceledException)
            {
            }

            foreach (var session in sessions)
            {
                await SaveFarmScheduleStateAsync(session);
                if (session.AbbeyInside)
                {
                    session.AbbeyUsed = CurrentAbbeyUsage(session);
                    session.AbbeyActiveSinceUtc = DateTime.UtcNow;
                    await SaveAbbeyBudgetStateAsync(session);
                }
                session.Audio.Armed = false;
                await session.Audio.StopAsync();
                await session.WindowCaptureGate.WaitAsync(CancellationToken.None);
                try
                {
                    if (session.WindowCapture is not null)
                    {
                        await session.WindowCapture.DisposeAsync();
                        session.WindowCapture = null;
                    }
                }
                finally
                {
                    session.WindowCaptureGate.Release();
                }
            }
        }
    }

    private async Task LoadFarmScheduleStateAsync(ClientSession session, FarmScheduleOptions schedule)
    {
        var configured = session.Options.Label.EndsWith("2", StringComparison.Ordinal)
            ? schedule.Client2
            : schedule.Client1;
        if (!schedule.Enabled || !session.Options.UseFarmSchedule || configured.Count == 0)
        {
            return;
        }

        configured = configured
            .Where(step => step.Destination is FarmScheduleDestination.Abbey or FarmScheduleDestination.AnonymousDungeon)
            .ToArray();
        if (configured.Count == 0)
        {
            WriteLog(session, "A Agenda antiga não contém Abadia ou Estreito de Tenerys; usando a T.A configurada.");
            return;
        }

        if (configured.Any(step => step.Duration <= TimeSpan.Zero ||
            step.Duration > TimeSpan.FromDays(7) ||
            !Enum.IsDefined(step.Destination) ||
            step.Destination == FarmScheduleDestination.AnonymousDungeon &&
            step.AnonymousDungeonLevel is not (86 or 97 or 110)))
        {
            WriteLog(session, "Agenda de farm inválida; usando o destino padrão deste cliente.");
            return;
        }

        session.FarmScheduleSteps = configured;
        var prefix = $"{SessionSettingPrefix(session)}.farmSchedule";
        _ = int.TryParse(await database.GetSettingAsync($"{prefix}.index"), out var index);
        var savedSecondsText = await database.GetSettingAsync($"{prefix}.remainingSeconds");
        var savedSignature = await database.GetSettingAsync($"{prefix}.signature");
        var savedCompleted = await database.GetSettingAsync($"{prefix}.completed");
        var savedWaitingForWeeklyReset = await database.GetSettingAsync($"{prefix}.waitingForWeeklyReset");
        var signature = FarmScheduleSignature(configured);
        var samePlan = string.Equals(savedSignature, signature, StringComparison.Ordinal);
        session.FarmScheduleCompleted = samePlan &&
            string.Equals(savedCompleted, "true", StringComparison.OrdinalIgnoreCase);
        session.FarmScheduleWaitingForWeeklyReset = samePlan &&
            string.Equals(savedWaitingForWeeklyReset, "true", StringComparison.OrdinalIgnoreCase);
        session.FarmScheduleIndex = samePlan ? Math.Clamp(index, 0, configured.Count - 1) : 0;
        var savedSeconds = 0d;
        var hasSavedTime = samePlan &&
            double.TryParse(savedSecondsText, NumberStyles.Float, CultureInfo.InvariantCulture, out savedSeconds) &&
            double.IsFinite(savedSeconds);
        if (hasSavedTime && savedSeconds <= 0 && !session.FarmScheduleCompleted)
        {
            if (session.FarmScheduleIndex + 1 < configured.Count)
                session.FarmScheduleIndex++;
            else
                session.FarmScheduleCompleted = true;
        }

        session.FarmScheduleRemaining = hasSavedTime && savedSeconds > 0 &&
            savedSeconds <= configured[session.FarmScheduleIndex].Duration.TotalSeconds
            ? TimeSpan.FromSeconds(savedSeconds)
            : configured[session.FarmScheduleIndex].Duration;
        session.FarmScheduleLastTickUtc = DateTime.UtcNow;

        await SaveFarmScheduleStateAsync(session);
        WriteLog(session, session.FarmScheduleCompleted
            ? "Agenda já concluída; seguindo para a T.A configurada."
            : $"Agenda retomada em {ScheduleDestinationName(configured[session.FarmScheduleIndex].Destination)}; faltam {FormatDuration(session.FarmScheduleRemaining)} de farm ativo.");
    }

    private async Task SaveFarmScheduleStateAsync(ClientSession session)
    {
        if (session.FarmScheduleSteps.Count == 0)
        {
            return;
        }

        var prefix = $"{SessionSettingPrefix(session)}.farmSchedule";
        await database.SaveSettingAsync($"{prefix}.index", session.FarmScheduleIndex.ToString(CultureInfo.InvariantCulture));
        await database.SaveSettingAsync(
            $"{prefix}.remainingSeconds",
            session.FarmScheduleRemaining.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture));
        await database.SaveSettingAsync($"{prefix}.signature", FarmScheduleSignature(session.FarmScheduleSteps));
        await database.SaveSettingAsync($"{prefix}.completed", session.FarmScheduleCompleted.ToString().ToLowerInvariant());
        await database.SaveSettingAsync(
            $"{prefix}.waitingForWeeklyReset",
            session.FarmScheduleWaitingForWeeklyReset.ToString().ToLowerInvariant());
    }

    private static string FarmScheduleSignature(IReadOnlyList<FarmScheduleStep> steps) =>
        string.Join("|", steps.Select(step => $"{step.Destination}:{step.Duration.Ticks}:{step.AnonymousDungeonLevel}"));

    private async Task<bool> TryAdvanceFarmScheduleAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var wasWaitingForReset = session.FarmScheduleWaitingForWeeklyReset;
        await RefreshAbbeyWeekAsync(session);
        if (wasWaitingForReset && !session.FarmScheduleWaitingForWeeklyReset)
            session.FarmScheduleResumePending = true;
        var now = DateTime.UtcNow;
        var elapsed = session.FarmScheduleLastTickUtc == default
            ? TimeSpan.Zero
            : now - session.FarmScheduleLastTickUtc;
        session.FarmScheduleLastTickUtc = now;
        if (session.InAgenda || session.HandlingDeath || session.InDailyCampaign ||
            session.NextRecoveryAttemptAt != default)
        {
            return false;
        }

        if (session.FarmScheduleResumePending)
        {
            session.FarmScheduleResumePending = false;
            await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
            return true;
        }

        if (session.AbbeyInside && Volatile.Read(ref session.PendingAbbeyTimeExhausted) != 0)
        {
            await RecordAbbeyExitAsync(session);
            session.AbbeyUsed = TimeSpan.FromHours(10);
            session.AbbeyTimeExhausted = true;
            Interlocked.Exchange(ref session.PendingAbbeyTimeExhausted, 0);
            await SaveAbbeyBudgetStateAsync(session);
            await SkipUnavailableFarmScheduleStepsAsync(session);
            WriteLog(session, "O tempo semanal da Abadia terminou; bloqueada até segunda-feira às 04:00.");
            await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
            return true;
        }


        if (session.AnonymousDungeonInside && Volatile.Read(ref session.PendingAnonymousTimeExhausted) != 0)
        {
            Interlocked.Exchange(ref session.PendingAnonymousTimeExhausted, 0);
            session.AnonymousDungeonInside = false;
            session.AnonymousDungeonExhausted = true;
            await SaveAbbeyBudgetStateAsync(session);
            await SkipUnavailableFarmScheduleStepsAsync(session);
            WriteLog(session, "O tempo semanal do Estreito de Tenerys terminou; bloqueado até segunda-feira às 04:00.");
            await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
            return true;
        }

        if (session.FarmScheduleSteps.Count == 0 || session.FarmScheduleCompleted ||
            !(session.AbbeyInside || session.AnonymousDungeonInside))
        {
            return false;
        }

        if (session.SafeInRest && elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromSeconds(30))
        {
            session.FarmScheduleRemaining -= elapsed;
            if (now - session.FarmScheduleLastSaveUtc >= TimeSpan.FromMinutes(1))
            {
                session.FarmScheduleLastSaveUtc = now;
                await SaveFarmScheduleStateAsync(session);
            }
        }

        if (session.FarmScheduleRemaining > TimeSpan.Zero)
        {
            return false;
        }

        await RecordAbbeyExitAsync(session);
        session.AnonymousDungeonInside = false;
        if (session.FarmScheduleIndex + 1 >= session.FarmScheduleSteps.Count)
        {
            session.FarmScheduleCompleted = true;
            session.FarmScheduleWaitingForWeeklyReset = false;
            session.FarmScheduleRemaining = TimeSpan.Zero;
            await SaveFarmScheduleStateAsync(session);
            WriteLog(session, "Agenda concluída; redirecionando para a T.A configurada.");
            await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
            return true;
        }

        session.FarmScheduleIndex++;
        var step = session.FarmScheduleSteps[session.FarmScheduleIndex];
        session.FarmScheduleRemaining = step.Duration;
        session.FarmScheduleLastTickUtc = DateTime.UtcNow;
        await SaveFarmScheduleStateAsync(session);
        WriteLog(session, $"Agenda avançou para {ScheduleDestinationName(step.Destination)} por {step.Duration.TotalMinutes:0.#} min.");
        await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
        return true;
    }

    private async Task<bool> SkipUnavailableFarmScheduleStepsAsync(ClientSession session)
    {
        if (session.FarmScheduleCompleted || session.FarmScheduleSteps.Count == 0)
            return false;

        var changed = false;
        while (!IsFarmScheduleDestinationAvailable(session, session.FarmScheduleSteps[session.FarmScheduleIndex].Destination))
        {
            changed = true;
            if (session.FarmScheduleIndex + 1 >= session.FarmScheduleSteps.Count)
            {
                session.FarmScheduleCompleted = true;
                session.FarmScheduleWaitingForWeeklyReset = true;
                session.FarmScheduleRemaining = TimeSpan.Zero;
                break;
            }

            session.FarmScheduleIndex++;
            session.FarmScheduleRemaining = session.FarmScheduleSteps[session.FarmScheduleIndex].Duration;
            session.FarmScheduleLastTickUtc = DateTime.UtcNow;
        }

        if (changed)
        {
            await SaveFarmScheduleStateAsync(session);
            WriteLog(session, session.FarmScheduleCompleted
                ? "Todas as etapas restantes da Agenda estão sem tempo ou sem entradas semanais; usando a T.A até segunda-feira às 04:00."
                : $"Etapa sem tempo semanal ignorada; avançando para {ScheduleDestinationName(session.FarmScheduleSteps[session.FarmScheduleIndex].Destination)}.");
        }

        return changed;
    }

    private static bool IsFarmScheduleDestinationAvailable(ClientSession session, FarmScheduleDestination destination)
    {
        var canPayNewEntry = session.AgendaEntries < session.Options.WeeklyAgendaEntryLimit;
        return destination switch
        {
            FarmScheduleDestination.Abbey =>
                !session.AbbeyTimeExhausted && (session.AbbeyInside || canPayNewEntry),
            FarmScheduleDestination.AnonymousDungeon =>
                !session.AnonymousDungeonExhausted && (session.AnonymousDungeonInside || canPayNewEntry),
            _ => false
        };
    }

    private static TimeSpan CurrentAbbeyUsage(ClientSession session) =>
        session.AbbeyUsed + (session.AbbeyInside && session.AbbeyActiveSinceUtc != default
            ? DateTime.UtcNow - session.AbbeyActiveSinceUtc
            : TimeSpan.Zero);

    private async Task RecordAbbeyExitAsync(ClientSession session)
    {
        if (!session.AbbeyInside)
        {
            return;
        }

        session.AbbeyUsed = CurrentAbbeyUsage(session);
        session.AbbeyInside = false;
        session.AbbeyActiveSinceUtc = default;
        session.AbbeySupplyPurchasePending = true;
        await SaveAbbeyBudgetStateAsync(session);
    }

    private async Task LoadAbbeyBudgetStateAsync(ClientSession session)
    {
        var prefix = $"{SessionSettingPrefix(session)}.abbey.runtime";
        var currentWeek = AbbeyWeekKey(DateTime.Now);
        var storedWeek = await database.GetSettingAsync($"{prefix}.week");
        var isNewWeek = !string.Equals(storedWeek, currentWeek, StringComparison.Ordinal);
        session.AbbeyWeek = currentWeek;

        _ = double.TryParse(
            await database.GetSettingAsync($"{prefix}.usedMinutes"),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var usedMinutes);
        _ = int.TryParse(
            await database.GetSettingAsync($"{prefix}.agendaEntries") ??
            await database.GetSettingAsync($"{prefix}.entries"),
            out var entries);
        session.AbbeyUsed = string.Equals(storedWeek, currentWeek, StringComparison.Ordinal)
            ? TimeSpan.FromMinutes(Math.Max(0, usedMinutes))
            : TimeSpan.Zero;
        session.AgendaEntries = !isNewWeek
            ? Math.Max(0, entries)
            : 0;
        session.AbbeySupplyPurchasePending = bool.TryParse(
            await database.GetSettingAsync($"{prefix}.supplyPurchasePending"),
            out var supplyPurchasePending) && supplyPurchasePending;
        session.AnonymousDungeonExhausted = string.Equals(storedWeek, currentWeek, StringComparison.Ordinal) &&
            bool.TryParse(await database.GetSettingAsync($"{prefix}.anonymousExhausted"), out var anonymousExhausted) &&
            anonymousExhausted;
        session.AbbeyTimeExhausted = !isNewWeek &&
            bool.TryParse(await database.GetSettingAsync($"{prefix}.timeExhausted"), out var abbeyExhausted) && abbeyExhausted;
        if (isNewWeek)
        {
            await ResetFarmScheduleAfterWeeklyWaitAsync(session);
            await SaveAbbeyBudgetStateAsync(session);
        }
    }

    private async Task RefreshAbbeyWeekAsync(ClientSession session)
    {
        var currentWeek = AbbeyWeekKey(DateTime.Now);
        if (string.Equals(session.AbbeyWeek, currentWeek, StringComparison.Ordinal))
        {
            return;
        }

        session.AbbeyWeek = currentWeek;
        session.AbbeyUsed = TimeSpan.Zero;
        session.AbbeyTimeExhausted = false;
        session.AgendaEntries = 0;
        session.AbbeyActiveSinceUtc = session.AbbeyInside ? DateTime.UtcNow : default;
        session.AbbeyTimeLowHits = 0;
        session.AnonymousDungeonExhausted = false;
        session.AnonymousTimeLowHits = 0;
        Interlocked.Exchange(ref session.PendingAbbeyTimeExhausted, 0);
        Interlocked.Exchange(ref session.PendingAnonymousTimeExhausted, 0);
        await ResetFarmScheduleAfterWeeklyWaitAsync(session);
        await SaveAbbeyBudgetStateAsync(session);
        WriteLog(session, "Novo ciclo semanal iniciado: tempo da Abadia/Estreito e limite de entradas da Agenda foram liberados.");
    }

    private async Task ResetFarmScheduleAfterWeeklyWaitAsync(ClientSession session)
    {
        if (!session.FarmScheduleWaitingForWeeklyReset || session.FarmScheduleSteps.Count == 0)
            return;

        session.FarmScheduleWaitingForWeeklyReset = false;
        session.FarmScheduleCompleted = false;
        session.FarmScheduleIndex = 0;
        session.FarmScheduleRemaining = session.FarmScheduleSteps[0].Duration;
        session.FarmScheduleLastTickUtc = DateTime.UtcNow;
        await SaveFarmScheduleStateAsync(session);
        WriteLog(session, "Agenda semanal reativada após o reset de segunda-feira às 04:00.");
    }

    private async Task SaveAbbeyBudgetStateAsync(ClientSession session)
    {
        var prefix = $"{SessionSettingPrefix(session)}.abbey.runtime";
        await database.SaveSettingAsync($"{prefix}.week", session.AbbeyWeek ?? AbbeyWeekKey(DateTime.Now));
        await database.SaveSettingAsync($"{prefix}.usedMinutes", session.AbbeyUsed.TotalMinutes.ToString("F3", CultureInfo.InvariantCulture));
        await database.SaveSettingAsync($"{prefix}.agendaEntries", session.AgendaEntries.ToString(CultureInfo.InvariantCulture));
        await database.SaveSettingAsync($"{prefix}.timeExhausted", session.AbbeyTimeExhausted.ToString());
        await database.SaveSettingAsync(
            $"{prefix}.supplyPurchasePending",
            session.AbbeySupplyPurchasePending.ToString(CultureInfo.InvariantCulture));
        await database.SaveSettingAsync(
            $"{prefix}.anonymousExhausted",
            session.AnonymousDungeonExhausted.ToString(CultureInfo.InvariantCulture));
    }

    private async Task RunCoreAsync(
        IReadOnlyList<ClientSession> sessions,
        SapherasOptions sapheras,
        AntiOverkillOptions antiOverkill,
        DailyRoutineOptions dailyRoutines,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var sapherasSessions = sessions.Where(session => session.Options.UseSapheras).ToArray();
        if (sapherasSessions.Length == 0)
        {
            WriteLog("Sapheras desativada para todos os clientes. Mantendo o farm contínuo nos destinos configurados.");
            foreach (var session in sessions)
            {
                if (await RunStartupRestorationSafelyAsync(session, sapheras, antiOverkill, pause, cancellationToken))
                {
                    continue;
                }
                if (await RecoverOpenRoutinePanelsSafelyAsync(session, dailyRoutines, pause, cancellationToken))
                {
                    continue;
                }
                await TryCollectDueMailSafelyAsync(session, pause, cancellationToken);
                if (await TryStartVisibleDailyCampaignSafelyAsync(session, dailyRoutines, pause, cancellationToken))
                {
                    continue;
                }
                if (await RunDueDailyRoutinesSafelyAsync(session, dailyRoutines, pause, cancellationToken))
                {
                    continue;
                }
                await RunSessionActionSafelyAsync(
                    session,
                    "preparação inicial do farm",
                    () => PrepareClientForFarmAsync(
                        sessions, session, sapheras, antiOverkill, pause, cancellationToken, "contínuo"),
                    cancellationToken);
            }

            await MonitorFarmsAsync(sessions, sapheras, antiOverkill, dailyRoutines, pause, cancellationToken, stopAt: null);
            return;
        }

        var untilSapheras = sapheras.ScheduledAt - DateTime.Now;
        if (untilSapheras > sapheras.DirectSapherasWindow)
        {
            WriteLog($"Sapheras está a {FormatDuration(untilSapheras)}. Preparando os clientes para farmar antes do horário.");
            using var sapherasPriority = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sapherasPriority.CancelAfter(untilSapheras);
            try
            {
                foreach (var session in sessions)
                {
                    if (await RunStartupRestorationSafelyAsync(session, sapheras, antiOverkill, pause, sapherasPriority.Token))
                    {
                        continue;
                    }
                    if (await RecoverOpenRoutinePanelsSafelyAsync(session, dailyRoutines, pause, sapherasPriority.Token))
                    {
                        continue;
                    }
                    await TryCollectDueMailSafelyAsync(session, pause, sapherasPriority.Token);
                    if (await TryStartVisibleDailyCampaignSafelyAsync(session, dailyRoutines, pause, sapherasPriority.Token))
                    {
                        continue;
                    }
                    if (await RunDueDailyRoutinesSafelyAsync(session, dailyRoutines, pause, sapherasPriority.Token))
                    {
                        continue;
                    }
                    await RunSessionActionSafelyAsync(
                        session,
                        "preparação do farm antes de Sapheras",
                        () => PrepareClientForFarmAsync(
                            sessions, session, sapheras, antiOverkill, pause, sapherasPriority.Token, "antes de Sapheras"),
                        sapherasPriority.Token);
                }

                await MonitorFarmsAsync(sessions, sapheras, antiOverkill, dailyRoutines, pause, sapherasPriority.Token, sapheras.ScheduledAt);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && sapherasPriority.IsCancellationRequested)
            {
                WriteLog("PRIORIDADE MÁXIMA: horário de Sapheras alcançado. A ação atual foi interrompida imediatamente.");
                foreach (var session in sessions.Where(session => !session.Options.UseSapheras && !session.InAgenda))
                {
                    session.Audio.Armed = session.NextRecoveryAttemptAt == default && session.IsFarmingTa;
                    WriteLog(session, "Proteção rearmada após a interrupção prioritária de Sapheras.");
                }
            }
        }
        else
        {
            WriteLog($"Sapheras está próxima ({FormatDuration(untilSapheras)}). Entrada direta programada.");
            await WaitForScheduleAsync(sapheras.ScheduledAt, pause, cancellationToken);
        }

        foreach (var session in sapherasSessions)
        {
            var entered = await RunSessionActionSafelyAsync(
                session,
                "entrada prioritária em Sapheras",
                async () =>
                {
                    await EnsureHigherPriorityClientsSafeAsync(sessions, session, cancellationToken);
                    if (session.InAgenda)
                    {
                        await ExitAgendaAsync(session, sapheras, pause, cancellationToken, resumeFarm: false);
                    }

                    session.Audio.Armed = false;
                    session.IsFarmingTa = false;
                    await RecordAbbeyExitAsync(session);
                    session.SafeInRest = false;
                    SetStatus(BotRunState.Running, $"{session.Options.Label}: entrando em Sapheras", "Preparando o cliente");
                    await ActivateGameAsync(session, cancellationToken);
                    var death = await FindDeathOnClientAsync(session, cancellationToken);
                    if (death.Found)
                    {
                        WriteLog(session, "Morte encontrada na virada do horário; restaurando antes da entrada prioritária.");
                        await RestoreDeathResourcesAsync(session, pause, cancellationToken);
                        if (RegisterDeath(session, antiOverkill))
                        {
                            session.PendingAgendaAfterSapheras = true;
                        }
                    }

                    await EnterSapherasAsync(session, sapheras, pause, cancellationToken);
                    session.SafeInRest = true;
                },
                cancellationToken);
            if (!entered)
            {
                continue;
            }
        }

        var finishesAt = DateTime.Now + sapheras.Duration;
        WriteLog($"{sapherasSessions.Length} cliente(s) selecionado(s) entraram em Sapheras. Término previsto: {finishesAt:HH:mm:ss}.");
        await MonitorDuringSapherasAsync(sessions, finishesAt, sapheras, antiOverkill, pause, cancellationToken);

        WriteLog("Tempo de Sapheras concluído. Normalizando o estado dos dois clientes.");
        foreach (var session in sessions.OrderBy(session => session.Options.Priority))
        {
            if (!session.Options.UseSapheras)
            {
                if (!session.InAgenda && !session.IsFarmingTa)
                {
                    WriteLog(session, $"Cliente sem passe não estava no farm; retomando {ConfiguredFarmName(session)}.");
                    await RunSessionActionSafelyAsync(
                        session,
                        "retomada do cliente sem passe após Sapheras",
                        () => EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false),
                        cancellationToken);
                }

                continue;
            }

            if (session.PendingAgendaAfterSapheras)
            {
                session.PendingAgendaAfterSapheras = false;
                session.Deaths.Clear();
                await RunSessionActionSafelyAsync(
                    session,
                    "início da Agenda após Sapheras",
                    () => StartAgendaAsync(session, antiOverkill, pause, cancellationToken),
                    cancellationToken);
            }
            else
            {
                await RunSessionActionSafelyAsync(
                    session,
                    "retomada do farm após Sapheras",
                    () => EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false),
                    cancellationToken);
            }
        }

        var nextSapheras = sapheras with { ScheduledAt = sapheras.ScheduledAt.AddDays(1) };
        WriteLog($"Ciclo diário mantido: próxima Sapheras programada para {nextSapheras.ScheduledAt:dd/MM HH:mm:ss}.");
        await RunCoreAsync(sessions, nextSapheras, antiOverkill, dailyRoutines, pause, cancellationToken);
    }

    private async Task<bool> RunStartupRestorationSafelyAsync(
        ClientSession session,
        SapherasOptions sapheras,
        AntiOverkillOptions antiOverkill,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        try
        {
            // A restauração precede Correio, Diretivas e Diárias: essas rotinas
            // podem assumir o controle do cliente e pular a preparação do farm.
            var death = await FindDeathOnClientAsync(session, cancellationToken);
            if (death.Found)
            {
                WriteLog(session, $"Morte presente na inicialização ({death.Confidence:P0}); restaurando antes das rotinas.");
                await HandleDeathAsync(session, sapheras, antiOverkill, pause, cancellationToken);
                return true;
            }

            var panel = await WaitForRestorationCounterAsync(
                session, TimeSpan.FromSeconds(2), pause, cancellationToken);
            var iconConfirmed = false;
            if (panel.State == RestorationCountState.Unknown)
            {
                for (var sample = 0; sample < 2; sample++)
                {
                    var frame = await CaptureClientFrameAsync(session, cancellationToken);
                    var icon = await recognition.FindAsync("icone_perda_exp", frame, cancellationToken);
                    if (!icon.Found || icon.Confidence < 0.59 || !TombstoneIconAnalyzer.HasRedIcon(frame))
                    {
                        iconConfirmed = false;
                        break;
                    }

                    iconConfirmed = true;
                    if (sample == 0)
                    {
                        await Task.Delay(400, cancellationToken);
                    }
                }
            }

            if (panel.State != RestorationCountState.Unknown || iconConfirmed)
            {
                WriteLog(session, panel.State != RestorationCountState.Unknown
                    ? "Painel de restauração já aberto ao iniciar; resolvendo antes das rotinas."
                    : "Lápide antiga confirmada ao iniciar; resolvendo antes das rotinas.");
                await ActivateGameForEmergencyAsync(session, cancellationToken);
                await RestoreDeathResourcesAsync(session, pause, cancellationToken);
            }

            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            WriteLog(session, $"Falha na recuperação inicial; o monitoramento tentará novamente: {exception.GetBaseException().Message}");
            WritePersistentOnly(session, exception.ToString());
            session.NeedsDeathRestoration = true;
            session.NextRecoveryAttemptAt = DateTime.UtcNow.AddSeconds(10);
            return true;
        }
    }

    private async Task PrepareClientForFarmAsync(
        IReadOnlyList<ClientSession> sessions,
        ClientSession session,
        SapherasOptions sapheras,
        AntiOverkillOptions antiOverkill,
        PauseController pause,
        CancellationToken cancellationToken,
        string context)
    {
        await EnsureHigherPriorityClientsSafeAsync(sessions, session, cancellationToken);
        if (session.InAgenda)
        {
            if (DateTime.Now < session.AgendaUntil)
            {
                session.Audio.Armed = false;
                session.SafeInRest = true;
                WriteLog(session, $"Agenda segura já ativa até {session.AgendaUntil:HH:mm:ss}; mantendo este cliente protegido {context}.");
                return;
            }

            WriteLog(session, "Tempo da Agenda já terminou; saindo e retomando o farm configurado.");
            await ExitAgendaAsync(session, sapheras, pause, cancellationToken, resumeFarm: true);
            return;
        }

        await ActivateGameAsync(session, cancellationToken);

        var currentRest = await FindRestStateAsync(cancellationToken);
        if (currentRest is not null)
        {
            session.SafeInRest = true;
            session.IsFarmingTa = true;
            if (WantsAbbey(session) && await IsAbbeyLocationVisibleAsync(cancellationToken))
            {
                session.AbbeyInside = true;
                session.AbbeyActiveSinceUtc = DateTime.UtcNow;
            }
            if (WantsAnonymousDungeon(session) && await IsAnonymousDungeonLocationVisibleAsync(cancellationToken))
                session.AnonymousDungeonInside = true;
            session.Audio.Armed = true;
            WriteLog(
                session,
                $"Bot iniciado com o farm já em descanso, confirmado por " +
                $"'{RestStateDescription(currentRest.Value.ReferenceId)}' ({currentRest.Value.Result.Confidence:P0}); " +
                $"mantendo o farm atual e seguindo diretamente para o monitoramento {context}.");
            return;
        }

        if (await IsConfiguredTaLocationVisibleAsync(session, cancellationToken))
        {
            WriteLog(session, $"O personagem já está na {TaName(EffectiveTaDestination(session))}; mantendo a área atual sem abrir uma nova entrada.");
            await ResumeCurrentTaWithoutReentryAsync(session, pause, cancellationToken);
            return;
        }

        await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
    }

    private async Task<RecognitionResult> FindDeathOnClientAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            var frame = await CaptureClientFrameAsync(session, cancellationToken);
            return await FindDeathInFrameAsync(frame, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Nunca atribua ao Cliente 2 uma morte vista no Cliente 1 (ou vice-
            // versa). A tela principal só é um fallback seguro quando a janela
            // consultada é, de fato, a janela em primeiro plano.
            if (gameWindows.IsForeground(session.Options.Target))
            {
                return await FindDeathInFrameAsync(capture.CapturePrimaryScreen(), cancellationToken);
            }

            return new RecognitionResult(false, 0, 0, 0);
        }
    }

    private async Task<RecognitionResult> FindDeathInFrameAsync(
        PixelFrame frame,
        CancellationToken cancellationToken)
    {
        // A morte pode aparecer na tela completa ou no descanso. O texto do
        // assassino, o mapa e o fundo variam; só elementos fixos confirmam a
        // tela completa.
        var fullDeath = await FindFullDeathInFrameAsync(frame, cancellationToken);
        if (fullDeath.Found)
        {
            return fullDeath;
        }

        var rest = await recognition.FindAsync("descanso_morte", frame, cancellationToken);

        var best = rest.Confidence > fullDeath.Confidence ? rest : fullDeath;
        var confirmed = fullDeath.Found || rest.Confidence >= RestDeathConfidence;

        return confirmed
            ? new RecognitionResult(true, best.Confidence, best.X, best.Y)
            : new RecognitionResult(false, best.Confidence, best.X, best.Y);
    }

    private async Task<RecognitionResult> FindFullDeathInFrameAsync(
        PixelFrame frame,
        CancellationToken cancellationToken)
    {
        // O botão permanece visível mesmo quando avisos cobrem "Você morreu".
        // Conferi também contra telas normais, mapa, loja e restauração.
        var resurrectButton = await recognition.FindAsync("morte_ressuscitar", frame, cancellationToken);
        if (resurrectButton.Confidence >= FixedDeathElementConfidence)
        {
            return resurrectButton with { Found = true };
        }

        var deathTitle = await recognition.FindAsync("morte_titulo", frame, cancellationToken);
        if (deathTitle.Confidence >= FixedDeathElementConfidence)
        {
            return deathTitle with { Found = true };
        }

        var primary = await recognition.FindAsync("morte_confirmada", frame, cancellationToken);
        var clientTwo = await recognition.FindAsync("morte_confirmada_ta2", frame, cancellationToken);
        var best = new[] { resurrectButton, deathTitle, primary, clientTwo }
            .MaxBy(result => result.Confidence)!;
        var confirmed = primary.Confidence >= FullDeathConfidence ||
                        clientTwo.Confidence >= FullDeathConfidence;
        return confirmed
            ? new RecognitionResult(true, best.Confidence, best.X, best.Y)
            : new RecognitionResult(false, best.Confidence, best.X, best.Y);
    }

    private async Task<RecognitionResult> FindFullDeathOnClientAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            var frame = await CaptureClientFrameAsync(session, cancellationToken);
            return await FindFullDeathInFrameAsync(frame, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (gameWindows.IsForeground(session.Options.Target))
            {
                return await FindFullDeathInFrameAsync(capture.CapturePrimaryScreen(), cancellationToken);
            }

            throw new InvalidOperationException(
                $"{session.Options.Label}: a tela de morte não pôde ser observada com segurança.",
                exception);
        }
    }

    private async Task<RecognitionResult> FindReferenceOnClientAsync(
        ClientSession session,
        string referenceId,
        CancellationToken cancellationToken,
        bool requireObservable = false)
    {
        try
        {
            var frame = await CaptureClientFrameAsync(session, cancellationToken);
            return await recognition.FindAsync(referenceId, frame, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (gameWindows.IsForeground(session.Options.Target))
            {
                return await recognition.FindAsync(referenceId, cancellationToken);
            }

            if (requireObservable)
            {
                throw new InvalidOperationException(
                    $"{session.Options.Label}: a janela não entregou imagem válida para verificar '{referenceId}'.",
                    exception);
            }

            return new RecognitionResult(false, 0, 0, 0);
        }
    }

    private async Task<bool> WaitForReferenceOnClientAsync(
        ClientSession session,
        string referenceId,
        TimeSpan timeout,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            if ((await FindReferenceOnClientAsync(
                    session, referenceId, cancellationToken, requireObservable: true)).Found)
            {
                return true;
            }

            await Task.Delay(350, cancellationToken);
        }

        return false;
    }

    private static async Task<PixelFrame> CaptureClientFrameAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        await session.WindowCaptureGate.WaitAsync(cancellationToken);
        try
        {
            var windowCapture = session.WindowCapture ??
                throw new InvalidOperationException("A captura visual da janela não está conectada.");
            return await windowCapture.CaptureAsync(cancellationToken);
        }
        finally
        {
            session.WindowCaptureGate.Release();
        }
    }

    private async Task MonitorDuringSapherasAsync(
        IReadOnlyList<ClientSession> sessions,
        DateTime finishesAt,
        SapherasOptions sapheras,
        AntiOverkillOptions antiOverkill,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var protectedSessions = sessions
            .OrderBy(session => session.Options.Priority)
            .ToArray();
        foreach (var session in protectedSessions)
        {
            session.Audio.Armed = !session.InAgenda && session.NextRecoveryAttemptAt == default;
            WriteLog(session, session.InAgenda
                ? "Agenda segura permanece sob controle durante a janela de Sapheras."
                : session.Options.UseSapheras
                    ? "Farm de Sapheras com proteção de HP ativa."
                    : "Sem passe de Sapheras: mantendo o farm da T.A e a proteção de HP ativa.");
        }

        try
        {
            while (DateTime.Now < finishesAt)
            {
                await CheckpointAsync(pause, cancellationToken);
                foreach (var session in protectedSessions)
                {
                    if (session.InAgenda)
                    {
                        session.Audio.Armed = false;
                        if (DateTime.Now >= session.AgendaUntil)
                        {
                            WriteLog(session, "Tempo seguro da Agenda concluído durante a janela de Sapheras.");
                            await ExitAgendaAsync(session, sapheras, pause, cancellationToken, resumeFarm: true);
                            break;
                        }

                        continue;
                    }

                    // Morte sempre tem precedência também durante Sapheras.
                    // Antes, uma retomada pendente fazia o loop ignorar este
                    // cliente até o fim da masmorra.
                    if (!session.HandlingDeath)
                    {
                        var recoveryIsDue = session.NextRecoveryAttemptAt != default &&
                                            DateTime.UtcNow >= session.NextRecoveryAttemptAt;
                        if (Volatile.Read(ref session.PendingVisualDeath) == 0 && recoveryIsDue)
                        {
                            var visibleDeath = await FindDeathOnClientAsync(session, cancellationToken);
                            if (visibleDeath.Found)
                            {
                                Interlocked.Exchange(ref session.PendingVisualDeath, 1);
                                WriteLog(session, $"Morte encontrada antes da retomada durante Sapheras ({visibleDeath.Confidence:P0}).");
                            }
                        }

                        if (Interlocked.Exchange(ref session.PendingVisualDeath, 0) != 0)
                        {
                            session.NextRecoveryAttemptAt = default;
                            WriteLog(session, "Atendendo morte detectada pela vigilância visual durante Sapheras.");
                            if (session.Options.UseSapheras)
                            {
                                await RunSessionActionSafelyAsync(
                                    session,
                                    "restauração da morte em Sapheras",
                                    () => RecoverDeathDuringSapherasAsync(session, sapheras, antiOverkill, finishesAt, pause, cancellationToken),
                                    cancellationToken);
                            }
                            else
                            {
                                await RunSessionActionSafelyAsync(
                                    session,
                                    "restauração da morte na T.A durante Sapheras",
                                    () => HandleDeathAsync(session, sapheras, antiOverkill, pause, cancellationToken),
                                    cancellationToken);
                            }

                            break;
                        }
                    }

                    if (session.NextRecoveryAttemptAt != default &&
                        DateTime.UtcNow >= session.NextRecoveryAttemptAt &&
                        !session.HandlingDeath)
                    {
                        session.NextRecoveryAttemptAt = default;
                        var resumed = await RunRecoveryActionSafelyAsync(
                            session,
                            session.Options.UseSapheras
                                ? "retomada de Sapheras após falha"
                                : "retomada da T.A durante Sapheras",
                            async actionToken =>
                            {
                                if (session.NeedsDeathRestoration)
                                {
                                    WriteLog(session, "Restauração pendente: concluindo a lápide antes de reiniciar a rota.");
                                    await ActivateGameForEmergencyAsync(session, actionToken);
                                    await RestoreDeathResourcesAsync(session, pause, actionToken);
                                }

                                if (session.AwaitingHuntActivationAtSpot &&
                                    !session.RequiresHardFlowReset)
                                {
                                    await ResumeHuntAtCurrentSpotAsync(
                                        session,
                                        !session.Options.UseSapheras,
                                        pause,
                                        actionToken);
                                    return;
                                }

                                if (session.RequiresHardFlowReset &&
                                    (!session.AbbeyInside || session.ConsecutiveRecoveryFailures >= 3))
                                {
                                    await ResetStalledClientRouteAsync(session, sapheras, pause, actionToken);
                                }

                                if (session.Options.UseSapheras)
                                {
                                    await ActivateGameAsync(session, actionToken);
                                    session.IsFarmingTa = false;
                                    session.SafeInRest = false;
                                    await EnterSapherasAsync(session, sapheras, pause, actionToken);
                                    session.SafeInRest = true;
                                }
                                else
                                {
                                    await EnterConfiguredFarmAsync(session, pause, actionToken, isEmergency: true);
                                }
                            },
                            cancellationToken);
                        if (!resumed)
                        {
                            break;
                        }

                        session.Audio.Armed = true;
                    }

                    if (session.NextRecoveryAttemptAt != default)
                    {
                        continue;
                    }

                    if (!session.Audio.IsHealthy && !session.AudioFailureLogged)
                    {
                        session.AudioFailureLogged = true;
                        WriteLog(session, "Áudio de HP indisponível; tentando reconectar. A barra visual mantém proteção de contingência.");
                    }

                    if (session.Audio.TryConsumeAlert(out var confidence))
                    {
                        WriteLog(session, $"HP baixo durante Sapheras ({confidence:P0}). Acionando proteção.");
                        Interlocked.Exchange(ref session.PendingVisualLowHp, 0);
                        session.Audio.Armed = false;
                        bool protectionCompleted;
                        if (session.Options.UseSapheras)
                        {
                            protectionCompleted = await RunSessionActionSafelyAsync(
                                session,
                                "TP de áudio em Sapheras",
                                () => EmergencyReturnToSapherasAsync(session, sapheras, antiOverkill, finishesAt, pause, cancellationToken),
                                cancellationToken);
                        }
                        else
                        {
                            protectionCompleted = await RunSessionActionSafelyAsync(
                                session,
                                "TP de áudio na T.A durante Sapheras",
                                () => EmergencyReturnAsync(session, sapheras, antiOverkill, pause, cancellationToken),
                                cancellationToken);
                        }

                        if (protectionCompleted)
                        {
                            session.Audio.Armed = session.NextRecoveryAttemptAt == default &&
                                                  (session.IsFarmingTa ||
                                                   (session.Options.UseSapheras && session.SafeInRest));
                        }

                        break;
                    }

                    if (Interlocked.Exchange(ref session.PendingVisualLowHp, 0) != 0)
                    {
                        session.Audio.Armed = false;
                        if (session.Options.UseSapheras)
                        {
                            await RunSessionActionSafelyAsync(
                                session,
                                "retomada de Sapheras após TP em segundo plano",
                                () => EmergencyReturnToSapherasAsync(session, sapheras, antiOverkill, finishesAt, pause, cancellationToken),
                                cancellationToken);
                        }
                        else
                        {
                            await RunSessionActionSafelyAsync(
                                session,
                                "retomada da T.A após TP em segundo plano",
                                () => RecoverAfterBackgroundEmergencyAsync(session, sapheras, antiOverkill, pause, cancellationToken),
                                cancellationToken);
                        }

                        break;
                    }

                    continue;
                }

                SetStatus(BotRunState.Running, "Farmando em Sapheras", $"Tempo restante: {FormatDuration(finishesAt - DateTime.Now)}");
                await Task.Delay(100, cancellationToken);
            }
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
            {
                foreach (var session in protectedSessions)
                {
                    session.Audio.Armed = false;
                }
            }
        }
    }

    private async Task EmergencyReturnToSapherasAsync(
        ClientSession session,
        SapherasOptions sapheras,
        AntiOverkillOptions antiOverkill,
        DateTime finishesAt,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        _ = session.Audio.TryConsumeAlert(out _);
        session.SafeInRest = false;
        await ActivateGameForEmergencyAsync(session, cancellationToken);
        if ((await FindDeathOnClientAsync(session, cancellationToken)).Found)
        {
            await RecoverDeathDuringSapherasAsync(session, sapheras, antiOverkill, finishesAt, pause, cancellationToken);
            return;
        }

        WriteLog(session, $"Proteção em Sapheras: enviando um TP {sapheras.EmergencyTeleportKeyName}.");
        await input.PressEmergencyKeyAsync(sapheras.EmergencyTeleportVirtualKey, cancellationToken);

        if (await WaitForDeathAfterEmergencyAsync(
                session,
                TimeSpan.FromSeconds(5),
                cancellationToken) is not null)
        {
            await RecoverDeathDuringSapherasAsync(session, sapheras, antiOverkill, finishesAt, pause, cancellationToken);
            return;
        }

        if (DateTime.Now < finishesAt - TimeSpan.FromMinutes(1))
        {
            WriteLog(
                session,
                "TP enviado sem tela de morte; reentrando em Sapheras. " +
                "A retomada só será concluída após a Atalaia Erodida ser confirmada visualmente.");
            await EnterSapherasAsync(session, sapheras, pause, cancellationToken);
            session.SafeInRest = true;
        }
        else
        {
            session.SafeInRest = false;
            WriteLog(session, "Sapheras termina em menos de 1 minuto; permanecendo na cidade para o retorno ao ciclo normal.");
        }
    }

    private async Task RecoverDeathDuringSapherasAsync(
        ClientSession session,
        SapherasOptions sapheras,
        AntiOverkillOptions antiOverkill,
        DateTime finishesAt,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        if (session.HandlingDeath)
        {
            return;
        }

        session.HandlingDeath = true;
        session.Audio.Armed = false;
        session.AwaitingHuntActivationAtSpot = false;
        _ = session.Audio.TryConsumeAlert(out _);
        Interlocked.Exchange(ref session.PendingVisualDeath, 0);
        try
        {
            await ClearRestorationIconAbsenceAsync(session);
            SetStatus(BotRunState.Running, $"{session.Options.Label}: morte em Sapheras", "Restaurando antes de retomar a prioridade");
            await ActivateGameAsync(session, cancellationToken);
            await RestoreDeathResourcesAsync(session, pause, cancellationToken);
            if (RegisterDeath(session, antiOverkill))
            {
                session.PendingAgendaAfterSapheras = true;
                WriteLog(session, "Limite do Anti Over Kill atingido; a Agenda começará assim que Sapheras terminar.");
            }

            if (DateTime.Now < finishesAt - TimeSpan.FromMinutes(1))
            {
                await EnterSapherasAsync(session, sapheras, pause, cancellationToken);
                session.SafeInRest = true;
            }
            else
            {
                session.SafeInRest = true;
            }
        }
        finally
        {
            session.HandlingDeath = false;
            if (!session.InAgenda && session.SafeInRest)
            {
                session.Audio.Armed = true;
            }
        }
    }

    private async Task WaitForScheduleAsync(DateTime scheduledAt, PauseController pause, CancellationToken cancellationToken)
    {
        WriteLog($"Sessão programada para {scheduledAt:HH:mm:ss}.");
        while (DateTime.Now < scheduledAt)
        {
            await CheckpointAsync(pause, cancellationToken);
            SetStatus(BotRunState.Waiting, "Aguardando Sapheras", $"Início em {FormatDuration(scheduledAt - DateTime.Now)}");
            await Task.Delay(400, cancellationToken);
        }
    }

    private async Task EnterSapherasAsync(
        ClientSession session,
        SapherasOptions options,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        session.AwaitingHuntActivationAtSpot = false;
        await ExitRestIfNeededAsync(session, pause, cancellationToken);
        await OpenDungeonMenuAsync(session, pause, cancellationToken);
        await input.ClickAsync(1744, 273, cancellationToken);
        await WaitForReferenceAsync("tela_masmorras", "página Masmorra", TimeSpan.FromSeconds(12), pause, cancellationToken);
        await input.ClickAsync(245, 766, cancellationToken);
        await input.ClickAsync(1791, 989, cancellationToken);
        await WaitForReferenceAsync("confirmar_sepheras", "confirmação de Ruínas de Sapheras", TimeSpan.FromSeconds(10), pause, cancellationToken);
        await input.PressKeyAsync(KeyY, cancellationToken: cancellationToken);
        await WaitForReferenceAsync("atalaia_erodida", "mapa Atalaia Erodida", TimeSpan.FromSeconds(50), pause, cancellationToken);

        WriteLog(session, "Atalaia Erodida reconhecida; aguardando o mapa carregar por completo.");
        await ActionDelayAsync(cancellationToken, 5500, 7000);
        await input.HoldKeyAsync(KeyW, TimeSpan.FromMilliseconds(3500), cancellationToken);

        var teleportCount = _random.Next(1, 6);
        WriteLog(session, $"Usando teleporte aleatório {teleportCount} vez(es) pela tecla {options.TeleportKeyName}.");
        for (var index = 0; index < teleportCount; index++)
        {
            await input.PressKeyAsync(options.TeleportVirtualKey, cancellationToken: cancellationToken);
        }

        session.AwaitingHuntActivationAtSpot = true;
        await StartAutomaticHuntAsync(session, pause, cancellationToken);
        session.ConsecutiveRecoveryFailures = 0;
        session.RequiresHardFlowReset = false;
    }

    private async Task OpenDungeonMenuAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await TryDismissAgendaAsync(session, cancellationToken);
            var menuButton = await recognition.FindAsync("menu_masmorra", cancellationToken);
            if (menuButton.Found)
            {
                WriteRecognition(session, "Botão Masm.", menuButton);
                return;
            }

            var overlayOpen = (await recognition.FindAsync("mapa_aberto", cancellationToken)).Found ||
                              (await recognition.FindAsync("loja_artigos", cancellationToken)).Found ||
                              (await recognition.FindAsync("seletor_ta", cancellationToken)).Found;
            if (overlayOpen)
            {
                WriteLog(session, "Sapheras prioritária: fechando a tela atual com Esc.");
                await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
            }

            WriteLog(session, $"Sapheras prioritária: abrindo o menu lateral com = — tentativa {attempt}/3.");
            await input.PressKeyAsync(KeyEquals, cancellationToken: cancellationToken,
                cooldown: TimeSpan.FromMilliseconds(80));
            if (await WaitForReferenceToAppearAsync("menu_masmorra", TimeSpan.FromSeconds(8), pause, cancellationToken))
            {
                return;
            }
        }

        var diagnostic = await recognition.SaveDiagnosticAsync("prioridade_sapheras_menu");
        throw new TimeoutException($"{session.Options.Label}: não foi possível abrir Masmorras para a entrada prioritária em Sapheras. Diagnóstico: {diagnostic}");
    }

    private async Task EnterConfiguredFarmAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken,
        bool isEmergency)
    {
        await RefreshAbbeyWeekAsync(session);
        await SkipUnavailableFarmScheduleStepsAsync(session);

        if (session.InDailyCampaign)
        {
            var dailyResume = await ResumeDailyCampaignAsync(session, pause, cancellationToken);
            if (dailyResume == DailyResumeResult.Started)
            {
                return;
            }

            if (dailyResume == DailyResumeResult.NoMission)
            {
                await MarkDailyCampaignCompletedAsync(
                    session,
                    "Nenhuma missão roxa permanece na lista; Diárias concluídas. Retomando o farm configurado.");
            }
            else
            {
                session.DailyNeedsTeleport = true;
                session.NextDailyMissionCheckAt = DateTime.UtcNow.AddSeconds(15);
                WriteLog(session, "A retomada da Diária ainda não foi confirmada; mantendo o ciclo pendente e reavaliando em 15 segundos.");
                return;
            }
        }

        if (WantsAbbey(session) && AbbeyIsAvailable(session) && session.AbbeyEntryMayHaveBeenCharged)
        {
            await ActivateGameAsync(session, cancellationToken);
            if (await IsAbbeyLocationVisibleAsync(cancellationToken))
            {
                session.AbbeyEntryMayHaveBeenCharged = false;
                session.AbbeyInside = true;
                await RecordAgendaPaidEntryAsync(session, "Abadia");
                session.AbbeyActiveSinceUtc = DateTime.UtcNow;
                await SaveAbbeyBudgetStateAsync(session);
                WriteLog(session, "Chegada à Abadia reconhecida após atraso; retomando sem pagar outra entrada.");
            }
        }

        if (WantsAbbey(session) && AbbeyIsAvailable(session) && session.AbbeyInside)
        {
            await ActivateGameAsync(session, cancellationToken);
            if (await IsMapOpenAsync(session, cancellationToken) ||
                await IsAbbeyLocationVisibleAsync(cancellationToken))
            {
                WriteLog(session, "Abadia atual ainda acessível; retomando no mapa sem pagar outra entrada.");
                await TravelToAbbeySpotAsync(session, pause, cancellationToken);
                return;
            }

            await RecordAbbeyExitAsync(session);
            WriteLog(session, "A localização da Abadia não está visível; verificando o orçamento antes de reentrar.");
        }

        if (WantsAbbey(session) && AbbeyIsAvailable(session) && !session.AbbeyEntryMayHaveBeenCharged &&
            CanPayAgendaEntry(session))
        {
            await EnterAbbeyAndStartFarmAsync(session, pause, cancellationToken);
            return;
        }

        if (WantsAbbey(session))
        {
            WriteLog(session, session.AbbeyEntryMayHaveBeenCharged
                ? "Entrada da Abadia não pôde ser comprovada após Y; evitando nova cobrança e usando a T.A configurada."
                : $"Limite semanal da Agenda atingido ({session.AgendaEntries}/{session.Options.WeeklyAgendaEntryLimit}); usando a T.A configurada.");
        }


        if (WantsAnonymousDungeon(session))
        {
            if (session.AnonymousDungeonEntryMayHaveBeenCharged &&
                await IsAnonymousDungeonLocationVisibleAsync(cancellationToken))
            {
                session.AnonymousDungeonEntryMayHaveBeenCharged = false;
                session.AnonymousDungeonInside = true;
                await RecordAgendaPaidEntryAsync(session, "Estreito de Tenerys");
                WriteLog(session, "Chegada atrasada ao Estreito de Tenerys reconhecida; retomando sem pagar outra entrada.");
            }

            if (session.AnonymousDungeonInside && await IsAnonymousDungeonLocationVisibleAsync(cancellationToken))
            {
                WriteLog(session, "Estreito de Tenerys ainda acessível; retomando sem pagar outra entrada.");
                await TravelToAnonymousDungeonSpotAsync(session, pause, cancellationToken);
                return;
            }

            session.AnonymousDungeonInside = false;
            if (!session.AnonymousDungeonEntryMayHaveBeenCharged && CanPayAgendaEntry(session))
            {
                await EnterAnonymousDungeonAndStartFarmAsync(session, pause, cancellationToken);
                return;
            }

            WriteLog(session, "A entrada do Estreito de Tenerys pode ter sido cobrada; evitando nova cobrança e usando a T.A configurada.");
        }

        await SkipUnavailableFarmScheduleStepsAsync(session);
        session.AnonymousDungeonInside = false;
        await EnterTaAndStartFarmAsync(session, pause, cancellationToken, isEmergency);
    }

    private async Task EnterAnonymousDungeonAndStartFarmAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var step = CurrentFarmScheduleStep(session) ?? throw new InvalidOperationException("Etapa do Estreito de Tenerys ausente.");
        session.Audio.Armed = false;
        session.SafeInRest = false;
        session.IsFarmingTa = false;
        SetStatus(BotRunState.Running, $"{session.Options.Label}: entrando no Estreito de Tenerys", $"Selecionando nível {step.AnonymousDungeonLevel}");
        await ActivateGameAsync(session, cancellationToken);
        await AbortWorkflowIfDeathDetectedAsync(session, "antes de entrar no Estreito de Tenerys", cancellationToken);
        await ExitRestIfNeededAsync(session, pause, cancellationToken);
        await OpenDungeonMenuAsync(session, pause, cancellationToken);
        await input.MoveAndClickAsync(1740, 271, TimeSpan.FromMilliseconds(220), cancellationToken, cooldown: TimeSpan.FromMilliseconds(60));
        await WaitForReferenceAsync("tela_masmorras", "página Masmorra", TimeSpan.FromSeconds(18), pause, cancellationToken);
        await input.MoveAndClickAsync(720, 140, TimeSpan.FromMilliseconds(180), cancellationToken, cooldown: TimeSpan.FromMilliseconds(50));
        await WaitForReferenceAsync("anonymous_epic_tab", "aba Épica", TimeSpan.FromSeconds(15), pause, cancellationToken);
        await input.MoveAndClickAsync(239, 488, TimeSpan.FromMilliseconds(180), cancellationToken, cooldown: TimeSpan.FromMilliseconds(50));
        await WaitForReferenceAsync("anonymous_level_panel", "níveis do Estreito de Tenerys", TimeSpan.FromSeconds(15), pause, cancellationToken);

        var first = await _abbeyTimeReader.ReadEpicMenuAsync(await CaptureClientFrameAsync(session, cancellationToken), cancellationToken);
        if (first.Remaining is { } firstTime && firstTime == TimeSpan.Zero)
        {
            await Task.Delay(300, cancellationToken);
            var second = await _abbeyTimeReader.ReadEpicMenuAsync(await CaptureClientFrameAsync(session, cancellationToken), cancellationToken);
            if (second.Remaining is { } secondTime && secondTime == TimeSpan.Zero)
            {
                session.AnonymousDungeonExhausted = true;
                await SaveAbbeyBudgetStateAsync(session);
                await SkipUnavailableFarmScheduleStepsAsync(session);
                WriteLog(session, "O cartão confirmou tempo zero duas vezes; Estreito bloqueado até segunda-feira às 04:00.");
                await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
                await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
                return;
            }
        }

        var levelPoint = step.AnonymousDungeonLevel switch
        {
            86 => (1716, 237),
            97 => (1643, 310),
            110 => (1645, 381),
            _ => throw new InvalidOperationException("Nível do Estreito de Tenerys inválido.")
        };
        await input.MoveAndClickAsync(levelPoint.Item1, levelPoint.Item2, TimeSpan.FromMilliseconds(180), cancellationToken, cooldown: TimeSpan.FromMilliseconds(50));
        await Task.Delay(180, cancellationToken);
        await input.MoveAndClickAsync(1798, 992, TimeSpan.FromMilliseconds(200), cancellationToken, cooldown: TimeSpan.FromMilliseconds(50));
        await WaitForReferenceAsync("anonymous_entry_confirmation", "confirmação de entrada do Estreito de Tenerys", TimeSpan.FromSeconds(12), pause, cancellationToken);
        session.AnonymousDungeonEntryMayHaveBeenCharged = true;
        await input.PressKeyAsync(KeyY, cancellationToken: cancellationToken);
        await WaitForReferenceAsync("anonymous_arrival", "chegada ao Estreito de Tenerys", TimeSpan.FromSeconds(70), pause, cancellationToken);
        session.AnonymousDungeonEntryMayHaveBeenCharged = false;
        session.AnonymousDungeonInside = true;
        await RecordAgendaPaidEntryAsync(session, "Estreito de Tenerys");
        WriteLog(session, $"Entrada no Estreito de Tenerys nível {step.AnonymousDungeonLevel} comprovada.");
        await TravelToAnonymousDungeonSpotAsync(session, pause, cancellationToken);
    }

    private async Task<bool> IsAnonymousDungeonLocationVisibleAsync(CancellationToken cancellationToken) =>
        (await recognition.FindAsync("anonymous_arrival", cancellationToken)).Found ||
        (await recognition.FindAsync("anonymous_map", cancellationToken)).Found;

    private async Task TravelToAnonymousDungeonSpotAsync(ClientSession session, PauseController pause, CancellationToken cancellationToken)
    {
        if (!(await recognition.FindAsync("anonymous_map", cancellationToken)).Found)
        {
            await input.PressKeyAsync(KeyM, cancellationToken: cancellationToken);
            await WaitForReferenceAsync("anonymous_map", "mapa do Estreito de Tenerys", TimeSpan.FromSeconds(18), pause, cancellationToken);
        }

        var start = ChooseNextSpot(session, -3, AnonymousDungeonSpots.Length);
        for (var offset = 0; offset < AnonymousDungeonSpots.Length; offset++)
        {
            var point = AnonymousDungeonSpots[(start + offset) % AnonymousDungeonSpots.Length];
            await input.MoveAndClickAsync(point.X, point.Y, TimeSpan.FromMilliseconds(180), cancellationToken, cooldown: TimeSpan.FromMilliseconds(50));
            await Task.Delay(140, cancellationToken);
            if ((await recognition.FindAsync("anonymous_invalid_point", cancellationToken)).Found)
                continue;

            var go = await WaitForAnyReferenceInRegionAsync(
                ["anonymous_go", "botao_ir", "botao_ir_legado"],
                Math.Max(0, point.X - 180), Math.Max(0, point.Y - 130), 390, 220,
                TimeSpan.FromSeconds(2), pause, cancellationToken);
            if (go is null)
                continue;

            await input.MoveAndClickAsync(go.X, go.Y, TimeSpan.FromMilliseconds(160), cancellationToken, cooldown: TimeSpan.FromMilliseconds(50));
            await CloseMapAfterGoAsync(session, pause, cancellationToken);
            await OpenRestForTravelAsync(session, pause, cancellationToken);
            await WaitForFarmArrivalAsync(session, pause, cancellationToken, "Estreito de Tenerys");
            session.AwaitingHuntActivationAtSpot = true;
            await StartAutomaticHuntAsync(session, pause, cancellationToken);
            session.IsFarmingTa = true;
            session.SafeInRest = true;
            session.Audio.Armed = true;
            WriteLog(session, $"Farm iniciado no Estreito de Tenerys pelo ponto ({point.X}, {point.Y}).");
            return;
        }

        var diagnostic = await recognition.SaveDiagnosticAsync($"tenerys_spot_{session.Options.Priority}");
        throw new TimeoutException($"{session.Options.Label}: nenhum ponto válido do Estreito de Tenerys confirmou o botão Ir. Diagnóstico: {diagnostic}");
    }

    private async Task EnterAbbeyAndStartFarmAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        if (session.AbbeySupplyPurchasePending)
        {
            await BuySuppliesInCurrentCityAsync(session, pause, cancellationToken);
            session.AbbeySupplyPurchasePending = false;
            await SaveAbbeyBudgetStateAsync(session);
        }

        session.Audio.Armed = false;
        session.SafeInRest = false;
        session.IsFarmingTa = false;
        session.AwaitingHuntActivationAtSpot = false;
        SetStatus(BotRunState.Running, $"{session.Options.Label}: entrando na Abadia", "Confirmando cada etapa antes da entrada paga");
        await ActivateGameAsync(session, cancellationToken);
        await AbortWorkflowIfDeathDetectedAsync(session, "antes de entrar na Abadia", cancellationToken);
        await ExitRestIfNeededAsync(session, pause, cancellationToken);
        await OpenDungeonMenuAsync(session, pause, cancellationToken);
        await input.MoveAndClickAsync(1740, 271, TimeSpan.FromMilliseconds(360), cancellationToken,
            cooldown: TimeSpan.FromMilliseconds(80));
        await WaitForReferenceAsync("tela_masmorras", "página Masmorra", TimeSpan.FromSeconds(15), pause, cancellationToken);
        await input.MoveAndClickAsync(330, 150, TimeSpan.FromMilliseconds(360), cancellationToken,
            cooldown: TimeSpan.FromMilliseconds(80));
        await WaitForReferenceAsync("abadia_especial", "aba Especial da Masmorra", TimeSpan.FromSeconds(15), pause, cancellationToken);
        await WaitForReferenceAsync("abadia_cartao", "cartão Abadia da Lembrança", TimeSpan.FromSeconds(10), pause, cancellationToken);
        var firstRemaining = await _abbeyTimeReader.ReadMenuAsync(
            await CaptureClientFrameAsync(session, cancellationToken), cancellationToken);
        if (firstRemaining.Remaining is { } first && first == TimeSpan.Zero)
        {
            await Task.Delay(350, cancellationToken);
            var secondRemaining = await _abbeyTimeReader.ReadMenuAsync(
                await CaptureClientFrameAsync(session, cancellationToken), cancellationToken);
            if (secondRemaining.Remaining is { } second && second == TimeSpan.Zero)
            {
                session.AbbeyUsed = TimeSpan.FromHours(10);
                session.AbbeyTimeExhausted = true;
                await SaveAbbeyBudgetStateAsync(session);
                await SkipUnavailableFarmScheduleStepsAsync(session);
                WriteLog(session, "O cartão da Abadia confirmou tempo zero duas vezes; bloqueada até segunda-feira às 04:00.");
                await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
                await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
                return;
            }
        }
        await input.MoveAndClickAsync(257, 711, TimeSpan.FromMilliseconds(360), cancellationToken);
        await CheckpointAsync(pause, cancellationToken);
        await input.MoveAndClickAsync(1794, 988, TimeSpan.FromMilliseconds(470), cancellationToken);
        await WaitForReferenceAsync("abadia_confirmacao", "confirmação de entrada na Abadia da Lembrança", TimeSpan.FromSeconds(12), pause, cancellationToken);

        // Depois de Y uma entrada pode ter sido cobrada, mesmo se a captura falhar.
        // Nunca tentar comprar de novo sem ter comprovado a chegada.
        session.AbbeyEntryMayHaveBeenCharged = true;
        await input.PressKeyAsync(KeyY, cancellationToken: cancellationToken);
        await WaitForAbbeyArrivalAsync(session, pause, cancellationToken);
        session.AbbeyEntryMayHaveBeenCharged = false;
        session.AbbeyInside = true;
        session.AbbeyActiveSinceUtc = DateTime.UtcNow;
        await RecordAgendaPaidEntryAsync(session, "Abadia");
        await SaveAbbeyBudgetStateAsync(session);
        await TravelToAbbeySpotAsync(session, pause, cancellationToken);
    }

    private async Task WaitForAbbeyArrivalAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(70);
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            foreach (var (reference, name) in new[]
            {
                ("abadia_chegada_silencio", "Ambão do Silêncio"),
                ("abadia_chegada_apreciacao", "Ambão da Apreciação")
            })
            {
                var result = await recognition.FindAsync(reference, cancellationToken);
                if (result.Found)
                {
                    WriteLog(session, $"Chegada à Abadia confirmada por {name} ({result.Confidence:P0}).");
                    return;
                }
            }

            await Task.Delay(180, cancellationToken);
        }

        var diagnostic = await recognition.SaveDiagnosticAsync($"abadia_chegada_{session.Options.Priority}");
        throw new TimeoutException($"{session.Options.Label}: entrada paga enviada, mas não foi possível confirmar um dos dois pontos de chegada. Nenhuma nova entrada será tentada automaticamente. Diagnóstico: {diagnostic}");
    }

    private async Task<bool> IsAbbeyLocationVisibleAsync(CancellationToken cancellationToken) =>
        (await recognition.FindAsync("abadia_chegada_silencio", cancellationToken)).Found ||
        (await recognition.FindAsync("abadia_chegada_apreciacao", cancellationToken)).Found;

    private async Task TravelToAbbeySpotAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        SetStatus(BotRunState.Running, $"{session.Options.Label}: indo ao spot da Abadia", "Abrindo mapa sem Favoritos");
        if (!await IsMapOpenAsync(session, cancellationToken))
        {
            await input.PressKeyAsync(KeyM, cancellationToken: cancellationToken);
            await WaitForReferenceAsync("mapa_abadia", "mapa da Abadia", TimeSpan.FromSeconds(15), pause, cancellationToken);
        }
        var coordinate = session.Options.AbbeyCustomFarmCoordinate;
        var point = coordinate is null
            ? AbbeySpots[ChooseNextSpot(session, -2, AbbeySpots.Length)]
            : gameWindows.MapReferencePoint(session.Options.Target, coordinate.X, coordinate.Y);
        WriteLog(session, coordinate is null
            ? $"Selecionando um dos quatro pontos da Abadia: ({point.X}, {point.Y})."
            : $"Usando ponto personalizado da Abadia: ({point.X}, {point.Y}).");

        string[] goReferences = ["botao_ir", "botao_ir_ta2", "botao_ir_legado"];
        var searchX = Math.Max(0, point.X - 220);
        var searchY = Math.Max(0, point.Y - 210);
        RecognitionResult? goButton = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await input.MoveAndClickAsync(point.X, point.Y, TimeSpan.FromMilliseconds(340), cancellationToken);
            goButton = await WaitForAnyReferenceInRegionAsync(
                goReferences, searchX, searchY, 470, 230,
                TimeSpan.FromSeconds(7), pause, cancellationToken);
            if (goButton is not null)
            {
                break;
            }
        }

        if (goButton is null)
        {
            var diagnostic = await recognition.SaveDiagnosticAsync($"abadia_botao_ir_{session.Options.Priority}");
            throw new TimeoutException($"{session.Options.Label}: botão Ir não confirmado no mapa da Abadia; movimento não iniciado. Diagnóstico: {diagnostic}");
        }

        await ClickGoButtonWithConfirmationAsync(
            session, goReferences, searchX, searchY, goButton.X, goButton.Y, pause, cancellationToken);
        await CloseMapAfterGoAsync(session, pause, cancellationToken);
        await OpenRestForTravelAsync(session, pause, cancellationToken);
        await WaitForFarmArrivalAsync(session, pause, cancellationToken, "Abadia");
        session.AwaitingHuntActivationAtSpot = true;
        await StartAutomaticHuntAsync(session, pause, cancellationToken);
        session.IsFarmingTa = true;
        session.SafeInRest = true;
        session.Audio.Armed = true;
        session.ConsecutiveRecoveryFailures = 0;
        session.RequiresHardFlowReset = false;
        WriteLog(session, "Farm da Abadia iniciado e confirmado.");
    }

    private async Task EnterTaAndStartFarmAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken,
        bool isEmergency)
    {
        var destination = EffectiveTaDestination(session);
        var taName = TaName(destination);
        session.Audio.Armed = false;
        session.SafeInRest = false;
        session.IsFarmingTa = false;
        session.AwaitingHuntActivationAtSpot = false;
        SetStatus(BotRunState.Running, $"{session.Options.Label}: entrando na {taName}", "Abrindo Terra Avassaladora");
        await ActivateGameAsync(session, cancellationToken);
        await AbortWorkflowIfDeathDetectedAsync(session, $"antes de abrir a {taName}", cancellationToken);
        if (await IsConfiguredTaLocationVisibleAsync(session, cancellationToken))
        {
            WriteLog(session, $"Chegada da {taName} já está visível; reutilizando a entrada atual.");
            await ResumeCurrentTaWithoutReentryAsync(session, pause, cancellationToken);
            return;
        }
        await ExitRestIfNeededAsync(session, pause, cancellationToken);
        await AbortWorkflowIfDeathDetectedAsync(session, $"antes de abrir o menu da {taName}", cancellationToken);
        if (session.AwaitingFavoriteSpotRecognition &&
            await IsMapOpenAsync(session, cancellationToken) &&
            (await recognition.FindAsync("aba_favoritos", cancellationToken)).Found)
        {
            WriteLog(session, "Mapa e Favoritos ainda abertos após falha na leitura do spot; retomando desta etapa sem reiniciar a T.A.");
            await TravelToFarmSpotAsync(session, pause, cancellationToken);
            return;
        }

        session.AwaitingFavoriteSpotRecognition = false;
        var readyReference = EntryReadyReference(destination);
        if (await IsTaSelectorContextVisibleAsync(session, readyReference, cancellationToken))
        {
            WriteLog(session, "O seletor da T.A já está aberto; retomando exatamente desta etapa.");
        }
        else
        {
            await OpenTaMenuAsync(session, pause, cancellationToken);
            WriteLog(session, "Abrindo Terra Avassaladora em (1742, 431).");
            await input.ClickAsync(1742, 431, cancellationToken,
                cooldown: TimeSpan.FromMilliseconds(80));
        }

        await EnterTaFromSelectorAsync(session, pause, cancellationToken, isEmergency);
    }

    private async Task OpenTaMenuAsync(ClientSession session, PauseController pause, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await AbortWorkflowIfDeathDetectedAsync(session, "durante a abertura do menu da T.A", cancellationToken);
            await TryDismissAgendaAsync(session, cancellationToken);
            var button = await recognition.FindAsync("menu_ta", cancellationToken);
            if (button.Found)
            {
                WriteRecognition(session, "Botão T.A.", button);
                return;
            }

            WriteLog(session, attempt == 1 ? "Abrindo menu lateral com =." : "Tentando abrir o menu lateral novamente.");
            await input.PressKeyAsync(KeyEquals, cancellationToken: cancellationToken,
                cooldown: TimeSpan.FromMilliseconds(80));
            if (await WaitForReferenceToAppearAsync("menu_ta", TimeSpan.FromSeconds(8), pause, cancellationToken))
            {
                return;
            }
        }

        var diagnostic = await recognition.SaveDiagnosticAsync("menu_ta");
        throw new TimeoutException($"{session.Options.Label}: não foi possível confirmar o menu da T.A. Diagnóstico: {diagnostic}");
    }

    private async Task EnterTaFromSelectorAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken,
        bool isEmergency)
    {
        var destination = EffectiveTaDestination(session);
        var taName = TaName(destination);
        SetStatus(BotRunState.Running, $"{session.Options.Label}: entrando na {taName}", isEmergency ? "Recuperação após alerta de HP" : "Selecionando a T.A");
        var readyReference = EntryReadyReference(destination);
        if (await WaitForReferenceToAppearAsync("seletor_ta", TimeSpan.FromSeconds(15), pause, cancellationToken) &&
            await IsDesiredTaEntryDisabledStableAsync(session, destination, pause, cancellationToken))
        {
            WriteLog(session, $"O botão Entrar da {taName} está apagado de forma estável; o personagem já está nessa área.");
            await ResumeCurrentTaWithoutReentryAsync(session, pause, cancellationToken);
            return;
        }
        await WaitForTaSelectorContextAsync(
            session,
            readyReference,
            TimeSpan.FromSeconds(35),
            pause,
            cancellationToken);

        // Em PCs lentos, o título aparece antes dos cartões e o clique antigo
        // era enviado para uma tela ainda carregando. O botão correto precisa
        // estar visualmente pronto; o bot apenas consulta a tela durante a
        // espera, sem impor uma pausa fixa aos computadores rápidos.
        var entryVisuallyReady = destination == TaDestination.Ta1Codex ||
            await WaitForTaEntryReadyAsync(
                session,
                readyReference,
                TimeSpan.FromSeconds(18),
                pause,
                cancellationToken);
        WriteLog(session, entryVisuallyReady
            ? $"Botão Entrar da {taName} reconhecido; preparando o clique."
            : $"O texto do botão variou neste PC; usando a posição proporcional da janela com confirmação posterior.");

        var entry = TaEntryPoints[destination];
        var arrivalReference = ArrivalReference(destination);
        var arrivalConfirmed = false;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var retryEntryClick = false;
            await EnsureGameForegroundAsync(session, cancellationToken);
            var mappedEntry = gameWindows.MapReferencePoint(
                session.Options.Target,
                entry.X,
                entry.Y);
            WriteLog(
                session,
                $"Movendo o cursor até Entrar da {taName} em ({mappedEntry.X}, {mappedEntry.Y}) — tentativa {attempt}/3.");
            await input.MoveAndClickAsync(
                mappedEntry.X,
                mappedEntry.Y,
                TimeSpan.FromMilliseconds(attempt == 1 ? 220 : 250),
                cancellationToken,
                cooldown: TimeSpan.FromMilliseconds(100));

            var startedAt = DateTime.UtcNow;
            while (DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(75))
            {
                await CheckpointAsync(pause, cancellationToken);
                var death = await FindDeathOnClientAsync(session, cancellationToken);
                if (death.Found)
                {
                    Interlocked.Exchange(ref session.PendingVisualDeath, 1);
                    throw new InvalidOperationException(
                        $"{session.Options.Label}: uma morte foi detectada durante a entrada na {taName}; a restauração terá prioridade.");
                }

                if ((await recognition.FindAsync(arrivalReference, cancellationToken)).Found)
                {
                    WriteLog(session, $"Chegada à {taName} confirmada visualmente.");
                    arrivalConfirmed = true;
                    break;
                }

                // Se o botão continua pronto após alguns segundos, o clique não
                // foi aceito. Repetimos somente nesse caso; se o seletor sumiu,
                // o carregamento está em andamento e continuamos aguardando.
                if (DateTime.UtcNow - startedAt >= TimeSpan.FromSeconds(10) &&
                    await IsTaSelectorContextVisibleAsync(session, readyReference, cancellationToken))
                {
                    WriteLog(session, "A tela de entrada continuou aberta; o clique ainda não foi aceito.");
                    retryEntryClick = true;
                    break;
                }

                await Task.Delay(450, cancellationToken);
            }

            if (arrivalConfirmed)
            {
                break;
            }

            if (!retryEntryClick)
            {
                // O seletor desapareceu, portanto o clique foi aceito. Não é
                // seguro clicar novamente em coordenadas sobre outra tela.
                break;
            }
        }

        if (!arrivalConfirmed)
        {
            var diagnostic = await recognition.SaveDiagnosticAsync($"entrada_{taName}_{session.Options.Priority}");
            throw new TimeoutException(
                $"{session.Options.Label}: a entrada na {taName} não foi confirmada após três tentativas. Diagnóstico: {diagnostic}");
        }

        await TryDismissAgendaAsync(session, cancellationToken);

        WriteLog(session, $"{taName} reconhecida; verificando o NPC de suprimentos assim que estiver visível.");
        await BuySuppliesInsideTaAsync(session, pause, cancellationToken);
        await TravelToFarmSpotAsync(session, pause, cancellationToken);
    }

    private async Task<bool> IsConfiguredTaLocationVisibleAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        var destination = EffectiveTaDestination(session);
        return (await FindReferenceOnClientAsync(
            session,
            ArrivalReference(destination),
            cancellationToken,
            requireObservable: true)).Found;
    }

    private async Task<bool> IsDesiredTaEntryDisabledStableAsync(
        ClientSession session,
        TaDestination destination,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var stableHits = 0;
        var desiredIndex = destination switch
        {
            TaDestination.Ta1Codex => 0,
            TaDestination.Ta2 => 1,
            _ => 2
        };
        var buttonRegions = new[]
        {
            (455, 735, 220, 75),
            (725, 735, 220, 75),
            (1000, 735, 245, 75)
        };

        for (var sample = 0; sample < 4; sample++)
        {
            await CheckpointAsync(pause, cancellationToken);
            var frame = await CaptureClientFrameAsync(session, cancellationToken);
            var selector = await recognition.FindAsync("seletor_ta", frame, cancellationToken);
            if (!selector.Found)
                return false;

            var lumas = buttonRegions
                .Select(region => VisualRecognitionService.MeasureAverageLuma(
                    frame, region.Item1, region.Item2, region.Item3, region.Item4))
                .ToArray();
            var brightestOther = lumas.Where((_, index) => index != desiredIndex).Max();
            stableHits = lumas[desiredIndex] + 11 <= brightestOther ? stableHits + 1 : 0;
            if (stableHits >= 3)
                return true;

            await Task.Delay(300, cancellationToken);
        }

        return false;
    }

    private async Task ResumeCurrentTaWithoutReentryAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        if ((await recognition.FindAsync("seletor_ta", cancellationToken)).Found)
        {
            await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
            await WaitForReferenceToDisappearAsync("seletor_ta", TimeSpan.FromSeconds(8), pause, cancellationToken);
        }

        // Abrir o descanso primeiro permite observar se a caça já estava ativa.
        // Pressionar Q às cegas aqui poderia desligá-la justamente no caso em
        // que o seletor informou que o personagem já estava dentro da T.A.
        var rest = await FindRestStateAsync(cancellationToken) ??
                   await TryOpenRestPanelAsync(session, pause, cancellationToken);
        if (rest is null || rest.Value.ReferenceId != "caca_automatica")
        {
            session.AwaitingHuntActivationAtSpot = true;
            await StartAutomaticHuntAsync(session, pause, cancellationToken);
        }
        else
        {
            WriteLog(session, "Caça automática já estava ativa; Q não será pressionado novamente.");
            session.AwaitingHuntActivationAtSpot = false;
        }
        session.IsFarmingTa = true;
        session.SafeInRest = await FindRestStateAsync(cancellationToken) is not null;
        session.Audio.Armed = true;
        session.ConsecutiveRecoveryFailures = 0;
        session.RequiresHardFlowReset = false;
        WriteLog(session, $"Farm existente na {TaName(EffectiveTaDestination(session))} preservado sem nova entrada.");
    }

    private async Task AbortWorkflowIfDeathDetectedAsync(
        ClientSession session,
        string context,
        CancellationToken cancellationToken)
    {
        var death = await FindDeathOnClientAsync(session, cancellationToken);
        if (Volatile.Read(ref session.PendingVisualDeath) == 0 && !death.Found)
        {
            return;
        }

        Interlocked.Exchange(ref session.PendingVisualDeath, 1);
        throw new InvalidOperationException(
            $"{session.Options.Label}: morte detectada {context}; o fluxo normal foi interrompido para priorizar a ressurreição.");
    }

    private async Task WaitForTaSelectorContextAsync(
        ClientSession session,
        string readyReference,
        TimeSpan timeout,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        double bestSelector = 0;
        double bestButton = 0;
        var ta1Confirmations = 0;
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            await AbortWorkflowIfDeathDetectedAsync(
                session,
                "enquanto aguardava o seletor da T.A",
                cancellationToken);
            if (readyReference == "entrar_ta1_pronto")
            {
                var evidence = await ReadTa1SelectorEvidenceAsync(session, cancellationToken);
                bestButton = Math.Max(bestButton, evidence.FirstCardConfidence);
                bestSelector = Math.Max(bestSelector, evidence.CardConfidence);
                ta1Confirmations = evidence.Confirmed ? ta1Confirmations + 1 : 0;
                if (ta1Confirmations >= 2)
                {
                    WriteLog(session,
                        $"T.A 1 confirmada pelo ícone do cartão, botão Entrar e leitura do texto " +
                        $"({evidence.CardConfidence:P0}; {evidence.FirstCardConfidence:P0}).");
                    return;
                }

                await Task.Delay(180, cancellationToken);
                continue;
            }

            var selector = await recognition.FindAsync("seletor_ta", cancellationToken);
            var button = await recognition.FindAsync(readyReference, cancellationToken);
            bestSelector = Math.Max(bestSelector, selector.Confidence);
            bestButton = Math.Max(bestButton, button.Confidence);
            if (selector.Found || button.Found ||
                (selector.Confidence >= TaContextConfidence &&
                 button.Confidence >= TaContextConfidence))
            {
                WriteLog(
                    session,
                    $"Tela da T.A confirmada (título {selector.Confidence:P0}; botão {button.Confidence:P0}).");
                return;
            }

            await Task.Delay(180, cancellationToken);
        }

        var diagnosticFrame = await CaptureClientFrameAsync(session, cancellationToken);
        var diagnostic = await recognition.SaveDiagnosticAsync("seletor_ta", diagnosticFrame);
        throw new TimeoutException(
            $"A tela da T.A não foi confirmada. Título: {bestSelector:P0}; botão: {bestButton:P0}. Diagnóstico: {diagnostic}");
    }

    private async Task<bool> WaitForTaEntryReadyAsync(
        ClientSession session,
        string referenceId,
        TimeSpan timeout,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            await AbortWorkflowIfDeathDetectedAsync(
                session,
                "enquanto aguardava a tela de entrada da T.A",
                cancellationToken);
            var button = await recognition.FindAsync(referenceId, cancellationToken);
            if (button.Found || button.Confidence >= TaContextConfidence)
            {
                return true;
            }

            // O seletor já aberto continua sendo uma prova suficiente para o
            // fallback proporcional; não há pausa fixa depois deste limite.
            await Task.Delay(180, cancellationToken);
        }

        return false;
    }

    private async Task<(bool Confirmed, double FirstCardConfidence, double CardConfidence)>
        ReadTa1SelectorEvidenceAsync(ClientSession session, CancellationToken cancellationToken)
    {
        var frame = await CaptureClientFrameAsync(session, cancellationToken);
        var firstTask = recognition.FindAsync("entrar_ta1_pronto", frame, cancellationToken);
        var cardTask = recognition.FindAsync("ta1_primeiro_cartao", frame, cancellationToken);
        await Task.WhenAll(firstTask, cardTask);
        var first = await firstTask;
        var card = await cardTask;
        // A correlação isolada muda com luz/cenário. Só aceitar a T.A 1 se
        // também houver seu ícone no lugar certo e o comando Entrar legível.
        // Nomes de mapas e disponibilidade da T.A 2 não entram na decisão.
        if (first.Confidence < 0.58 || !card.Found)
        {
            return (false, first.Confidence, card.Confidence);
        }

        var entryText = await _taEntryTextReader.HasFirstEntryAsync(frame, cancellationToken);
        return (entryText, first.Confidence, card.Confidence);
    }

    private async Task<bool> IsTaSelectorContextVisibleAsync(
        ClientSession session,
        string readyReference,
        CancellationToken cancellationToken)
    {
        if (readyReference == "entrar_ta1_pronto")
        {
            return (await ReadTa1SelectorEvidenceAsync(session, cancellationToken)).Confirmed;
        }

        var selector = await recognition.FindAsync("seletor_ta", cancellationToken);
        var button = await recognition.FindAsync(readyReference, cancellationToken);
        return selector.Found || button.Found ||
               (selector.Confidence >= TaContextConfidence &&
                button.Confidence >= TaContextConfidence);
    }

    private async Task BuySuppliesInsideTaAsync(ClientSession session, PauseController pause, CancellationToken cancellationToken)
    {
        var destination = EffectiveTaDestination(session);
        var taName = TaName(destination);
        SetStatus(BotRunState.Running, $"{session.Options.Label}: comprando suprimentos", $"NPC Artigos dentro da {taName}");
        await WaitForReferenceAsync(ArrivalReference(destination), "painel Artigos", TimeSpan.FromSeconds(12), pause, cancellationToken);
        await input.ClickAsync(187, 129, cancellationToken);
        await WaitForReferenceAsync("loja_artigos", "Mercador de Artigos", TimeSpan.FromSeconds(15), pause, cancellationToken);
        var buyButtonLuma = await WaitForStableBuyButtonLumaAsync(pause, cancellationToken);
        if (buyButtonLuma < 72)
        {
            WriteLog(
                session,
                $"Comprar (Lote) está apagado (brilho {buyButtonLuma:F0}). Nada a repor; fechando a loja com Esc.");
            await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
            await WaitForReferenceAsync(
                ArrivalReference(destination),
                $"retorno à {taName}",
                TimeSpan.FromSeconds(15),
                pause,
                cancellationToken);
            return;
        }

        WriteLog(session, $"Comprar (Lote) disponível (brilho {buyButtonLuma:F0}). Realizando a compra.");
        await input.ClickAsync(428, 1005, cancellationToken);
        await input.PressKeyAsync(KeyY, cancellationToken: cancellationToken);
        await WaitForReferenceAsync("compra_concluida", "Item Obtido", TimeSpan.FromSeconds(18), pause, cancellationToken);
        await input.ClickAsync(966, 453, cancellationToken);
        await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
        await WaitForReferenceAsync(ArrivalReference(destination), $"retorno à {taName}", TimeSpan.FromSeconds(15), pause, cancellationToken);
    }

    private async Task BuySuppliesInCurrentCityAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        SetStatus(BotRunState.Running, $"{session.Options.Label}: compra preventiva", "Repondo Artigos antes de voltar à Abadia");
        await ActivateGameAsync(session, cancellationToken);
        var cityDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        var cityVisible = false;
        while (DateTime.UtcNow < cityDeadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            foreach (var referenceId in new[] { "ta1_chegada", "ta2_chegada", "ta3_chegada" })
            {
                if ((await recognition.FindAsync(referenceId, cancellationToken)).Found)
                {
                    cityVisible = true;
                    break;
                }
            }

            if (cityVisible)
            {
                break;
            }

            await Task.Delay(400, cancellationToken);
        }

        if (!cityVisible)
        {
            throw new TimeoutException("Compra preventiva adiada: a chegada à cidade não foi reconhecida; não clicarei em uma coordenada sem confirmar a tela.");
        }

        await input.MoveAndClickAsync(189, 134, TimeSpan.FromMilliseconds(320), cancellationToken);
        await WaitForReferenceAsync("loja_artigos", "Mercador de Artigos da cidade", TimeSpan.FromSeconds(15), pause, cancellationToken);
        var buyButtonLuma = await WaitForStableBuyButtonLumaAsync(pause, cancellationToken);
        if (buyButtonLuma >= 72)
        {
            await input.ClickAsync(428, 1005, cancellationToken);
            await input.PressKeyAsync(KeyY, cancellationToken: cancellationToken);
            await WaitForReferenceAsync("compra_concluida", "Item Obtido", TimeSpan.FromSeconds(18), pause, cancellationToken);
            await input.ClickAsync(966, 453, cancellationToken);
            WriteLog(session, "Compra preventiva de Artigos concluída antes da Abadia.");
        }
        else
        {
            WriteLog(session, "Artigos já estavam abastecidos; nenhuma compra preventiva necessária.");
        }

        await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
    }

    private async Task<double> WaitForStableBuyButtonLumaAsync(
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var deadline = startedAt + TimeSpan.FromSeconds(5);
        double? previous = null;
        var stableSamples = 0;
        double current = 0;
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            current = VisualRecognitionService.MeasureAverageLuma(
                capture.CapturePrimaryScreen(), 300, 980, 165, 45);
            stableSamples = previous is { } value && Math.Abs(current - value) <= 5
                ? stableSamples + 1
                : 0;
            if (stableSamples >= 2 &&
                (current >= 72 || DateTime.UtcNow - startedAt >= TimeSpan.FromSeconds(1.2)))
            {
                return current;
            }

            previous = current;
            await Task.Delay(220, cancellationToken);
        }

        return current;
    }

    private async Task TravelToFarmSpotAsync(ClientSession session, PauseController pause, CancellationToken cancellationToken)
    {
        var destination = EffectiveTaDestination(session);
        var taName = TaName(destination);
        SetStatus(BotRunState.Running, $"{session.Options.Label}: indo ao spot", $"Lendo Favoritos da {taName}");
        if (!await IsMapOpenAsync(session, cancellationToken))
        {
            await input.PressKeyAsync(KeyM, cancellationToken: cancellationToken);
            await WaitForReferenceAsync(
                destination == TaDestination.Ta1Codex ? "mapa_ta1" : "mapa_aberto",
                "mapa aberto", TimeSpan.FromSeconds(15), pause, cancellationToken);
        }

        if (destination == TaDestination.Ta1Codex)
        {
            await TravelToTa1CodexSpotAsync(session, pause, cancellationToken);
            return;
        }

        if (!(await recognition.FindAsync("aba_favoritos", cancellationToken)).Found)
        {
            await OpenFavoritesAsync(session, pause, cancellationToken);
        }

        var secondFavorite = await WaitForReferenceToAppearAsync("segundo_favorito_teleporte", TimeSpan.FromSeconds(3), pause, cancellationToken);
        if (secondFavorite)
        {
            WriteLog(session, "Segundo favorito detectado: usando primeiro o ponto de teleporte.");
            await input.ClickAsync(1710, 387, cancellationToken);
            await input.ClickAsync(1710, 387, cancellationToken);
            await input.PressKeyAsync(KeyY, cancellationToken: cancellationToken);
            await WaitForReferenceToDisappearAsync("mapa_aberto", TimeSpan.FromSeconds(12), pause, cancellationToken);
            await ActionDelayAsync(cancellationToken, 4800, 6200);
            WriteLog(session, "Ponto de teleporte concluído; reabrindo o mapa e os Favoritos.");
            await input.PressKeyAsync(KeyM, cancellationToken: cancellationToken);
            await WaitForReferenceAsync("mapa_aberto", "mapa aberto após teleporte", TimeSpan.FromSeconds(15), pause, cancellationToken);
            await OpenFavoritesAsync(session, pause, cancellationToken);
        }
        else
        {
            WriteLog(session, "Sem segundo favorito de teleporte; seguindo direto ao primeiro favorito.");
        }

        int? spotLevel = null;
        if (EffectiveCustomFarmCoordinate(session) is null)
        {
            session.AwaitingFavoriteSpotRecognition = true;
            spotLevel = await RecognizeFavoriteSpotLevelAsync(session, pause, cancellationToken);
            session.AwaitingFavoriteSpotRecognition = false;
            WriteLog(session, $"Primeiro favorito reconhecido como spot Nv. {spotLevel}.");
        }
        else
        {
            session.AwaitingFavoriteSpotRecognition = false;
            WriteLog(
                session,
                $"Coordenada personalizada ativa: ({EffectiveCustomFarmCoordinate(session)!.X}, " +
                $"{EffectiveCustomFarmCoordinate(session)!.Y}). A leitura do nível e o sorteio serão ignorados.");
        }

        WriteLog(session, "Selecionando o primeiro favorito (área de farm) em (1702, 285).");
        await input.ClickAsync(1702, 285, cancellationToken);
        await ActionDelayAsync(cancellationToken, 1800, 2200);
        await input.ClickAsync(444, 531, cancellationToken);
        await input.ClickAsync(1484, 532, cancellationToken);

        (int X, int Y) spot;
        if (EffectiveCustomFarmCoordinate(session) is { } customCoordinate)
        {
            spot = gameWindows.MapReferencePoint(
                session.Options.Target,
                customCoordinate.X,
                customCoordinate.Y);
            WriteLog(
                session,
                $"Usando ponto personalizado fixo ({customCoordinate.X}, {customCoordinate.Y}) " +
                $"na janela atual: ({spot.X}, {spot.Y}).");
        }
        else
        {
            var confirmedLevel = spotLevel ??
                throw new InvalidOperationException("O nível do spot não foi reconhecido.");
            var spots = FarmSpots[destination][confirmedLevel];
            var spotIndex = ChooseNextSpot(session, confirmedLevel, spots.Length);
            spot = spots[spotIndex];
            WriteLog(
                session,
                $"Spot Nv. {confirmedLevel}, posição aleatória {spotIndex + 1}/{spots.Length}: " +
                $"({spot.X}, {spot.Y}), sem repetir a anterior.");
        }

        var goReferences = destination == TaDestination.Ta2
            ? new[] { "botao_ir_ta2", "botao_ir", "botao_ir_legado" }
            : new[] { "botao_ir", "botao_ir_legado", "botao_ir_ta2" };
        RecognitionResult? goButton = null;
        var searchX = Math.Max(0, spot.X - 220);
        var searchY = Math.Max(0, spot.Y - 210);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await input.ClickAsync(spot.X, spot.Y, cancellationToken);
            goButton = await WaitForAnyReferenceInRegionAsync(
                goReferences, searchX, searchY, 470, 230,
                TimeSpan.FromSeconds(7), pause, cancellationToken);
            if (goButton is not null)
            {
                break;
            }
        }

        var goX = goButton?.X ?? spot.X + 16;
        var goY = goButton?.Y ?? spot.Y - 76;
        if (goButton is null)
        {
            var diagnostic = await recognition.SaveDiagnosticAsync($"botao_ir_{session.Options.Priority}");
            WriteLog(session, $"Botão Ir não reconhecido visualmente; usando posição relativa segura ({goX}, {goY}). Diagnóstico: {diagnostic}");
        }

        await ClickGoButtonWithConfirmationAsync(
            session, goReferences, searchX, searchY, goX, goY, pause, cancellationToken);

        await CloseMapAfterGoAsync(session, pause, cancellationToken);
        await OpenRestForTravelAsync(session, pause, cancellationToken);

        await WaitForFarmArrivalAsync(session, pause, cancellationToken);
        session.AwaitingHuntActivationAtSpot = true;
        await StartAutomaticHuntAsync(session, pause, cancellationToken);
        session.IsFarmingTa = true;
        session.SafeInRest = true;
        session.Audio.Armed = true;
        session.ConsecutiveRecoveryFailures = 0;
        session.RequiresHardFlowReset = false;
        WriteLog(session, $"Farm da {taName} iniciado e confirmado.");
    }

    private async Task TravelToTa1CodexSpotAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var custom = EffectiveCustomFarmCoordinate(session) ??
            throw new InvalidOperationException("A T.A 1 (Codex) exige uma coordenada personalizada.");
        SetStatus(BotRunState.Running, $"{session.Options.Label}: T.A 1 (Codex)", "Abrindo todo o mapa e indo ao ponto personalizado");
        await WaitForReferenceAsync("mapa_ta1", "mapa de Kildebat", TimeSpan.FromSeconds(15), pause, cancellationToken);

        // Fecha as duas laterais antes do zoom para manter a mesma geometria da captura do usuário.
        await input.MoveAndClickAsync(444, 531, TimeSpan.FromMilliseconds(250), cancellationToken);
        await input.MoveAndClickAsync(1484, 532, TimeSpan.FromMilliseconds(250), cancellationToken);
        await input.MoveAndClickAsync(1000, 520, TimeSpan.FromMilliseconds(180), cancellationToken);
        await input.ScrollAsync(-120, 12, cancellationToken);
        await WaitForReferenceAsync("mapa_ta1_zoom_max", "mapa de Kildebat no zoom mínimo", TimeSpan.FromSeconds(12), pause, cancellationToken);

        var spot = gameWindows.MapReferencePoint(session.Options.Target, custom.X, custom.Y);
        WriteLog(session, $"T.A 1 no zoom mínimo confirmada; selecionando o ponto Codex ({custom.X}, {custom.Y}).");
        await input.ClickAsync(spot.X, spot.Y, cancellationToken);
        var goReferences = new[] { "botao_ir", "botao_ir_legado", "botao_ir_ta2" };
        var searchX = Math.Max(0, spot.X - 220);
        var searchY = Math.Max(0, spot.Y - 210);
        var go = await WaitForAnyReferenceInRegionAsync(
            goReferences, searchX, searchY, 470, 230, TimeSpan.FromSeconds(8), pause, cancellationToken);
        await ClickGoButtonWithConfirmationAsync(
            session, goReferences, searchX, searchY,
            go?.X ?? spot.X + 16, go?.Y ?? spot.Y - 76,
            pause, cancellationToken);
        await CloseMapAfterGoAsync(session, pause, cancellationToken);
        await OpenRestForTravelAsync(session, pause, cancellationToken);
        await WaitForFarmArrivalAsync(session, pause, cancellationToken, "T.A 1 (Codex)");
        session.AwaitingHuntActivationAtSpot = true;
        await StartAutomaticHuntAsync(session, pause, cancellationToken);
        session.IsFarmingTa = true;
        session.SafeInRest = true;
        session.Audio.Armed = true;
        session.ConsecutiveRecoveryFailures = 0;
        session.RequiresHardFlowReset = false;
        WriteLog(session, "Farm da T.A 1 (Codex) iniciado no ponto personalizado.");
    }

    private async Task OpenFavoritesAsync(ClientSession session, PauseController pause, CancellationToken cancellationToken)
    {
        WriteLog(session, "Abrindo Favoritos em (1819, 191).");
        await input.ClickAsync(1819, 191, cancellationToken);
        await WaitForReferenceAsync("aba_favoritos", "aba Favoritos", TimeSpan.FromSeconds(12), pause, cancellationToken);
    }

    private async Task<int> RecognizeFavoriteSpotLevelAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var levels = FarmSpots[EffectiveTaDestination(session)].Keys.ToHashSet();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        string lastText = string.Empty;
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            var frame = await CaptureClientFrameAsync(session, cancellationToken);
            var result = await _spotLevelRecognition.RecognizeFirstFavoriteAsync(
                frame,
                levels,
                cancellationToken);
            lastText = result.Text;
            if (result.Level is { } level)
            {
                return level;
            }

            await Task.Delay(450, cancellationToken);
        }

        var diagnosticFrame = await CaptureClientFrameAsync(session, cancellationToken);
        var diagnostic = await recognition.SaveDiagnosticAsync(
            $"nivel_spot_{session.Options.Priority}",
            diagnosticFrame);
        throw new InvalidOperationException(
            $"{session.Options.Label}: não foi possível reconhecer o nível do primeiro favorito da " +
            $"{TaName(EffectiveTaDestination(session))}. Texto lido: '{lastText}'. Diagnóstico: {diagnostic}");
    }

    private async Task ClickGoButtonWithConfirmationAsync(
        ClientSession session,
        IReadOnlyList<string> goReferences,
        int searchX,
        int searchY,
        int goX,
        int goY,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            WriteLog(session, $"Clicando em Ir ({goX}, {goY}) — tentativa {attempt}/3.");
            await input.ClickAsync(goX, goY, cancellationToken);
            await CheckpointAsync(pause, cancellationToken);

            var popupStillVisible = await FindAnyReferenceInRegionOnceAsync(
                goReferences, searchX, searchY, 470, 230, cancellationToken);
            if (popupStillVisible is null)
            {
                WriteLog(session, "Clique em Ir aceito; o seletor do mapa desapareceu.");
                return;
            }

            goX = popupStillVisible.X;
            goY = popupStillVisible.Y;
            WriteLog(session, "O seletor ainda está visível; repetindo o clique no Ir reconhecido.");
        }

        var diagnostic = await recognition.SaveDiagnosticAsync($"botao_ir_persistente_{session.Options.Priority}");
        WriteLog(
            session,
            $"O seletor continuou visível após 3 cliques; seguindo para a validação de deslocamento sem interromper o fluxo. Diagnóstico: {diagnostic}");
    }

    private async Task CloseMapAfterGoAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        // O mapa normalmente permanece aberto após Ir. O primeiro M é intencional e
        // não depende de uma única referência visual, que pode variar entre T.A 2 e T.A 3.
        WriteLog(session, "Fechando o mapa após Ir com M.");
        await EnsureGameForegroundAsync(session, cancellationToken);
        await input.PressKeyAsync(KeyM, cancellationToken: cancellationToken);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await CheckpointAsync(pause, cancellationToken);
            if (!await IsMapOpenAsync(session, cancellationToken))
            {
                WriteLog(session, "Mapa fechado e confirmado.");
                return;
            }

            WriteLog(session, $"O mapa ainda está aberto; repetindo M — tentativa {attempt}/2.");
            await EnsureGameForegroundAsync(session, cancellationToken);
            await input.PressKeyAsync(KeyM, cancellationToken: cancellationToken);
        }

        if (!await IsMapOpenAsync(session, cancellationToken))
        {
            WriteLog(session, "Mapa fechado e confirmado.");
            return;
        }

        var diagnostic = await recognition.SaveDiagnosticAsync($"mapa_nao_fechou_{session.Options.Priority}");
        throw new TimeoutException($"{session.Options.Label}: o mapa não fechou após Ir. Diagnóstico: {diagnostic}");
    }

    private async Task OpenRestForTravelAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await CheckpointAsync(pause, cancellationToken);
            var existingState = await FindRestStateAsync(cancellationToken);
            if (existingState is not null)
            {
                WriteLog(session, $"Descanso confirmado por '{RestStateDescription(existingState.Value.ReferenceId)}'; acompanhando a locomoção.");
                return;
            }

            if (await IsMapOpenAsync(session, cancellationToken))
            {
                WriteLog(session, "Mapa reapareceu antes do descanso; fechando novamente com M.");
                await EnsureGameForegroundAsync(session, cancellationToken);
                await input.PressKeyAsync(KeyM, cancellationToken: cancellationToken);
            }

            WriteLog(session, $"Abrindo a tela de descanso com L — tentativa {attempt}/3.");
            await EnsureGameForegroundAsync(session, cancellationToken);
            await input.PressKeyAsync(KeyL, cancellationToken: cancellationToken);
            var openedState = await WaitForRestStateAsync(TimeSpan.FromSeconds(20), pause, cancellationToken);
            if (openedState is not null)
            {
                WriteLog(session, $"Descanso confirmado por '{RestStateDescription(openedState.Value.ReferenceId)}'; acompanhando a locomoção.");
                return;
            }
        }

        var diagnostic = await recognition.SaveDiagnosticAsync($"descanso_locomoção_{session.Options.Priority}");
        throw new TimeoutException($"{session.Options.Label}: não foi possível abrir o descanso após Ir. Diagnóstico: {diagnostic}");
    }

    private async Task<bool> IsMapOpenAsync(ClientSession session, CancellationToken cancellationToken)
    {
        if ((await recognition.FindAsync("mapa_abadia", cancellationToken)).Found)
        {
            return true;
        }

        if ((await recognition.FindAsync("anonymous_map", cancellationToken)).Found)
        {
            return true;
        }

        var destination = EffectiveTaDestination(session);
        if (destination == TaDestination.Ta1Codex &&
            ((await recognition.FindAsync("mapa_ta1", cancellationToken)).Found ||
             (await recognition.FindAsync("mapa_ta1_zoom_max", cancellationToken)).Found))
        {
            return true;
        }

        var references = destination == TaDestination.Ta2
            ? new[] { "mapa_aberto_ta2_ir", "mapa_aberto", "botao_ir_ta2" }
            : new[] { "mapa_aberto", "mapa_ta3", "botao_ir", "botao_ir_legado" };
        foreach (var referenceId in references)
        {
            if ((await recognition.FindAsync(referenceId, cancellationToken)).Found)
            {
                return true;
            }
        }

        return false;
    }

    private async Task WaitForFarmArrivalAsync(ClientSession session, PauseController pause, CancellationToken cancellationToken, string? farmName = null)
    {
        var taName = farmName ?? TaName(EffectiveTaDestination(session));
        SetStatus(BotRunState.Running, $"{session.Options.Label}: indo ao spot", "Aguardando o personagem chegar");
        var startedAt = DateTime.UtcNow;
        var confirmations = 0;
        var missingRestConfirmations = 0;
        var reopenFailures = 0;
        var lastState = string.Empty;
        while (DateTime.UtcNow - startedAt < TimeSpan.FromMinutes(8))
        {
            await CheckpointAsync(pause, cancellationToken);
            await AbortWorkflowIfDeathDetectedAsync(
                session, "durante o deslocamento para o spot", cancellationToken);

            var restState = await FindRestStateAsync(cancellationToken);
            if (restState is null)
            {
                confirmations = 0;
                missingRestConfirmations++;
                if (missingRestConfirmations >= 3)
                {
                    WriteLog(
                        session,
                        "A tela de descanso sumiu durante o trajeto; reabrindo com L para continuar " +
                        "acompanhando a chegada ao mesmo spot.");
                    var reopened = await TryOpenRestPanelAsync(session, pause, cancellationToken);
                    missingRestConfirmations = 0;
                    if (reopened is null)
                    {
                        reopenFailures++;
                        if (reopenFailures >= 3)
                        {
                            var restDiagnostic = await recognition.SaveDiagnosticAsync(
                                $"descanso_perdido_trajeto_{session.Options.Priority}");
                            throw new TimeoutException(
                                $"{session.Options.Label}: a tela de descanso desapareceu durante o trajeto " +
                                $"e não reabriu após três verificações locais. Diagnóstico: {restDiagnostic}");
                        }

                        WriteLog(
                            session,
                            "O descanso ainda não reapareceu; aguardando o carregamento antes de tentar novamente.");
                    }
                    else
                    {
                        reopenFailures = 0;
                        WriteLog(
                            session,
                            $"Descanso reaberto em '{RestStateDescription(reopened.Value.ReferenceId)}'; " +
                            "continuando a acompanhar o deslocamento.");
                    }
                }

                await Task.Delay(700, cancellationToken);
                continue;
            }

            missingRestConfirmations = 0;
            reopenFailures = 0;
            if (restState.Value.ReferenceId == "descanso_ponto_fixo")
            {
                confirmations = 0;
                LogMovementState(session, ref lastState, "Aguardando no ponto fixo");
                await Task.Delay(700, cancellationToken);
                continue;
            }

            if (restState.Value.ReferenceId == "descanso_movendo")
            {
                confirmations = 0;
                LogMovementState(session, ref lastState, "Movendo-se");
                await Task.Delay(700, cancellationToken);
                continue;
            }

            if (restState.Value.ReferenceId == "descanso_aguardando_spot")
            {
                confirmations++;
                LogMovementState(session, ref lastState, "Aguardando no spot");
                if (confirmations >= 2)
                {
                    return;
                }
            }
            else
            {
                confirmations = 0;
            }

            await Task.Delay(700, cancellationToken);
        }

        var diagnostic = await recognition.SaveDiagnosticAsync($"chegada_spot_{session.Options.Priority}");
        throw new TimeoutException($"{session.Options.Label}: não chegou ao spot da {taName} em 8 minutos. Diagnóstico: {diagnostic}");
    }

    private async Task StartAutomaticHuntAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await CheckpointAsync(pause, cancellationToken);
            var hunt = await recognition.FindAsync("caca_automatica", cancellationToken);
            if (hunt.Found)
            {
                WriteLog(session, "Caça automática confirmada na tela de descanso.");
                session.SafeInRest = true;
                session.AwaitingHuntActivationAtSpot = false;
                return;
            }

            var restState = await FindRestStateAsync(cancellationToken);
            if (restState is not null)
            {
                var closed = await TryCloseRestPanelAsync(
                    session,
                    pause,
                    "ativar a caça",
                    cancellationToken);
                if (!closed)
                {
                    var closeDiagnostic = await recognition.SaveDiagnosticAsync(
                        $"descanso_nao_fechou_{session.Options.Priority}");
                    throw new TimeoutException(
                        $"{session.Options.Label}: a tela de descanso permaneceu aberta após três comandos L confirmados. " +
                        $"Diagnóstico: {closeDiagnostic}");
                }
            }

            WriteLog(session, $"Ativando caça automática com Q — ciclo {attempt}/3.");
            await EnsureGameForegroundAsync(session, cancellationToken);
            await input.PressKeyAsync(
                KeyQ,
                TimeSpan.FromMilliseconds(90 + (attempt * 40)),
                cancellationToken);

            var reopenedState = await TryOpenRestPanelAsync(session, pause, cancellationToken);
            if (reopenedState is null)
            {
                var reopenDiagnostic = await recognition.SaveDiagnosticAsync(
                    $"descanso_nao_reabriu_{session.Options.Priority}");
                throw new TimeoutException(
                    $"{session.Options.Label}: a caça recebeu Q, mas o descanso não reabriu após três comandos L. " +
                    $"Diagnóstico: {reopenDiagnostic}");
            }

            if (await WaitForReferenceToAppearAsync(
                    "caca_automatica", TimeSpan.FromSeconds(12), pause, cancellationToken))
            {
                WriteLog(session, "Caça automática ativada e confirmada.");
                session.SafeInRest = true;
                session.AwaitingHuntActivationAtSpot = false;
                return;
            }

            WriteLog(
                session,
                $"Descanso reabriu como '{RestStateDescription(reopenedState.Value.ReferenceId)}', " +
                "mas a caça não foi confirmada; repetindo somente o ciclo local L → Q → L.");

        }

        var diagnostic = await recognition.SaveDiagnosticAsync($"caca_automatica_{session.Options.Label.Replace(' ', '_')}");
        throw new TimeoutException($"{session.Options.Label}: não foi possível confirmar a caça automática após três ciclos seguros. Diagnóstico: {diagnostic}");
    }

    private async Task<bool> TryCollectDueMailSafelyAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        if (!session.Options.EnableMail)
        {
            return false;
        }

        var now = DateTime.Now;
        var today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dueAt01 = now.Hour >= 1 && session.Mail01Date != today;
        var dueAt07 = now.Hour >= 7 && session.Mail07Date != today;
        if ((!dueAt01 && !dueAt07) || DateTime.UtcNow < session.NextMailAttemptAt ||
            session.InAgenda || session.DailyNeedsTeleport || session.HandlingDeath ||
            session.NextRecoveryAttemptAt != default)
        {
            return false;
        }

        session.NextMailAttemptAt = DateTime.UtcNow.AddMinutes(3);
        try
        {
            // Uma confirmação de teleporte já aberta tem precedência: abrir o
            // Correio sobre o popup poderia descartar a próxima Diária.
            if (await FindDailyTeleportPopupOnClientAsync(session, cancellationToken) is not null)
            {
                session.NextMailAttemptAt = DateTime.UtcNow.AddSeconds(30);
                return false;
            }

            var claimed = await CollectServerMailAsync(session, pause, cancellationToken);
            // O servidor pode entregar o lote poucos minutos depois da hora
            // cheia. Se a caixa estiver vazia nesse intervalo, reabrimos em
            // três minutos em vez de perder a entrega do dia.
            var waitFor01 = !claimed && now.Hour == 1 && now.Minute < 10;
            var waitFor07 = !claimed && now.Hour == 7 && now.Minute < 10;
            if (dueAt01 && !waitFor01)
            {
                session.Mail01Date = today;
                await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.mail.01Date", today);
            }

            if (dueAt07 && !waitFor07)
            {
                session.Mail07Date = today;
                await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.mail.07Date", today);
            }

            WriteLog(session, waitFor01 || waitFor07
                ? "Correio ainda vazio próximo ao horário de entrega; conferindo novamente em 3 minutos."
                : "Correio das 01:00/07:00 conferido; fluxo normal retomado.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            WriteLog(session, $"Correio não pôde ser confirmado: {exception.GetBaseException().Message}. Nova tentativa em 3 minutos.");
            WritePersistentOnly(session, exception.ToString());
        }

        return true;
    }

    private async Task<bool> CollectServerMailAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        await ActivateGameAsync(session, cancellationToken);
        var resumeRest = session.IsFarmingTa || session.SafeInRest ||
                         await FindRestStateAsync(cancellationToken) is not null;
        await ExitRestIfNeededAsync(session, pause, cancellationToken);
        await TryDismissAgendaAsync(session, cancellationToken);
        var mailScreenOpened = (await recognition.FindAsync("mail_page", cancellationToken)).Found;
        try
        {
            if (!mailScreenOpened && !(await recognition.FindAsync("menu_mail", cancellationToken)).Found)
            {
                await input.PressKeyAsync(KeyEquals, cancellationToken: cancellationToken);
                await WaitForReferenceAsync(
                    "menu_mail", "ícone Correio", TimeSpan.FromSeconds(10), pause, cancellationToken);
            }

            if (!mailScreenOpened)
            {
                await input.MoveAndClickAsync(1608, 977, TimeSpan.FromMilliseconds(300), cancellationToken);
                await WaitForReferenceAsync(
                    "mail_page", "página Correio", TimeSpan.FromSeconds(12), pause, cancellationToken);
                mailScreenOpened = true;
            }
            await input.MoveAndClickAsync(116, 138, TimeSpan.FromMilliseconds(230), cancellationToken);

            var mailStateDeadline = DateTime.UtcNow.AddSeconds(8);
            var mailStateStartedAt = DateTime.UtcNow;
            var pendingMail = false;
            var emptyMail = false;
            var clearObservations = 0;
            while (DateTime.UtcNow < mailStateDeadline)
            {
                await CheckpointAsync(pause, cancellationToken);
                pendingMail = VisualRecognitionService.HasUnclaimedServerMail(
                    capture.CapturePrimaryScreen());
                if (pendingMail)
                {
                    break;
                }

                clearObservations++;
                emptyMail = (await recognition.FindAsync("mail_empty", cancellationToken)).Found;
                if (!emptyMail && clearObservations >= 8 &&
                    DateTime.UtcNow - mailStateStartedAt >= TimeSpan.FromSeconds(4))
                {
                    emptyMail = true;
                }
                if (emptyMail)
                {
                    break;
                }

                await Task.Delay(350, cancellationToken);
            }

            if (!pendingMail && !emptyMail)
            {
                throw new TimeoutException($"{session.Options.Label}: o Correio abriu, mas não foi possível distinguir mensagens pendentes de caixa vazia.");
            }

            if (!pendingMail)
            {
                WriteLog(session, "Correio do Servidor sem notificação de mensagem pendente.");
                return false;
            }

            await WaitForReferenceAsync(
                "mail_receive_all", "botão Receber Tudo", TimeSpan.FromSeconds(8), pause, cancellationToken);
            await input.MoveAndClickAsync(1787, 1000, TimeSpan.FromMilliseconds(300), cancellationToken);
            var itemsShown = await WaitForReferenceToAppearAsync(
                "mail_item_obtained", TimeSpan.FromSeconds(12), pause, cancellationToken);
            if (itemsShown)
            {
                await input.MoveAndClickAsync(955, 440, TimeSpan.FromMilliseconds(220), cancellationToken);
                await Task.Delay(500, cancellationToken);
            }

            var remainingMail = VisualRecognitionService.HasUnclaimedServerMail(
                capture.CapturePrimaryScreen());
            if (remainingMail)
            {
                var diagnostic = await recognition.SaveDiagnosticAsync($"mail_claim_{session.Options.Priority}");
                throw new TimeoutException(
                    $"{session.Options.Label}: a notificação vermelha do Correio permanece após Receber Tudo. Diagnóstico: {diagnostic}");
            }

            WriteLog(session, itemsShown
                ? "Recompensas do Correio recebidas e aviso Item Obtido fechado."
                : "Notificação do Correio removida após Receber Tudo; seguindo sem aviso Item Obtido.");
            return true;
        }
        finally
        {
            if (mailScreenOpened || (await recognition.FindAsync("mail_page", cancellationToken)).Found)
            {
                await CloseMailScreenAsync(session, pause, cancellationToken);
            }
            else if ((await recognition.FindAsync("menu_mail", cancellationToken)).Found)
            {
                await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
            }

            if (resumeRest)
            {
                var rest = await TryOpenRestPanelAsync(session, pause, cancellationToken);
                session.SafeInRest = rest is not null;
                if (rest is null)
                {
                    WriteLog(session, "Correio encerrado; caça continua na tela normal porque o descanso não pôde ser confirmado.");
                }
            }
        }
    }

    private async Task CloseMailScreenAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!(await recognition.FindAsync("mail_page", cancellationToken)).Found)
            {
                break;
            }

            await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
            await CheckpointAsync(pause, cancellationToken);
            await Task.Delay(250, cancellationToken);
        }

        if ((await recognition.FindAsync("mail_page", cancellationToken)).Found)
        {
            throw new TimeoutException($"{session.Options.Label}: o Correio continuou aberto após três ESC.");
        }

        if ((await recognition.FindAsync("menu_mail", cancellationToken)).Found)
        {
            await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
        }
    }

    private async Task<bool> RunDueDailyRoutinesAsync(
        ClientSession session,
        DailyRoutineOptions options,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        if (session.InAgenda || session.HandlingDeath || session.InDailyCampaign || session.NextRecoveryAttemptAt != default)
        {
            return false;
        }

        var now = DateTime.Now;
        var cycle = DailyCycleKey(now);
        var dailyDue = options.EnableDailyMissions && session.Options.EnableDailyMissions && session.DailyCompletedCycle != cycle &&
                       now >= ScheduledInCycle(now, options.DailyMissionsAt);
        var directiveDue = options.EnableGuildDirective && session.Options.EnableGuildDirective && session.DirectiveCycle != cycle &&
                           DateTime.UtcNow >= session.NextDirectiveAttemptAt &&
                           now >= ScheduledInCycle(now, options.GuildDirectiveAt);
        var shopCycle = DailyShopCycleKey(now);
        var dailyShopDue = options.EnableDailyShop && session.Options.EnableDailyShop && session.DailyShopCycle != shopCycle &&
                           DateTime.UtcNow >= session.NextDailyShopAttemptAt &&
                           now >= ScheduledInDailyShopCycle(now, options.DailyShopAt);

        if (!dailyDue && !directiveDue && !dailyShopDue)
        {
            return false;
        }

        await ActivateGameAsync(session, cancellationToken);
        var death = await FindDeathOnClientAsync(session, cancellationToken);
        if (death.Found)
        {
            Interlocked.Exchange(ref session.PendingVisualDeath, 1);
            return false;
        }

        if (dailyShopDue)
        {
            try
            {
                await PurchaseDailyShopAsync(session, pause, cancellationToken);
                await MarkDailyShopHandledAsync(session, shopCycle);
                WriteLog(session, "Compra diária da Loja concluída ou já esgotada e registrada para este ciclo.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                WritePersistentOnly(session, exception.ToString());
                await RegisterDailyShopUncertainAsync(session, shopCycle, exception.GetBaseException().Message);
            }

            if (!dailyDue && !directiveDue)
                return true;
        }

        if (WantsAbbey(session) && session.AbbeyInside)
        {
            await RecordAbbeyExitAsync(session);
            WriteLog(session, "Saída programada da Abadia: eventual reentrada contará no limite configurado.");
        }

        session.Audio.Armed = false;
        session.IsFarmingTa = false;
        session.SafeInRest = false;
        await ExitRestIfNeededAsync(session, pause, cancellationToken);

        var dailyAlreadyAccepted = session.DailyCycle == cycle;
        if (dailyDue && !dailyAlreadyAccepted)
        {
            dailyAlreadyAccepted = await AcceptDailyMissionsAsync(session, pause, cancellationToken);
            session.DailyCycle = cycle;
            await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyCycle", cycle);
        }

        if (directiveDue)
        {
            bool directiveHandled;
            try
            {
                directiveHandled = await AcceptGuildDirectiveAsync(
                    session, options.GuildDirectiveArea, pause, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                WritePersistentOnly(session, exception.ToString());
                directiveHandled = await RegisterDirectiveUncertainAsync(
                    session, pause, cancellationToken, exception.GetBaseException().Message);
            }

            if (directiveHandled)
                await MarkDirectiveHandledAsync(session, cycle);
        }

        if (dailyDue)
        {
            session.DailyNeedsTeleport = true;
            bool dailyRestConfirmed;
            if (dailyAlreadyAccepted)
            {
                WriteLog(session, "Retomando as Missões Diárias já aceitas após interrupção ou reinício anterior.");
                var resumeResult = await ResumeDailyCampaignAsync(session, pause, cancellationToken);
                if (resumeResult == DailyResumeResult.NoMission)
                {
                    await MarkDailyCampaignCompletedAsync(
                        session,
                        "As Diárias já aceitas não possuem mais missão roxa; seguindo para o farm configurado.");
                    await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
                    return true;
                }

                var resumed = resumeResult == DailyResumeResult.Started;
                dailyRestConfirmed = resumed && session.SafeInRest;
                if (resumeResult == DailyResumeResult.Inconclusive)
                {
                    WriteLog(session, "A retomada não encontrou missão roxa neste momento; mantendo a rotina ativa para confirmar a conclusão sem aceitar tudo novamente.");
                }
            }
            else
            {
                dailyRestConfirmed = await StartDailyCampaignAsync(session, pause, cancellationToken);
            }

            session.InDailyCampaign = true;
            session.NextDailyMissionCheckAt = DateTime.UtcNow.Add(
                session.DailyNeedsTeleport ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(2));
            if (!session.DailyNeedsTeleport)
            {
                session.DailyStartedCycle = cycle;
                await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyStartedCycle", cycle);
                session.IsFarmingTa = true;
                session.SafeInRest = dailyRestConfirmed;
            }
            session.Audio.Armed = true;
            WriteLog(session, session.DailyNeedsTeleport
                ? "Teleporte da Diária pendente; a tela será reavaliada automaticamente em 15 segundos sem encerrar o bot."
                : dailyRestConfirmed
                    ? "Campanha automática das Diárias iniciada em modo descanso."
                    : "Campanha das Diárias iniciada fora do descanso; o monitoramento continuará normalmente.");
        }
        else
        {
            await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
        }

        return true;
    }

    private async Task PurchaseDailyShopAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var shopCycle = DailyShopCycleKey(DateTime.Now);
        var resumeRest = session.SafeInRest || await FindRestStateAsync(cancellationToken) is not null;
        await ActivateGameAsync(session, cancellationToken);
        await ExitRestIfNeededAsync(session, pause, cancellationToken);
        if (await FindRestStateAsync(cancellationToken) is not null)
            throw new TimeoutException($"{session.Options.Label}: o descanso permaneceu aberto; a Loja não será acionada sobre a tela L.");

        var storeOpened = false;
        try
        {
            await input.PressKeyAsync(KeyV, cancellationToken: cancellationToken);
            await WaitForReferenceAsync("daily_shop_page", "página Loja", TimeSpan.FromSeconds(15), pause, cancellationToken);
            storeOpened = true;
            await input.MoveAndClickAsync(337, 142, TimeSpan.FromMilliseconds(180), cancellationToken, cooldown: TimeSpan.FromMilliseconds(50));
            await WaitForReferenceAsync("daily_shop_coins", "aba Moedas", TimeSpan.FromSeconds(12), pause, cancellationToken);
            await input.MoveAndClickAsync(120, 264, TimeSpan.FromMilliseconds(180), cancellationToken, cooldown: TimeSpan.FromMilliseconds(50));
            await WaitForReferenceAsync("daily_shop_common", "categoria Comum", TimeSpan.FromSeconds(12), pause, cancellationToken);

            if (session.DailyShopCommonCycle == shopCycle)
            {
                WriteLog(session, "Compra diária de Comum já foi registrada neste ciclo.");
            }
            else if (await IsDailyShopCategoryExhaustedStableAsync(session, summon: false, pause, cancellationToken))
            {
                WriteLog(session, "Compra diária de Comum já está esgotada neste ciclo; nenhuma nova tentativa será feita.");
                await MarkDailyShopCategoryHandledAsync(session, shopCycle, summon: false);
            }
            else
            {
                await ConfirmDailyBulkPurchaseAsync(session, pause, cancellationToken, "Comum", summon: false);
                await MarkDailyShopCategoryHandledAsync(session, shopCycle, summon: false);
            }

            await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
            await input.MoveAndClickAsync(146, 328, TimeSpan.FromMilliseconds(180), cancellationToken, cooldown: TimeSpan.FromMilliseconds(50));
            await Task.Delay(500, cancellationToken);
            if (session.DailyShopSummonCycle == shopCycle)
            {
                WriteLog(session, "Compra diária de Invocação já foi registrada neste ciclo.");
            }
            else if (await IsDailyShopCategoryExhaustedStableAsync(session, summon: true, pause, cancellationToken))
            {
                WriteLog(session, "Compra diária de Invocação já está esgotada neste ciclo; nenhuma nova tentativa será feita.");
                await MarkDailyShopCategoryHandledAsync(session, shopCycle, summon: true);
            }
            else
            {
                await ConfirmDailyBulkPurchaseAsync(session, pause, cancellationToken, "Invocação", summon: true);
                await MarkDailyShopCategoryHandledAsync(session, shopCycle, summon: true);
            }
        }
        finally
        {
            if (storeOpened || (await recognition.FindAsync("daily_shop_page", cancellationToken)).Found)
            {
                await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
                await Task.Delay(250, cancellationToken);
                await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
                await Task.Delay(350, cancellationToken);
            }

            if (resumeRest && await FindRestStateAsync(cancellationToken) is null)
            {
                var rest = await TryOpenRestPanelAsync(session, pause, cancellationToken);
                session.SafeInRest = rest is not null;
                if (rest is null)
                    WriteLog(session, "Compra encerrada; caça continua na tela normal porque o descanso não pôde ser reaberto.");
            }
        }
    }

    private async Task ConfirmDailyBulkPurchaseAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken,
        string category,
        bool summon)
    {
        if (!await WaitForReferenceToAppearAsync("daily_shop_bulk", TimeSpan.FromSeconds(8), pause, cancellationToken))
        {
            if (await IsDailyShopCategoryExhaustedStableAsync(session, summon, pause, cancellationToken))
            {
                WriteLog(session, $"{category} ficou esgotada antes do clique; etapa considerada concluída.");
                return;
            }

            throw new TimeoutException($"{session.Options.Label}: Compra em Lote de {category} não apareceu e o esgotamento diário não foi confirmado.");
        }

        await input.MoveAndClickAsync(145, 1009, TimeSpan.FromMilliseconds(180), cancellationToken, cooldown: TimeSpan.FromMilliseconds(50));
        if (!await WaitForReferenceToAppearAsync("daily_shop_bulk_popup", TimeSpan.FromSeconds(10), pause, cancellationToken))
        {
            if (await IsDailyShopCategoryExhaustedStableAsync(session, summon, pause, cancellationToken))
            {
                WriteLog(session, $"{category} já estava esgotada; popup de compra não será procurado novamente.");
                return;
            }

            throw new TimeoutException($"{session.Options.Label}: o popup do lote de {category} não apareceu.");
        }

        await input.PressKeyAsync(KeyY, cancellationToken: cancellationToken);
        try
        {
            await WaitForReferenceToDisappearAsync("daily_shop_bulk_popup", TimeSpan.FromSeconds(15), pause, cancellationToken);
        }
        catch (TimeoutException exception)
        {
            WritePersistentOnly(session, $"Popup da compra de {category} demorou a sumir após Y: {exception.Message}");
            await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
        }
        WriteLog(session, $"Compra em Lote de {category} confirmada.");
    }

    private async Task MarkDailyShopCategoryHandledAsync(
        ClientSession session,
        string cycle,
        bool summon)
    {
        var key = summon ? "dailyShopSummonCycle" : "dailyShopCommonCycle";
        if (summon)
            session.DailyShopSummonCycle = cycle;
        else
            session.DailyShopCommonCycle = cycle;
        await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.routines.{key}", cycle);
    }

    private async Task MarkDailyShopHandledAsync(ClientSession session, string cycle)
    {
        session.DailyShopCycle = cycle;
        session.DailyShopAttemptCycle = cycle;
        session.DailyShopAttemptCount = 0;
        session.NextDailyShopAttemptAt = default;
        var prefix = $"{SessionSettingPrefix(session)}.routines";
        await database.SaveSettingAsync($"{prefix}.dailyShopCycle", cycle);
        await database.SaveSettingAsync($"{prefix}.dailyShopAttemptCycle", cycle);
        await database.SaveSettingAsync($"{prefix}.dailyShopAttemptCount", "0");
    }

    private async Task RegisterDailyShopUncertainAsync(
        ClientSession session,
        string cycle,
        string reason)
    {
        if (!string.Equals(session.DailyShopAttemptCycle, cycle, StringComparison.Ordinal))
        {
            session.DailyShopAttemptCycle = cycle;
            session.DailyShopAttemptCount = 0;
        }

        session.DailyShopAttemptCount++;
        var prefix = $"{SessionSettingPrefix(session)}.routines";
        await database.SaveSettingAsync($"{prefix}.dailyShopAttemptCycle", cycle);
        await database.SaveSettingAsync(
            $"{prefix}.dailyShopAttemptCount",
            session.DailyShopAttemptCount.ToString(CultureInfo.InvariantCulture));
        if (session.DailyShopAttemptCount >= 3)
        {
            await MarkDailyShopHandledAsync(session, cycle);
            WriteLog(session,
                $"Loja não pôde ser confirmada após três verificações ({reason}). " +
                "A rotina ficará encerrada neste ciclo para preservar o farm e evitar repetição.");
            return;
        }

        var delay = session.DailyShopAttemptCount == 1 ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(10);
        session.NextDailyShopAttemptAt = DateTime.UtcNow + delay;
        WriteLog(session,
            $"Compra diária adiada por {delay.TotalMinutes:0} min: {reason} " +
            $"Tentativa segura {session.DailyShopAttemptCount}/3.");
    }

    private async Task<bool> IsDailyShopCategoryExhaustedStableAsync(
        ClientSession session,
        bool summon,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var confirmations = 0;
        for (var sample = 0; sample < 3; sample++)
        {
            await CheckpointAsync(pause, cancellationToken);
            var frame = await CaptureClientFrameAsync(session, cancellationToken);
            var status = summon
                ? await _dailyShopStatusReader.ReadSummonAsync(frame, cancellationToken)
                : await _dailyShopStatusReader.ReadCommonAsync(frame, cancellationToken);
            confirmations = status.Exhausted ? confirmations + 1 : 0;
            if (confirmations >= 2)
                return true;

            await Task.Delay(280, cancellationToken);
        }

        return false;
    }

    private async Task<bool> RunDueDailyRoutinesSafelyAsync(
        ClientSession session,
        DailyRoutineOptions options,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow < session.NextDailyRoutineAttemptAt)
        {
            return false;
        }

        try
        {
            var started = await RunDueDailyRoutinesAsync(session, options, pause, cancellationToken);
            if (started)
            {
                session.NextDailyRoutineAttemptAt = default;
                session.DailyRoutineFailureCount = 0;
            }

            return started;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            session.DailyRoutineFailureCount++;
            var retryDelay = session.DailyRoutineFailureCount switch
            {
                1 => TimeSpan.FromSeconds(30),
                2 => TimeSpan.FromMinutes(2),
                3 => TimeSpan.FromMinutes(10),
                _ => TimeSpan.FromMinutes(30)
            };
            session.NextDailyRoutineAttemptAt = DateTime.UtcNow.Add(retryDelay);
            session.NextRoutinePanelRecoveryAt = session.NextDailyRoutineAttemptAt;
            session.Audio.Armed = !session.InAgenda;
            WritePersistentOnly(session, exception.ToString());
            await RecoverDailyRoutineFailureAsync(session, pause, cancellationToken);
            WriteLog(session,
                $"Falha recuperável na rotina diária: {exception.GetBaseException().Message}. " +
                $"A interface foi liberada e a nova tentativa ocorrerá em {FormatDuration(retryDelay)}, " +
                "sem repetir cliques durante a espera.");
            return true;
        }
    }

    private async Task RecoverDailyRoutineFailureAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        try
        {
            await ActivateGameAsync(session, cancellationToken);
            for (var attempt = 0; attempt < 5 && await IsKnownBlockingOverlayVisibleAsync(cancellationToken); attempt++)
            {
                await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
                await Task.Delay(300, cancellationToken);
            }

            if (!session.InAgenda && await FindRestStateAsync(cancellationToken) is null)
            {
                var rest = await TryOpenRestPanelAsync(session, pause, cancellationToken);
                session.SafeInRest = rest is not null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception recoveryException)
        {
            WritePersistentOnly(session, $"Recuperação da rotina diária: {recoveryException}");
        }
    }

    private async Task<bool> TryStartVisibleDailyCampaignSafelyAsync(
        ClientSession session,
        DailyRoutineOptions options,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var currentCycle = DailyCycleKey(DateTime.Now);
        if (!options.EnableDailyMissions || !session.Options.EnableDailyMissions || session.InAgenda || session.HandlingDeath ||
            session.InDailyCampaign || session.NextRecoveryAttemptAt != default ||
            DateTime.UtcNow < session.NextVisibleDailyScanAt ||
            session.DailyCompletedCycle == currentCycle)
        {
            return false;
        }

        session.NextVisibleDailyScanAt = DateTime.UtcNow.AddSeconds(30);
        try
        {
            // O popup escurece a lista lateral. Ele precisa ser reconhecido antes
            // da busca pelas linhas roxas, inclusive quando o bot inicia nessa tela.
            var pendingTeleportPopup = await FindDailyTeleportPopupOnClientAsync(session, cancellationToken) is not null;
            if (pendingTeleportPopup)
            {
                var popupFrame = await CaptureDailyMissionFrameAsync(session, cancellationToken);
                if (VisualRecognitionService.CountDimmedDailyMissionRows(popupFrame) < 2)
                {
                    // O diálogo de teleporte também existe em outras campanhas.
                    // Só o adotamos como Diária quando há várias linhas roxas atrás dele.
                    return false;
                }
            }

            // Leitura passiva: o botão lateral alterna mostrar/ocultar e não pode ser
            // usado para descobrir se há Diárias durante o farm.
            for (var sample = 0; !pendingTeleportPopup && sample < 2; sample++)
            {
                await CheckpointAsync(pause, cancellationToken);
                var frame = await CaptureDailyMissionFrameAsync(session, cancellationToken);
                var scanCycle = currentCycle;
                // Fora do descanso, uma missão comum pode usar texto roxo. Para
                // adotar uma rotina desconhecida exigimos o bloco típico de
                // várias Diárias; depois que este ciclo já começou, uma única
                // missão restante é suficiente.
                var requiredPurpleRows = session.DailyStartedCycle == scanCycle ? 1 : 4;
                if (!HasVisibleQuestRows(frame) ||
                    FindPurpleDailyMissionY(frame, requiredPurpleRows) is null)
                {
                    return false;
                }

                if (sample == 0)
                {
                    await Task.Delay(350, cancellationToken);
                }
            }

            var cycle = currentCycle;
            WriteLog(session, pendingTeleportPopup
                ? "Popup de teleporte da Diária pendente; confirmando agora, independentemente do horário programado."
                : "Missões Diárias roxas já aceitas e visíveis; retomando agora, independentemente do horário programado.");
            if (session.DailyCompletedCycle == cycle)
            {
                session.DailyCompletedCycle = null;
                await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyCompletedCycle", "");
                WriteLog(session, "O estado visível das missões corrigiu o registro de conclusão deste ciclo.");
            }

            session.DailyCycle = cycle;
            await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyCycle", cycle);
            if (WantsAbbey(session) && session.AbbeyInside)
            {
                await RecordAbbeyExitAsync(session);
            }

            session.Audio.Armed = false;
            var alreadyRunning = (await FindReferenceOnClientAsync(
                session, "daily_automatic", cancellationToken, requireObservable: true)).Found;
            var resumeResult = alreadyRunning
                ? DailyResumeResult.Started
                : await ResumeDailyCampaignAsync(session, pause, cancellationToken);
            if (resumeResult == DailyResumeResult.NoMission)
            {
                await MarkDailyCampaignCompletedAsync(
                    session,
                    "A missão roxa deixou de existir durante a retomada; seguindo para o farm configurado.");
                await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
                return true;
            }

            var resumed = resumeResult == DailyResumeResult.Started;
            session.InDailyCampaign = true;
            session.DailyNeedsTeleport = !resumed;
            session.DailyNoMissionHits = 0;
            session.NextDailyMissionCheckAt = DateTime.UtcNow.Add(
                resumed ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(15));
            if (resumed)
            {
                session.DailyStartedCycle = cycle;
                await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyStartedCycle", cycle);
                session.IsFarmingTa = true;
                session.SafeInRest = alreadyRunning || session.SafeInRest;
            }

            session.Audio.Armed = true;
            WriteLog(session, resumed
                ? alreadyRunning
                    ? "Campanha automática já estava em andamento; mantendo o descanso e acompanhando até concluir."
                    : "Campanha das Diárias retomada; o farm configurado voltará após a conclusão."
                : "Diárias visíveis, mas o teleporte ainda não foi confirmado; nova tentativa automática em 15 segundos.");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            session.NextVisibleDailyScanAt = DateTime.UtcNow.AddSeconds(15);
            session.Audio.Armed = !session.InAgenda;
            WriteLog(session, $"Checagem visual das Diárias será repetida em 15 segundos: {exception.GetBaseException().Message}.");
            WritePersistentOnly(session, exception.ToString());
            return false;
        }
    }

    // Ao iniciar no meio de uma tela de rotina, sincroniza o estado com o jogo
    // antes de decidir entre retomar Diárias e preparar o farm normal.
    private async Task<bool> RecoverOpenRoutinePanelsSafelyAsync(
        ClientSession session,
        DailyRoutineOptions options,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        try
        {
            session.NextRoutinePanelRecoveryAt = default;
            await ActivateGameAsync(session, cancellationToken);
            var cycle = DailyCycleKey(DateTime.Now);
            if ((await recognition.FindAsync("daily_shop_page", cancellationToken)).Found ||
                (await recognition.FindAsync("daily_shop_bulk_popup", cancellationToken)).Found)
            {
                WriteLog(session, "Loja encontrada aberta ao iniciar; fechando a tela e sincronizando a compra no próximo ciclo da rotina.");
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
                    await Task.Delay(250, cancellationToken);
                }
            }

            if (await IsGuildDirectiveCompletedAsync(session, cancellationToken))
            {
                WriteLog(session, "Diretivas 5/5 já concluídas ao iniciar; fechando a Guilda sem recarregar.");
                await MarkDirectiveHandledAsync(session, cycle);
                await CloseGuildScreenAsync(session, pause, cancellationToken);
            }
            else if (await IsGuildDirectiveInProgressAsync(cancellationToken))
            {
                WriteLog(session, "Diretiva já aceita e em andamento ao iniciar; não clicando em Desistir.");
                await MarkDirectiveHandledAsync(session, cycle);
                await CloseGuildScreenAsync(session, pause, cancellationToken);
            }
            else if ((await recognition.FindAsync("guild_page", cancellationToken)).Found)
            {
                WriteLog(session, "Tela da Guilda aberta ao iniciar; retornando ao jogo para continuar o fluxo.");
                await CloseGuildScreenAsync(session, pause, cancellationToken);
            }

            if (!options.EnableDailyMissions)
            {
                return false;
            }

            var dailyPageOpen = (await recognition.FindAsync("daily_page", cancellationToken)).Found;
            var dailyAcceptedInCycle = session.DailyCycle == cycle && session.DailyCompletedCycle != cycle;
            if (!dailyPageOpen && !dailyAcceptedInCycle)
            {
                return false;
            }

            if (dailyPageOpen)
            {
                var alreadyAccepted = VisualRecognitionService.HasDailyThirtyCounter(
                    capture.CapturePrimaryScreen());
                WriteLog(session, alreadyAccepted
                    ? "Diárias 30/30 já aceitas ao iniciar; fechando o painel e verificando as missões pendentes."
                    : "Painel das Diárias aberto ao iniciar; fechando-o para continuar o fluxo.");
                await CloseCampaignScreenAsync(session, pause, cancellationToken);
                if (!alreadyAccepted)
                {
                    return false;
                }

                session.DailyCycle = cycle;
                await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyCycle", cycle);
            }

            var missionList = await EnsureDailyMissionListAsync(session, pause, cancellationToken);
            if (missionList.MissionY is not null)
            {
                session.NextVisibleDailyScanAt = default;
                WriteLog(session, "Há missão roxa pendente; iniciando a retomada imediata das Diárias.");
                return false;
            }

            if (!missionList.ListVisible)
            {
                session.NextRoutinePanelRecoveryAt = DateTime.UtcNow.AddSeconds(15);
                WriteLog(session, "Diárias aceitas, mas a lista não pôde ser confirmada; nova verificação em 15 segundos.");
                return true;
            }

            await Task.Delay(500, cancellationToken);
            var confirmation = await ReadDailyMissionListAsync(session, pause, cancellationToken);
            if (confirmation.MissionY is not null)
            {
                session.NextVisibleDailyScanAt = default;
                WriteLog(session, "Missão roxa confirmada na segunda leitura; iniciando a retomada imediata das Diárias.");
                return false;
            }

            if (!confirmation.ListVisible)
            {
                session.NextRoutinePanelRecoveryAt = DateTime.UtcNow.AddSeconds(15);
                WriteLog(session, "A segunda leitura da lista foi inconclusiva; nova verificação em 15 segundos.");
                return true;
            }

            await MarkDailyCampaignCompletedAsync(
                session,
                "Lista aberta e estável sem missão roxa: Diárias concluídas; seguindo para o farm.");
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            session.NextRoutinePanelRecoveryAt = DateTime.UtcNow.AddSeconds(15);
            WriteLog(session, $"Estado inicial das rotinas será reavaliado: {exception.GetBaseException().Message}.");
            WritePersistentOnly(session, exception.ToString());
            return true;
        }
    }

    private async Task<bool> AcceptGuildDirectiveAsync(
        ClientSession session,
        GuildDirectiveArea area,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        SetStatus(BotRunState.Running, $"{session.Options.Label}: Diretiva de Guilda", "Abrindo a Guilda e aceitando o local configurado");
        if (await CloseCompletedGuildDirectiveIfPresentAsync(session, pause, cancellationToken))
        {
            return true;
        }
        if (await CloseActiveGuildDirectiveIfPresentAsync(session, pause, cancellationToken))
        {
            return true;
        }

        await input.PressKeyAsync(KeyEquals, cancellationToken: cancellationToken);
        await WaitForReferenceAsync("menu_guild", "ícone Guilda", TimeSpan.FromSeconds(10), pause, cancellationToken);
        await input.MoveAndClickAsync(1603, 340, TimeSpan.FromMilliseconds(320), cancellationToken);
        await WaitForReferenceAsync("guild_page", "página da Guilda", TimeSpan.FromSeconds(15), pause, cancellationToken);
        await input.MoveAndClickAsync(523, 143, TimeSpan.FromMilliseconds(320), cancellationToken);
        var directivePageDeadline = DateTime.UtcNow.AddSeconds(15);
        var directivePageVisible = false;
        while (DateTime.UtcNow < directivePageDeadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            if (await CloseCompletedGuildDirectiveIfPresentAsync(session, pause, cancellationToken))
            {
                return true;
            }
            if (await CloseActiveGuildDirectiveIfPresentAsync(session, pause, cancellationToken))
            {
                return true;
            }

            if ((await recognition.FindAsync("guild_directive_page", cancellationToken)).Found)
            {
                directivePageVisible = true;
                break;
            }

            await Task.Delay(250, cancellationToken);
        }

        if (!directivePageVisible)
        {
            var diagnostic = await recognition.SaveDiagnosticAsync($"guild_directive_page_{session.Options.Priority}");
            return await RegisterDirectiveUncertainAsync(
                session, pause, cancellationToken,
                $"a página de Diretivas não foi reconhecida. Diagnóstico: {diagnostic}");
        }

        await Task.Delay(400, cancellationToken);
        if (await CloseCompletedGuildDirectiveIfPresentAsync(session, pause, cancellationToken))
        {
            return true;
        }
        if (await CloseActiveGuildDirectiveIfPresentAsync(session, pause, cancellationToken))
        {
            return true;
        }
        var point = area switch
        {
            GuildDirectiveArea.Ta => (1160, 809),
            GuildDirectiveArea.Dungeon => (1507, 805),
            _ => (834, 805)
        };
        var buttonSearchX = point.Item1 - 145;
        var buttonIsAvailable = false;
        for (var sample = 0; sample < 2; sample++)
        {
            if (await CloseActiveGuildDirectiveIfPresentAsync(session, pause, cancellationToken))
            {
                return true;
            }

            var accept = await recognition.FindAsync(
                "guild_directive_accept_button", buttonSearchX, 740, 290, 115, cancellationToken);
            var desist = await recognition.FindAsync(
                "guild_directive_decline_button", buttonSearchX, 740, 290, 115, cancellationToken);
            buttonIsAvailable = accept.Found && !desist.Found;
            if (!buttonIsAvailable)
            {
                break;
            }

            await Task.Delay(350, cancellationToken);
        }

        if (!buttonIsAvailable)
        {
            return await RegisterDirectiveUncertainAsync(
                session, pause, cancellationToken,
                "o botão Aceitar não foi confirmado; nenhuma área será clicada porque ela pode conter Desistir.");
        }

        await input.MoveAndClickAsync(point.Item1, point.Item2, TimeSpan.FromMilliseconds(350), cancellationToken);
        // Depois deste clique, repetir pode cancelar uma Diretiva que foi aceita
        // enquanto a captura ainda estava atualizando. Persistimos antes de
        // observar o aviso para tornar a operação idempotente.
        await MarkDirectiveHandledAsync(session, DailyCycleKey(DateTime.Now));
        var confirmationDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
        var accepted = false;
        while (DateTime.UtcNow < confirmationDeadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            if (await CloseCompletedGuildDirectiveIfPresentAsync(session, pause, cancellationToken))
            {
                return true;
            }
            if (await CloseActiveGuildDirectiveIfPresentAsync(session, pause, cancellationToken))
            {
                return true;
            }
            if ((await recognition.FindAsync("guild_directive_accepted", cancellationToken)).Found ||
                (await recognition.FindAsync("guild_directive_in_progress", cancellationToken)).Found)
            {
                accepted = true;
                break;
            }

            await Task.Delay(250, cancellationToken);
        }

        if (!accepted)
        {
            var diagnostic = await recognition.SaveDiagnosticAsync($"guild_directive_accept_{session.Options.Priority}");
            WriteLog(session,
                $"O clique em Aceitar foi enviado uma vez, mas o aviso final não apareceu. " +
                $"O ciclo foi protegido contra novo clique. Diagnóstico: {diagnostic}");
        }

        await CloseGuildScreenBestEffortAsync(session, pause, cancellationToken);
        WriteLog(session, accepted
            ? $"Diretiva aceita em {DirectiveAreaName(area)}; o jogo executará as cinco automaticamente."
            : "Diretiva encerrada em estado protegido; nenhuma nova tentativa ocorrerá neste ciclo.");
        return true;
    }

    private async Task<bool> CloseCompletedGuildDirectiveIfPresentAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        if (!await IsGuildDirectiveCompletedAsync(session, cancellationToken))
        {
            return false;
        }

        WriteLog(session, "As cinco Diretivas da Guilda já estão concluídas. Fechando a tela sem usar recarga.");
        var cycle = DailyCycleKey(DateTime.Now);
        await MarkDirectiveHandledAsync(session, cycle);
        await CloseGuildScreenBestEffortAsync(session, pause, cancellationToken);
        return true;
    }

    private async Task<bool> CloseActiveGuildDirectiveIfPresentAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        if (!await IsGuildDirectiveInProgressAsync(cancellationToken))
        {
            return false;
        }

        var cycle = DailyCycleKey(DateTime.Now);
        await MarkDirectiveHandledAsync(session, cycle);
        WriteLog(session, "Diretiva já está Em andamento; registrada neste ciclo sem tocar em Desistir.");
        await CloseGuildScreenBestEffortAsync(session, pause, cancellationToken);
        return true;
    }

    private async Task<bool> IsGuildDirectiveInProgressAsync(CancellationToken cancellationToken) =>
        (await recognition.FindAsync("guild_directive_in_progress", 650, 425, 1050, 160, cancellationToken)).Found ||
        (await recognition.FindAsync("guild_directive_decline_button", 685, 740, 975, 115, cancellationToken)).Found;

    private async Task<bool> IsGuildDirectiveCompletedAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        // O texto central existe tanto com recarga disponível quanto sem ela.
        if ((await recognition.FindAsync("guild_directive_completed", cancellationToken)).Found ||
            (await recognition.FindAsync("guild_directive_completed_alt", cancellationToken)).Found)
        {
            return true;
        }

        // Segunda evidência independente: 5/5 no rodapé, somente quando a
        // aba Diretiva está aberta. Nunca usar o botão OK como sinal de conclusão.
        if (!(await recognition.FindAsync("guild_directive_page", cancellationToken)).Found)
        {
            return false;
        }

        var frame = await CaptureClientFrameAsync(session, cancellationToken);
        return await new GuildDirectiveCounterReader().IsCompleteAsync(frame, cancellationToken);
    }

    private async Task CloseGuildScreenAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await EnsureGameForegroundAsync(session, cancellationToken);
            await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
            await CheckpointAsync(pause, cancellationToken);
            await Task.Delay(300, cancellationToken);
            var directiveVisible = (await recognition.FindAsync("guild_directive_page", cancellationToken)).Found;
            var guildVisible = (await recognition.FindAsync("guild_page", cancellationToken)).Found;
            var completedVisible = await IsGuildDirectiveCompletedAsync(session, cancellationToken);
            if (!directiveVisible && !guildVisible && !completedVisible)
            {
                WriteLog(session, $"Tela da Guilda fechada e confirmada após {attempt} ESC.");
                return;
            }

            WriteLog(session, attempt == 1
                ? "O primeiro ESC fechou apenas o aviso da Diretiva; a Guilda continua aberta."
                : $"A Guilda continua aberta após {attempt} ESC; repetindo.");
        }

        var diagnostic = await recognition.SaveDiagnosticAsync($"guild_directive_close_{session.Options.Priority}");
        throw new TimeoutException(
            $"{session.Options.Label}: a tela da Guilda não fechou após cinco ESC. Diagnóstico: {diagnostic}");
    }

    private async Task MarkDirectiveHandledAsync(ClientSession session, string cycle)
    {
        session.DirectiveCycle = cycle;
        session.DirectiveAttemptCycle = cycle;
        session.DirectiveAttemptCount = 0;
        session.NextDirectiveAttemptAt = default;
        var prefix = $"{SessionSettingPrefix(session)}.routines";
        await database.SaveSettingAsync($"{prefix}.directiveCycle", cycle);
        await database.SaveSettingAsync($"{prefix}.directiveAttemptCycle", cycle);
        await database.SaveSettingAsync($"{prefix}.directiveAttemptCount", "0");
    }

    private async Task<bool> RegisterDirectiveUncertainAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken,
        string reason)
    {
        var cycle = DailyCycleKey(DateTime.Now);
        if (!string.Equals(session.DirectiveAttemptCycle, cycle, StringComparison.Ordinal))
        {
            session.DirectiveAttemptCycle = cycle;
            session.DirectiveAttemptCount = 0;
        }

        session.DirectiveAttemptCount++;
        var prefix = $"{SessionSettingPrefix(session)}.routines";
        await database.SaveSettingAsync($"{prefix}.directiveAttemptCycle", cycle);
        await database.SaveSettingAsync(
            $"{prefix}.directiveAttemptCount",
            session.DirectiveAttemptCount.ToString(CultureInfo.InvariantCulture));
        await CloseGuildScreenBestEffortAsync(session, pause, cancellationToken);

        if (session.DirectiveAttemptCount >= 3)
        {
            await MarkDirectiveHandledAsync(session, cycle);
            WriteLog(session,
                $"Diretiva não ficou segura para clicar após três verificações ({reason}). " +
                "Ela será ignorada até o próximo ciclo para impedir repetição e inatividade.");
            return true;
        }

        var delay = session.DirectiveAttemptCount == 1 ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(10);
        session.NextDirectiveAttemptAt = DateTime.UtcNow + delay;
        WriteLog(session,
            $"Diretiva adiada por {delay.TotalMinutes:0} min: {reason} " +
            $"Tentativa segura {session.DirectiveAttemptCount}/3.");
        return false;
    }

    private async Task CloseGuildScreenBestEffortAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        try
        {
            await CloseGuildScreenAsync(session, pause, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            WritePersistentOnly(session, $"Fechamento de segurança da Guilda: {exception.Message}");
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
                await Task.Delay(250, cancellationToken);
            }
        }
    }

    private async Task<bool> AcceptDailyMissionsAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        SetStatus(BotRunState.Running, $"{session.Options.Label}: Missões Diárias", "Aceitando as 30 campanhas do ciclo");
        await input.PressKeyAsync(KeyEquals, cancellationToken: cancellationToken);
        await WaitForReferenceAsync("menu_campaign", "ícone Camp.", TimeSpan.FromSeconds(10), pause, cancellationToken);
        await input.MoveAndClickAsync(1606, 264, TimeSpan.FromMilliseconds(320), cancellationToken);
        await WaitForReferenceAsync("campaign_page", "página Campanha", TimeSpan.FromSeconds(15), pause, cancellationToken);
        await input.MoveAndClickAsync(728, 144, TimeSpan.FromMilliseconds(320), cancellationToken);
        await WaitForReferenceAsync("daily_page", "aba Diário", TimeSpan.FromSeconds(15), pause, cancellationToken);
        if (VisualRecognitionService.HasDailyThirtyCounter(capture.CapturePrimaryScreen()))
        {
            WriteLog(session, "Diárias 30/30 já aceitas neste ciclo; não clicando em Aceitar Tudo.");
            var cycle = DailyCycleKey(DateTime.Now);
            session.DailyCycle = cycle;
            await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.routines.dailyCycle", cycle);
            await CloseCampaignScreenAsync(session, pause, cancellationToken);
            return true;
        }

        await input.MoveAndClickAsync(142, 992, TimeSpan.FromMilliseconds(320), cancellationToken);
        var confirmationDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
        var accepted = false;
        while (DateTime.UtcNow < confirmationDeadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            if ((await recognition.FindAsync("daily_all_accepted", cancellationToken)).Found ||
                VisualRecognitionService.HasDailyThirtyCounter(capture.CapturePrimaryScreen()))
            {
                accepted = true;
                break;
            }

            await Task.Delay(250, cancellationToken);
        }

        if (!accepted)
        {
            var diagnostic = await recognition.SaveDiagnosticAsync($"daily_accept_{session.Options.Priority}");
            throw new TimeoutException(
                $"{session.Options.Label}: não foi possível confirmar as Diárias pelo aviso nem pelo contador 30/30. Diagnóstico: {diagnostic}");
        }

        await CloseCampaignScreenAsync(session, pause, cancellationToken);
        WriteLog(session, "As 30 Missões Diárias foram aceitas.");
        return false;
    }

    private async Task CloseCampaignScreenAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
            await CheckpointAsync(pause, cancellationToken);
            var dailyVisible = (await recognition.FindAsync("daily_page", cancellationToken)).Found;
            var campaignVisible = (await recognition.FindAsync("campaign_page", cancellationToken)).Found;
            if (!dailyVisible && !campaignVisible)
            {
                WriteLog(session, $"Tela de Campanha fechada e confirmada após {attempt} ESC.");
                return;
            }

            WriteLog(session, attempt == 1
                ? "O primeiro ESC fechou apenas o aviso; o painel de Diárias continua aberto."
                : $"O painel de Campanha continua aberto após {attempt} ESC; repetindo.");
        }

        var diagnostic = await recognition.SaveDiagnosticAsync($"daily_close_{session.Options.Priority}");
        throw new TimeoutException(
            $"{session.Options.Label}: o painel de Campanha não fechou após três ESC. Diagnóstico: {diagnostic}");
    }

    private async Task<bool> StartDailyCampaignAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        if (await TryConfirmDailyTeleportAsync(session, pause, cancellationToken, TimeSpan.FromMilliseconds(800)))
        {
            session.DailyNeedsTeleport = false;
            await ActionDelayAsync(cancellationToken, 5000, 7000);
            return await TryEnterDailyRestModeAsync(session, pause, cancellationToken);
        }

        var missionList = await EnsureDailyMissionListAsync(session, pause, cancellationToken);
        var missionY = missionList.MissionY;
        if (missionY is null)
        {
            WriteLog(session, "Missões aceitas, mas a próxima missão roxa não foi confirmada; nova tentativa será feita sem encerrar o bot.");
            return false;
        }

        WriteLog(session, $"Missão Diária roxa localizada na linha y={missionY}; selecionando a seta de teleporte.");
        await EnsureGameForegroundAsync(session, cancellationToken);
        await input.MoveAndClickAsync(1535, missionY.Value, TimeSpan.FromMilliseconds(280), cancellationToken);
        if (!await TryConfirmDailyTeleportAsync(session, pause, cancellationToken))
        {
            WriteLog(session, "Teleporte da Diária ainda não confirmado; o popup será reavaliado na próxima tentativa.");
            return false;
        }

        session.DailyNeedsTeleport = false;
        await ActionDelayAsync(cancellationToken, 5000, 7000);
        return await TryEnterDailyRestModeAsync(session, pause, cancellationToken);
    }

    private async Task<RecognitionResult?> FindDailyTeleportPopupOnClientAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        var button = await FindReferenceOnClientAsync(
            session, "daily_teleport_ok", cancellationToken, requireObservable: true);
        var resources = await FindReferenceOnClientAsync(
            session, "daily_teleport_resource", cancellationToken, requireObservable: true);
        if (button.Found && resources.Found)
        {
            return button;
        }

        // A captura independente da janela pode ter altura/escala diferente da
        // captura do monitor. Com o cliente em primeiro plano, a tela inteira
        // conserva as coordenadas originais das referências do popup.
        if (!gameWindows.IsForeground(session.Options.Target))
        {
            return null;
        }

        button = await recognition.FindAsync("daily_teleport_ok", cancellationToken);
        if (!button.Found)
        {
            return null;
        }

        resources = await recognition.FindAsync("daily_teleport_resource", cancellationToken);
        return resources.Found ? button : null;
    }

    private async Task<bool> TryConfirmDailyTeleportAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken,
        TimeSpan? popupWait = null)
    {
        await EnsureGameForegroundAsync(session, cancellationToken);
        var deadline = DateTime.UtcNow + (popupWait ?? TimeSpan.FromSeconds(8));
        RecognitionResult? popup = null;
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            popup = await FindDailyTeleportPopupOnClientAsync(session, cancellationToken);
            if (popup is not null)
            {
                break;
            }

            await Task.Delay(350, cancellationToken);
        }

        if (popup is null)
        {
            return false;
        }

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await EnsureGameForegroundAsync(session, cancellationToken);
            WriteLog(session, $"Confirmação de teleporte da Diária visível ({popup.Confidence:P0}); enviando Y — tentativa {attempt}/2.");
            await input.PressKeyAsync(KeyY, cancellationToken: cancellationToken);
            if (await WaitForDailyTeleportPopupToCloseAsync(session, pause, cancellationToken))
            {
                WriteLog(session, "Popup de teleporte fechado; prosseguindo com a campanha.");
                return true;
            }

            popup = await FindDailyTeleportPopupOnClientAsync(session, cancellationToken);
            if (popup is null)
            {
                WriteLog(session, "Popup de teleporte fechado; prosseguindo com a campanha.");
                return true;
            }
        }

        if (popup is not null)
        {
            await EnsureGameForegroundAsync(session, cancellationToken);
            WriteLog(session, "O atalho Y não fechou o popup; clicando diretamente no botão OK (Y).");
            await input.MoveAndClickAsync(popup.X, popup.Y, TimeSpan.FromMilliseconds(300), cancellationToken);
            if (await WaitForDailyTeleportPopupToCloseAsync(session, pause, cancellationToken))
            {
                return true;
            }
        }

        WriteLog(session, "O popup do teleporte continua aberto; manterei a rotina pendente para tentar novamente.");
        return false;
    }

    private async Task<bool> WaitForDailyTeleportPopupToCloseAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        var absentFrames = 0;
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            if (await FindDailyTeleportPopupOnClientAsync(session, cancellationToken) is null)
            {
                absentFrames++;
                if (absentFrames >= 2)
                {
                    return true;
                }
            }
            else
            {
                absentFrames = 0;
            }

            await Task.Delay(350, cancellationToken);
        }

        return false;
    }

    private async Task<DailyMissionListReading> EnsureDailyMissionListAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var reading = await ReadDailyMissionListAsync(session, pause, cancellationToken);
        if (reading.ListVisible)
        {
            return reading;
        }

        var cycle = DailyCycleKey(DateTime.Now);
        if (session.DailyCycle == cycle && session.DailyListToggleCycle == cycle)
        {
            // A pena alterna mostrar/ocultar. Uma tentativa por ciclo basta;
            // se o painel estiver vazio, insistir só o esconderia de novo.
            return reading;
        }

        WriteLog(session, "A lista de missões não está visível; abrindo-a uma vez pelo botão lateral.");
        await EnsureGameForegroundAsync(session, cancellationToken);
        session.DailyListToggleCycle = cycle;
        await input.MoveAndClickAsync(1879, 154, TimeSpan.FromMilliseconds(280), cancellationToken);
        return await ReadDailyMissionListAsync(session, pause, cancellationToken);
    }

    private async Task<DailyMissionListReading> ReadDailyMissionListAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        var listVisible = false;
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            var frame = await CaptureDailyMissionFrameAsync(session, cancellationToken);
            var rowsVisible = HasVisibleQuestRows(frame);
            var missionY = FindPurpleDailyMissionY(frame);
            if (missionY is not null)
            {
                // Uma missão realmente visível habilita uma futura abertura
                // da lista, caso o jogo ou usuário troque de aba depois.
                session.DailyListToggleCycle = null;
                return new DailyMissionListReading(true, missionY);
            }

            listVisible |= rowsVisible;

            await Task.Delay(300, cancellationToken);
        }

        // Sem missões no painel não há ícones de linhas para reconhecer.
        // Após abrir a pena uma vez e observar a tela por seis segundos,
        // uma lista vazia de um ciclo 30/30 é um resultado válido.
        var openedEmptyDailyList = session.DailyCycle == DailyCycleKey(DateTime.Now) &&
                                   session.DailyListToggleCycle == session.DailyCycle;
        return new DailyMissionListReading(listVisible || openedEmptyDailyList, null);
    }

    private async Task<PixelFrame> CaptureDailyMissionFrameAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CaptureClientFrameAsync(session, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (gameWindows.IsForeground(session.Options.Target))
            {
                return capture.CapturePrimaryScreen();
            }

            throw new InvalidOperationException(
                $"{session.Options.Label}: não foi possível observar a lista de missões sem confundir as janelas.",
                exception);
        }
    }

    private static bool HasVisibleQuestRows(PixelFrame frame)
    {
        // Os ícones dourados das missões repetem-se na margem esquerda da lista.
        // Radar e painel oculto não apresentam essa sequência de ícones.
        if (frame.Width < 1550 || frame.Height < 650)
        {
            return false;
        }

        var rowCenters = new List<int>();
        for (var y = 140; y < 650; y++)
        {
            var goldPixels = 0;
            for (var x = 1527; x < 1540; x++)
            {
                var offset = (y * frame.Stride) + (x * 4);
                var blue = frame.Pixels[offset];
                var green = frame.Pixels[offset + 1];
                var red = frame.Pixels[offset + 2];
                if (red >= 135 && green >= 115 && blue >= 85 &&
                    red > green && green > blue && red - blue >= 25)
                {
                    goldPixels++;
                }
            }

            if (goldPixels >= 6 && (rowCenters.Count == 0 || y - rowCenters[^1] >= 28))
            {
                rowCenters.Add(y);
                // Depois que as Diárias terminam, pode restar somente uma
                // campanha principal na lista. Exigir duas linhas fazia o
                // botão da pena ser alternado indefinidamente.
                if (rowCenters.Count >= 1)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private readonly record struct DailyMissionListReading(bool ListVisible, int? MissionY);

    private enum DailyResumeResult
    {
        Started,
        NoMission,
        Inconclusive
    }

    private static int? FindPurpleDailyMissionY(PixelFrame frame, int minimumMissionClusters = 1)
    {
        var rows = new List<(int Y, int Count)>();
        for (var y = 250; y < Math.Min(920, frame.Height); y++)
        {
            var count = 0;
            for (var x = 1480; x < Math.Min(1880, frame.Width); x++)
            {
                var offset = (y * frame.Stride) + (x * 4);
                var blue = frame.Pixels[offset];
                var green = frame.Pixels[offset + 1];
                var red = frame.Pixels[offset + 2];
                if (red >= 115 && blue >= 125 && green <= 180 &&
                    red >= green + 10 && blue >= green + 25 && blue >= red + 8)
                {
                    count++;
                }
            }

            if (count >= 16)
            {
                rows.Add((y, count));
            }
        }

        if (rows.Count == 0)
        {
            return null;
        }

        var clusters = new List<List<(int Y, int Count)>>();
        foreach (var row in rows)
        {
            if (clusters.Count == 0 || row.Y - clusters[^1][^1].Y > 3)
            {
                clusters.Add([]);
            }
            clusters[^1].Add(row);
        }

        var validMissions = clusters
            .Where(cluster => cluster.Sum(item => item.Count) >= 80)
            .OrderBy(cluster => cluster[0].Y)
            .ToArray();
        var firstMission = validMissions.Length >= minimumMissionClusters
            ? validMissions[0]
            : null;
        return firstMission is null
            ? null
            : (int)Math.Round(firstMission.Average(item => item.Y));
    }

    private static string DailyCycleKey(DateTime now) =>
        (now.TimeOfDay < TimeSpan.FromHours(4) ? now.Date.AddDays(-1) : now.Date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static readonly TimeSpan DailyShopResetAt = new(13, 1, 0);

    private static string DailyShopCycleKey(DateTime now) =>
        (now.TimeOfDay < DailyShopResetAt ? now.Date.AddDays(-1) : now.Date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static void VerifySchedulePolicy()
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        var monday = new DateTime(2026, 9, 28, 4, 0, 0);
        Check(AbbeyWeekKey(monday.AddSeconds(-1)) == "2026-09-21", "Reset semanal antecipado.");
        Check(AbbeyWeekKey(monday) == "2026-09-28", "Reset semanal ausente.");
        var shopReset = new DateTime(2026, 9, 22, 13, 1, 0);
        Check(DailyShopCycleKey(shopReset.AddSeconds(-1)) == "2026-09-21", "Reset da Loja antecipado.");
        Check(DailyShopCycleKey(shopReset) == "2026-09-22", "Reset da Loja ausente.");
        Check(ScheduledInDailyShopCycle(shopReset, TimeSpan.FromHours(4)) == shopReset, "Margem de segurança da Loja incorreta.");
        var session = new ClientSession(new AutomationClientOptions(
            "Cliente 1", new GameWindowTarget(0, "Teste", 0, false, true),
            TaDestination.Ta2, false, 1, null, WeeklyAgendaEntryLimit: 2));
        session.FarmScheduleSteps = [new(FarmScheduleDestination.Abbey, TimeSpan.FromHours(1))];
        session.AbbeyUsed = TimeSpan.FromHours(20);
        Check(IsFarmScheduleDestinationAvailable(session, FarmScheduleDestination.Abbey), "Estimativa local bloqueou saldo real.");
        session.AgendaEntries = 2;
        Check(!IsFarmScheduleDestinationAvailable(session, FarmScheduleDestination.Abbey) &&
              !IsFarmScheduleDestinationAvailable(session, FarmScheduleDestination.AnonymousDungeon), "Limite compartilhado falhou.");
        session.AbbeyInside = true;
        Check(IsFarmScheduleDestinationAvailable(session, FarmScheduleDestination.Abbey), "Limite interrompeu farm já pago.");
        session.AgendaEntries = 0;
        session.AbbeyTimeExhausted = true;
        Check(!IsFarmScheduleDestinationAvailable(session, FarmScheduleDestination.Abbey) &&
              IsFarmScheduleDestinationAvailable(session, FarmScheduleDestination.AnonymousDungeon), "Saldo de uma masmorra bloqueou a outra.");
    }

    private static DateTime ScheduledInDailyShopCycle(DateTime now, TimeSpan selectedTime)
    {
        var cycleStart = now.TimeOfDay < DailyShopResetAt ? now.Date.AddDays(-1) : now.Date;
        var effectiveTime = selectedTime < DailyShopResetAt ? DailyShopResetAt : selectedTime;
        return cycleStart + effectiveTime;
    }

    private static DateTime ScheduledInCycle(DateTime now, TimeSpan selectedTime)
    {
        var cycleStart = now.TimeOfDay < TimeSpan.FromHours(4) ? now.Date.AddDays(-1) : now.Date;
        return cycleStart + selectedTime + (selectedTime < TimeSpan.FromHours(4) ? TimeSpan.FromDays(1) : TimeSpan.Zero);
    }

    private static string DirectiveAreaName(GuildDirectiveArea area) => area switch
    {
        GuildDirectiveArea.Ta => "T.A",
        GuildDirectiveArea.Dungeon => "Masmorras",
        _ => "Mapa Aberto"
    };

    private static string SessionSettingPrefix(ClientSession session) =>
        session.Options.Label.EndsWith("2", StringComparison.Ordinal) ? "client2" : "client1";

    private async Task MonitorFarmsAsync(
        IReadOnlyList<ClientSession> sessions,
        SapherasOptions sapheras,
        AntiOverkillOptions antiOverkill,
        DailyRoutineOptions dailyRoutines,
        PauseController pause,
        CancellationToken cancellationToken,
        DateTime? stopAt)
    {
        foreach (var session in sessions.Where(session => !session.InAgenda && session.NextRecoveryAttemptAt == default))
        {
            session.Audio.Armed = true;
        }

        var priorityDetail = sessions.Count > 1
            ? " Cliente 1 tem prioridade de ação."
            : $" Somente {sessions[0].Options.Label} está ativo.";
        WriteLog(stopAt.HasValue
            ? $"Proteção de HP ativa nos {sessions.Count} cliente(s) até Sapheras.{priorityDetail}"
            : $"Monitoramento contínuo ativo nos {sessions.Count} cliente(s).{priorityDetail}");
        var nextRoutineCheckAt = DateTime.MinValue;
        var nextMailCheckAt = DateTime.MinValue;
        var nextVisibleDailyCheckAt = DateTime.MinValue;
        try
        {
            while (true)
            {
                await CheckpointAsync(pause, cancellationToken);
                if (stopAt.HasValue && DateTime.Now >= stopAt.Value)
                {
                    return;
                }

                var scheduleAdvanced = false;
                foreach (var scheduledSession in sessions.OrderBy(item => item.Options.Priority))
                {
                    if (await TryAdvanceFarmScheduleAsync(scheduledSession, pause, cancellationToken))
                    {
                        scheduleAdvanced = true;
                        break;
                    }
                }
                if (scheduleAdvanced)
                {
                    continue;
                }

                foreach (var routineSession in sessions.OrderBy(item => item.Options.Priority))
                {
                    if (routineSession.NextRoutinePanelRecoveryAt == default ||
                        DateTime.UtcNow < routineSession.NextRoutinePanelRecoveryAt)
                    {
                        continue;
                    }

                    await RecoverOpenRoutinePanelsSafelyAsync(
                        routineSession, dailyRoutines, pause, cancellationToken);
                    break;
                }

                if (DateTime.Now >= nextMailCheckAt &&
                    (!stopAt.HasValue || stopAt.Value - DateTime.Now > sapheras.DirectSapherasWindow))
                {
                    nextMailCheckAt = DateTime.Now.AddSeconds(30);
                    foreach (var mailSession in sessions.OrderBy(item => item.Options.Priority))
                    {
                        if (await TryCollectDueMailSafelyAsync(mailSession, pause, cancellationToken))
                        {
                            break;
                        }
                    }
                }

                if (DateTime.Now >= nextVisibleDailyCheckAt &&
                    (!stopAt.HasValue || stopAt.Value - DateTime.Now > sapheras.DirectSapherasWindow))
                {
                    nextVisibleDailyCheckAt = DateTime.Now.AddSeconds(30);
                    foreach (var routineSession in sessions.OrderBy(item => item.Options.Priority))
                    {
                        if (await TryStartVisibleDailyCampaignSafelyAsync(
                                routineSession, dailyRoutines, pause, cancellationToken))
                        {
                            break;
                        }
                    }
                }

                if (DateTime.Now >= nextRoutineCheckAt)
                {
                    nextRoutineCheckAt = DateTime.Now.AddSeconds(20);
                    foreach (var routineSession in sessions.OrderBy(item => item.Options.Priority))
                    {
                        if (await RunDueDailyRoutinesSafelyAsync(routineSession, dailyRoutines, pause, cancellationToken))
                        {
                            break;
                        }
                    }
                }

                foreach (var session in sessions.OrderBy(session => session.Options.Priority))
                {
                    if (session.InDailyCampaign && DateTime.UtcNow >= session.NextDailyMissionCheckAt)
                    {
                        try
                        {
                            var normalHunt = await FindReferenceOnClientAsync(
                                session, "caca_automatica", cancellationToken, requireObservable: true);
                            var dailyHunt = await FindReferenceOnClientAsync(
                                session, "daily_automatic", cancellationToken, requireObservable: true);
                            session.DailyNormalHuntHits = normalHunt.Found && !dailyHunt.Found
                                ? session.DailyNormalHuntHits + 1
                                : 0;
                            if (session.DailyNormalHuntHits >= 2)
                            {
                                await MarkDailyCampaignCompletedAsync(
                                    session,
                                    "A tela de descanso voltou para Caça automática em uso e a Campanha automática desapareceu; Diárias concluídas.");
                                await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
                                break;
                            }

                            if (session.DailyNeedsTeleport)
                            {
                                session.NextDailyMissionCheckAt = DateTime.UtcNow.AddSeconds(15);
                                var resumeResult = await ResumeDailyCampaignAsync(session, pause, cancellationToken);
                                session.DailyNeedsTeleport = resumeResult == DailyResumeResult.Inconclusive;
                                if (resumeResult == DailyResumeResult.Started)
                                {
                                    var startedCycle = session.DailyCycle ?? DailyCycleKey(DateTime.Now);
                                    session.DailyStartedCycle = startedCycle;
                                    await database.SaveSettingAsync(
                                        $"{SessionSettingPrefix(session)}.routines.dailyStartedCycle",
                                        startedCycle);
                                    session.DailyNoMissionHits = 0;
                                    session.NextDailyMissionCheckAt = DateTime.UtcNow.AddMinutes(2);
                                    WriteLog(session, "Teleporte pendente da Diária recuperado; campanha em andamento.");
                                }
                                else if (resumeResult == DailyResumeResult.NoMission)
                                {
                                    await MarkDailyCampaignCompletedAsync(
                                        session,
                                        "Lista confirmada sem missão roxa; Diárias concluídas. Retornando ao farm anterior.");
                                    await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
                                    break;
                                }
                                else
                                {
                                    WriteLog(session, "Teleporte da Diária ainda pendente; mantendo o monitoramento e tentando novamente em 15 segundos.");
                                }
                            }
                            else
                            {
                                var missionList = await CheckDailyMissionListAsync(session, pause, cancellationToken);
                                session.DailyNoMissionHits = missionList.MissionY is not null
                                    ? 0
                                    : missionList.ListVisible
                                        ? session.DailyNoMissionHits + 1
                                        : 0;
                                session.NextDailyMissionCheckAt = DateTime.UtcNow.Add(
                                    missionList.MissionY is not null
                                        ? TimeSpan.FromMinutes(2)
                                        : TimeSpan.FromSeconds(15));
                                if (session.DailyNoMissionHits >= 3)
                                {
                                    await MarkDailyCampaignCompletedAsync(
                                        session,
                                        "Missões roxas ausentes por três verificações da lista; Diárias concluídas. Retornando ao farm anterior.");
                                    await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: false);
                                    break;
                                }
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            session.NextDailyMissionCheckAt = DateTime.UtcNow.AddSeconds(15);
                            WriteLog(session, $"Falha recuperável ao acompanhar as Diárias: {exception.GetBaseException().Message}. Nova tentativa em 15 segundos.");
                            WritePersistentOnly(session, exception.ToString());
                        }
                    }

                    // Morte sempre vence uma retomada pendente. Antes, uma falha no
                    // retorno à T.A podia manter o cliente preso em novas tentativas
                    // de menu e nunca deixar a sinalização de morte ser atendida.
                    if (!session.InAgenda && !session.HandlingDeath)
                    {
                        var recoveryIsDue = session.NextRecoveryAttemptAt != default &&
                                            DateTime.UtcNow >= session.NextRecoveryAttemptAt;
                        if (Volatile.Read(ref session.PendingVisualDeath) == 0 && recoveryIsDue)
                        {
                            var visibleDeath = await FindDeathOnClientAsync(session, cancellationToken);
                            if (visibleDeath.Found)
                            {
                                Interlocked.Exchange(ref session.PendingVisualDeath, 1);
                                WriteLog(
                                    session,
                                    $"Morte encontrada antes da retomada da T.A ({visibleDeath.Confidence:P0}); cancelando a retomada normal.");
                            }
                        }

                        if (Interlocked.Exchange(ref session.PendingVisualDeath, 0) != 0)
                        {
                            session.NextRecoveryAttemptAt = default;
                            WriteLog(session, "Atendendo a morte sinalizada pela vigilância visual independente.");
                            try
                            {
                                await HandleDeathAsync(session, sapheras, antiOverkill, pause, cancellationToken);
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception exception)
                            {
                                RecoverSessionAfterActionFailure(session, exception, "restauração da morte");
                            }

                            break;
                        }
                    }

                    if (session.NextRecoveryAttemptAt != default &&
                        DateTime.UtcNow >= session.NextRecoveryAttemptAt &&
                        !session.InAgenda && !session.HandlingDeath)
                    {
                        session.NextRecoveryAttemptAt = default;
                        WriteLog(session, "Intervalo de segurança concluído; retomando o fluxo interrompido.");
                        if (!session.IsFarmingTa)
                        {
                            var resumed = await RunRecoveryActionSafelyAsync(
                                session,
                                "retomada do fluxo após falha",
                                async actionToken =>
                                {
                                    if (session.NeedsDeathRestoration)
                                    {
                                        WriteLog(session, "Restauração pendente: concluindo a lápide antes de voltar ao farm.");
                                        await ActivateGameForEmergencyAsync(session, actionToken);
                                        await RestoreDeathResourcesAsync(session, pause, actionToken);
                                    }

                                    if (session.AwaitingHuntActivationAtSpot &&
                                        !session.RequiresHardFlowReset)
                                    {
                                        await ResumeHuntAtCurrentSpotAsync(
                                            session,
                                            true,
                                            pause,
                                            actionToken);
                                        return;
                                    }

                                    if (session.RequiresHardFlowReset &&
                                        (!session.AbbeyInside || session.ConsecutiveRecoveryFailures >= 3))
                                    {
                                        await ResetStalledClientRouteAsync(session, sapheras, pause, actionToken);
                                    }

                                    await EnterConfiguredFarmAsync(session, pause, actionToken, isEmergency: true);
                                },
                                cancellationToken);
                            if (!resumed)
                            {
                                break;
                            }
                        }

                        session.Audio.Armed = !session.InAgenda && session.IsFarmingTa;
                    }

                    if (session.InAgenda)
                    {
                        session.Audio.Armed = false;
                        if (DateTime.Now >= session.AgendaUntil)
                        {
                            WriteLog(session, "Tempo seguro da Agenda concluído; retomando o farm configurado.");
                            await ExitAgendaAsync(session, sapheras, pause, cancellationToken, resumeFarm: true);
                            break;
                        }

                        continue;
                    }

                    if (session.NextRecoveryAttemptAt != default)
                    {
                        continue;
                    }

                    if (session.Audio.TryConsumeAlert(out var confidence))
                    {
                        WriteLog(session, $"Alerta sonoro de HP baixo confirmado ({confidence:P0}). Atendendo este cliente agora.");
                        Interlocked.Exchange(ref session.PendingVisualLowHp, 0);
                        session.Audio.Armed = false;
                        try
                        {
                            await EmergencyReturnAsync(session, sapheras, antiOverkill, pause, cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            RecoverSessionAfterActionFailure(session, exception, "TP de emergência por áudio");
                        }

                        break;
                    }

                    if (Interlocked.Exchange(ref session.PendingVisualLowHp, 0) != 0)
                    {
                        session.Audio.Armed = false;
                        try
                        {
                            await RecoverAfterBackgroundEmergencyAsync(
                                session, sapheras, antiOverkill, pause, cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            RecoverSessionAfterActionFailure(session, exception, "TP visual de emergência");
                        }

                        break;
                    }

                    session.Audio.Armed = true;
                    if (!session.Audio.IsHealthy && !session.AudioFailureLogged)
                    {
                        session.AudioFailureLogged = true;
                        WriteLog(session, "Áudio de HP indisponível; tentando reconectar. A barra de HP mantém a proteção visual de contingência.");
                    }
                    continue;
                }

                SetStatus(BotRunState.Running, "Clientes farmando", stopAt.HasValue
                    ? $"Sapheras em {FormatDuration(stopAt.Value - DateTime.Now)}"
                    : "Áudio seletivo monitorado por cliente");
                await Task.Delay(100, cancellationToken);
            }
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
            {
                foreach (var session in sessions)
                {
                    session.Audio.Armed = false;
                }
            }
        }
    }

    private async Task MarkDailyCampaignCompletedAsync(
        ClientSession session,
        string message)
    {
        session.InDailyCampaign = false;
        session.DailyNeedsTeleport = false;
        session.DailyNoMissionHits = 0;
        session.DailyNormalHuntHits = 0;
        var completedCycle = session.DailyCycle ?? DailyCycleKey(DateTime.Now);
        session.DailyCompletedCycle = completedCycle;
        await database.SaveSettingAsync(
            $"{SessionSettingPrefix(session)}.routines.dailyCompletedCycle",
            completedCycle);
        WriteLog(session, message);
    }

    private async Task<DailyResumeResult> ResumeDailyCampaignAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        await ActivateGameAsync(session, cancellationToken);
        await ExitRestIfNeededAsync(session, pause, cancellationToken);
        if (await TryConfirmDailyTeleportAsync(session, pause, cancellationToken, TimeSpan.FromMilliseconds(800)))
        {
            await ActionDelayAsync(cancellationToken, 5000, 7000);
            var existingPopupRest = await TryEnterDailyRestModeAsync(session, pause, cancellationToken);
            session.DailyNeedsTeleport = false;
            session.IsFarmingTa = true;
            session.SafeInRest = existingPopupRest;
            session.Audio.Armed = true;
            return DailyResumeResult.Started;
        }

        var confirmedEmptyLists = 0;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0 &&
                await TryConfirmDailyTeleportAsync(session, pause, cancellationToken, TimeSpan.FromMilliseconds(800)))
            {
                await ActionDelayAsync(cancellationToken, 5000, 7000);
                var recoveredRest = await TryEnterDailyRestModeAsync(session, pause, cancellationToken);
                session.DailyNeedsTeleport = false;
                session.IsFarmingTa = true;
                session.SafeInRest = recoveredRest;
                session.Audio.Armed = true;
                return DailyResumeResult.Started;
            }

            var missionList = await EnsureDailyMissionListAsync(session, pause, cancellationToken);
            var missionY = missionList.MissionY;
            if (missionY is null)
            {
                if (missionList.ListVisible)
                {
                    confirmedEmptyLists++;
                    if (confirmedEmptyLists >= 2)
                    {
                        return DailyResumeResult.NoMission;
                    }
                }
                else
                {
                    confirmedEmptyLists = 0;
                    WriteLog(session, "Retomada das Diárias inconclusiva: painel de missões não confirmado; não marcando a rotina como concluída.");
                }
                continue;
            }

            await EnsureGameForegroundAsync(session, cancellationToken);
            await input.MoveAndClickAsync(1535, missionY.Value, TimeSpan.FromMilliseconds(260), cancellationToken);
            if (!await TryConfirmDailyTeleportAsync(session, pause, cancellationToken))
            {
                WriteLog(session, "Clique na próxima missão realizado, mas o teleporte ainda não foi confirmado; reavaliando a tela.");
                continue;
            }

            await ActionDelayAsync(cancellationToken, 5000, 7000);
            var restConfirmed = await TryEnterDailyRestModeAsync(session, pause, cancellationToken);
            session.DailyNeedsTeleport = false;
            session.IsFarmingTa = true;
            session.SafeInRest = restConfirmed;
            session.Audio.Armed = true;
            return DailyResumeResult.Started;
        }

        return DailyResumeResult.Inconclusive;
    }

    private async Task<DailyMissionListReading> CheckDailyMissionListAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        await ActivateGameAsync(session, cancellationToken);
        var missionList = await ReadDailyMissionListAsync(session, pause, cancellationToken);
        if (!missionList.ListVisible)
        {
            await ExitRestIfNeededAsync(session, pause, cancellationToken);
            missionList = await EnsureDailyMissionListAsync(session, pause, cancellationToken);
            session.SafeInRest = await TryEnterDailyRestModeAsync(session, pause, cancellationToken);
        }

        WriteLog(session, missionList.MissionY is not null
            ? $"Verificação das Diárias: missão roxa ainda ativa na linha y={missionList.MissionY}."
            : missionList.ListVisible
                ? "Verificação das Diárias: lista aberta, sem missão roxa visível."
                : "Verificação das Diárias inconclusiva: lista não confirmada; mantendo a campanha ativa.");
        return missionList;
    }

    private async Task<bool> TryEnterDailyRestModeAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await TryDismissAgendaAsync(session, cancellationToken);
            await input.PressKeyAsync(KeyL, cancellationToken: cancellationToken);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                await CheckpointAsync(pause, cancellationToken);
                if (await TryDismissAgendaAsync(session, cancellationToken))
                {
                    WriteLog(session, "Aviso da Agenda encerrada removido antes de confirmar o descanso das Diárias.");
                    break;
                }

                if ((await recognition.FindAsync("daily_automatic", cancellationToken)).Found ||
                    await FindRestStateAsync(cancellationToken) is not null)
                {
                    WriteLog(session, "Modo descanso das Diárias confirmado.");
                    return true;
                }

                await Task.Delay(350, cancellationToken);
            }
        }

        WriteLog(session, "Não foi possível confirmar o descanso; mantendo as Diárias em execução na tela normal.");
        return false;
    }

    private async Task MonitorClientWindowAsync(
        ClientSession session,
        int emergencyTeleportVirtualKey,
        CancellationToken cancellationToken)
    {
        var lastCaptureFailureLog = DateTime.MinValue;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!session.Audio.IsHealthy && DateTime.UtcNow >= session.NextAudioRestartAt)
                {
                    await TryRestartAudioCaptureAsync(session, cancellationToken);
                }

                if (session.WindowCapture is null)
                {
                    await ReconnectWindowCaptureAsync(session, cancellationToken);
                }

                var frame = await CaptureClientFrameAsync(session, cancellationToken);
                session.LastWindowFrameAt = DateTime.UtcNow;
                if (session.VisualCaptureFaulted)
                {
                    session.VisualCaptureFaulted = false;
                    WriteLog(session, "Vigilância visual da janela reconectada com sucesso.");
                }

                if (session.InAgenda || session.HandlingDeath)
                {
                    session.DeathVisualHits = 0;
                    session.LowHpVisualHits = 0;
                    await Task.Delay(120, cancellationToken);
                    continue;
                }

                if (!session.Audio.Armed && !session.VisualEmergencyIssued)
                {
                    // A proteção sonora não deve ficar desarmada durante
                    // deslocamento, compras ou confirmação de telas.
                    session.Audio.Armed = true;
                }

                if (session.Audio.TryConsumeAlert(out var audioConfidence) && !session.VisualEmergencyIssued)
                {
                    session.VisualEmergencyIssued = true;
                    session.BackgroundEmergencySource = "áudio";
                    WriteLog(session, $"Alerta sonoro de HP baixo ({audioConfidence:P0}) recebido durante uma ação; enviando TP em segundo plano imediatamente.");
                    await IssueWatchdogEmergencyTeleportAsync(session, emergencyTeleportVirtualKey, cancellationToken);
                    await Task.Delay(120, cancellationToken);
                    continue;
                }

                var hp = HpBarAnalyzer.Measure(frame);
                if (session.AbbeyInside &&
                    DateTime.UtcNow - session.LastAbbeyTimeReadUtc >= TimeSpan.FromSeconds(30))
                {
                    session.LastAbbeyTimeReadUtc = DateTime.UtcNow;
                    try
                    {
                        var remaining = await _abbeyTimeReader.ReadAsync(frame, cancellationToken);
                        if (remaining.Remaining is { } time)
                        {
                            session.AbbeyTimeLowHits = time == TimeSpan.Zero
                                ? session.AbbeyTimeLowHits + 1
                                : 0;
                            if (session.AbbeyTimeLowHits >= 2)
                            {
                                Interlocked.Exchange(ref session.PendingAbbeyTimeExhausted, 1);
                            }
                        }
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        WritePersistentOnly(session, $"Leitura do tempo da Abadia indisponível: {exception.Message}");
                    }
                }
                if (session.AnonymousDungeonInside &&
                    DateTime.UtcNow - session.LastAnonymousTimeReadUtc >= TimeSpan.FromSeconds(30))
                {
                    session.LastAnonymousTimeReadUtc = DateTime.UtcNow;
                    try
                    {
                        var remaining = await _abbeyTimeReader.ReadAsync(frame, cancellationToken);
                        if (remaining.Remaining is { } time)
                        {
                            session.AnonymousTimeLowHits = time == TimeSpan.Zero
                                ? session.AnonymousTimeLowHits + 1
                                : 0;
                            if (session.AnonymousTimeLowHits >= 2)
                                Interlocked.Exchange(ref session.PendingAnonymousTimeExhausted, 1);
                        }
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        WritePersistentOnly(session, $"Leitura do tempo do Estreito de Tenerys indisponível: {exception.Message}");
                    }
                }
                if (hp.Found && hp.Percent <= 0.35)
                {
                    session.LowHpVisualHits++;
                    if (session.LowHpVisualHits == 1)
                    {
                        session.LowHpFirstObservedUtc = DateTime.UtcNow;
                    }
                    var allowVisualFallback = !session.Audio.IsHealthy ||
                        DateTime.UtcNow - session.LowHpFirstObservedUtc >= TimeSpan.FromSeconds(2);
                    if (session.LowHpVisualHits >= 3 && allowVisualFallback &&
                        !session.Audio.HasPendingAlert && !session.VisualEmergencyIssued)
                    {
                        session.VisualEmergencyIssued = true;
                        session.BackgroundEmergencySource = "imagem";
                        WriteLog(session, $"HP visual crítico ({hp.Percent:P0}) persistiu sem alerta sonoro; contingência enviando o TP em segundo plano.");
                        await IssueWatchdogEmergencyTeleportAsync(session, emergencyTeleportVirtualKey, cancellationToken);
                    }
                }
                else
                {
                    session.LowHpVisualHits = 0;
                    session.LowHpFirstObservedUtc = default;
                    if (hp.Found && hp.Percent >= 0.60)
                    {
                        session.VisualEmergencyIssued = false;
                    }
                }

                var death = await FindDeathInFrameAsync(frame, cancellationToken);
                if (death.Found)
                {
                    session.DeathVisualHits++;
                    if (session.DeathVisualHits >= 2 && Interlocked.Exchange(ref session.PendingVisualDeath, 1) == 0)
                    {
                        WriteLog(session, $"Morte confirmada na janela em segundo plano ({death.Confidence:P0}; 2 quadros consecutivos).");
                        CancelRecoveryActionForDeath(session);
                    }

                    await Task.Delay(180, cancellationToken);
                    continue;
                }

                // O pequeno ícone de restauração também aparece em telas normais
                // e, sozinho, gerava falsos positivos. Ele nunca inicia uma morte;
                // a confirmação precisa vir da tela completa "Você morreu".
                if (!death.Found)
                {
                    session.DeathVisualHits = 0;
                }

                await Task.Delay(120, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                session.VisualCaptureFaulted = true;
                if (DateTime.UtcNow - lastCaptureFailureLog >= TimeSpan.FromSeconds(10))
                {
                    lastCaptureFailureLog = DateTime.UtcNow;
                    WriteLog(
                        session,
                        $"Vigilância visual temporariamente indisponível: {exception.GetBaseException().Message}. Tentando novamente.");
                }

                try
                {
                    await ReconnectWindowCaptureAsync(session, cancellationToken);
                }
                catch (Exception reconnectException) when (!cancellationToken.IsCancellationRequested)
                {
                    WritePersistentOnly(session, $"Falha ao reconectar captura visual: {reconnectException}");
                }

                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private async Task IssueWatchdogEmergencyTeleportAsync(
        ClientSession session,
        int emergencyTeleportVirtualKey,
        CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await input.PressKeyToWindowAsync(
                    session.Options.Target.Handle,
                    emergencyTeleportVirtualKey,
                    cancellationToken: cancellationToken);
                await Task.Delay(300, cancellationToken);
            }

            await Task.Delay(900, cancellationToken);
            var frame = await CaptureClientFrameAsync(session, cancellationToken);
            var hp = HpBarAnalyzer.Measure(frame);
            var cityVisible = false;
            foreach (var reference in new[] { "ta1_chegada", "ta2_chegada", "ta3_chegada" })
            {
                if ((await recognition.FindAsync(reference, frame, cancellationToken)).Found)
                {
                    cityVisible = true;
                    break;
                }
            }

            var death = await FindDeathInFrameAsync(frame, cancellationToken);
            if (!cityVisible && !death.Found && hp.Found && hp.Percent <= 0.35)
            {
                WriteLog(session, "HP continua crítico e o TP em segundo plano não confirmou chegada; usando foco somente para a emergência.");
                if (gameWindows.Activate(session.Options.Target) &&
                    gameWindows.IsForeground(session.Options.Target))
                {
                    await input.PressEmergencyKeyAsync(emergencyTeleportVirtualKey, cancellationToken);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref session.PendingVisualLowHp, 1);
        }
    }

    private async Task ReconnectWindowCaptureAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        await session.WindowCaptureGate.WaitAsync(cancellationToken);
        try
        {
            if (session.WindowCapture is not null)
            {
                await session.WindowCapture.DisposeAsync();
                session.WindowCapture = null;
            }

            session.WindowCapture = new GameWindowCaptureSession(session.Options.Target);
        }
        finally
        {
            session.WindowCaptureGate.Release();
        }
    }

    private async Task TryRestartAudioCaptureAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        if (!await session.AudioRestartGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            session.NextAudioRestartAt = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            var shouldRemainArmed = session.Audio.Armed;
            await session.Audio.StopAsync();
            await session.Audio.StartAsync(session.Options.Target.ProcessId, cancellationToken);
            session.Audio.Armed = shouldRemainArmed;
            session.AudioFailureLogged = false;
            session.AudioStartFaulted = false;
            WriteLog(session, "Proteção de áudio seletivo reconectada com sucesso.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            session.AudioStartFaulted = true;
            session.NextAudioRestartAt = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            if (!session.AudioFailureLogged)
            {
                session.AudioFailureLogged = true;
                WriteLog(session, "Áudio seletivo caiu; tentando reconectar sem interromper a vigilância visual.");
            }

            WritePersistentOnly(session, $"Falha ao reconectar áudio seletivo: {exception}");
        }
        finally
        {
            session.AudioRestartGate.Release();
        }
    }

    private void RecoverSessionAfterActionFailure(
        ClientSession session,
        Exception exception,
        string action)
    {
        session.Audio.Armed = false;
        session.SafeInRest = false;
        session.ConsecutiveRecoveryFailures++;
        session.RequiresHardFlowReset = session.ConsecutiveRecoveryFailures >= 2;
        var retryDelaySeconds = session.ConsecutiveRecoveryFailures switch
        {
            1 => 5,
            2 => 10,
            3 => 20,
            _ => 30
        };
        session.NextRecoveryAttemptAt = DateTime.UtcNow + TimeSpan.FromSeconds(retryDelaySeconds);
        WriteLog(
            session,
            $"FALHA RECUPERÁVEL durante {action}: {exception.GetBaseException().Message}. " +
            $"O outro cliente continua sendo monitorado; retomarei este fluxo automaticamente em {retryDelaySeconds}s " +
            "sem repetir cliques durante a espera." +
            (session.RequiresHardFlowReset
                ? " A próxima retomada fará uma recuperação controlada da interface, sem gastar teleporte."
                : string.Empty));
        WritePersistentOnly(session, exception.ToString());
    }

    private async Task ResetStalledClientRouteAsync(
        ClientSession session,
        SapherasOptions options,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        SetStatus(
            BotRunState.Running,
            $"{session.Options.Label}: reiniciando rota",
            "Fechando telas presas sem usar teleporte");
        _ = options;
        await ActivateGameForEmergencyAsync(session, cancellationToken);
        var death = await FindDeathOnClientAsync(session, cancellationToken);
        if (death.Found || Volatile.Read(ref session.PendingVisualDeath) != 0)
        {
            Interlocked.Exchange(ref session.PendingVisualDeath, 1);
            throw new InvalidOperationException(
                $"{session.Options.Label}: morte detectada antes do reset da rota; a ressurreição terá prioridade.");
        }

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await CheckpointAsync(pause, cancellationToken);
            if (!await IsKnownBlockingOverlayVisibleAsync(cancellationToken))
            {
                break;
            }

            WriteLog(session, $"Recuperação visual: fechando a tela atual com Esc — tentativa {attempt}/5.");
            await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
            await Task.Delay(350, cancellationToken);
        }

        session.Audio.Armed = false;
        session.SafeInRest = false;
        session.IsFarmingTa = false;
        session.AwaitingHuntActivationAtSpot = false;
        session.AwaitingFavoriteSpotRecognition = false;
        death = await FindDeathOnClientAsync(session, cancellationToken);
        if (death.Found || Volatile.Read(ref session.PendingVisualDeath) != 0)
        {
            Interlocked.Exchange(ref session.PendingVisualDeath, 1);
            throw new InvalidOperationException(
                $"{session.Options.Label}: morte detectada durante o reset da rota; a ressurreição terá prioridade.");
        }

        session.RequiresHardFlowReset = false;
        WriteLog(session, "Interface recuperada sem teleporte; o destino configurado será reaberto e validado na próxima tentativa.");
    }

    private async Task ResumeHuntAtCurrentSpotAsync(
        ClientSession session,
        bool isTaFarm,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        SetStatus(
            BotRunState.Running,
            $"{session.Options.Label}: retomando caça",
            "Repetindo somente L → Q → L no spot atual");
        WriteLog(
            session,
            "O personagem já chegou ao spot; repetindo somente a ativação local da caça, sem refazer a rota.");
        await ActivateGameForEmergencyAsync(session, cancellationToken);
        await StartAutomaticHuntAsync(session, pause, cancellationToken);
        session.IsFarmingTa = isTaFarm;
        session.SafeInRest = true;
        session.Audio.Armed = true;
        session.ConsecutiveRecoveryFailures = 0;
        session.RequiresHardFlowReset = false;
        WriteLog(session, "Caça recuperada no spot atual; fluxo normal restabelecido.");
    }

    private async Task<bool> IsKnownBlockingOverlayVisibleAsync(CancellationToken cancellationToken)
    {
        string[] references =
        [
            "mapa_aberto",
            "mapa_aberto_ta2_ir",
            "loja_artigos",
            "seletor_ta",
            "tela_masmorras",
            "painel_restauracao",
            "agenda_tela",
            "menu_ta",
            "menu_masmorra",
            "daily_shop_page",
            "daily_shop_bulk_popup",
            "guild_page",
            "guild_directive_page",
            "campaign_page",
            "daily_page",
            "mail_page"
        ];
        foreach (var reference in references)
        {
            if ((await recognition.FindAsync(reference, cancellationToken)).Found)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> RunSessionActionSafelyAsync(
        ClientSession session,
        string action,
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            RecoverSessionAfterActionFailure(session, exception, action);
            return false;
        }
    }

    private async Task<bool> RunRecoveryActionSafelyAsync(
        ClientSession session,
        string action,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        using var actionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Interlocked.Exchange(ref session.RecoveryActionCancellation, actionCancellation);
        try
        {
            await operation(actionCancellation.Token);
            return true;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            Volatile.Read(ref session.PendingVisualDeath) != 0)
        {
            session.NextRecoveryAttemptAt = default;
            WriteLog(session, "Retomada interrompida imediatamente porque a morte foi detectada.");
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            RecoverSessionAfterActionFailure(session, exception, action);
            return false;
        }
        finally
        {
            Interlocked.CompareExchange(ref session.RecoveryActionCancellation, null, actionCancellation);
        }
    }

    private static void CancelRecoveryActionForDeath(ClientSession session)
    {
        var actionCancellation = Volatile.Read(ref session.RecoveryActionCancellation);
        if (actionCancellation is null)
        {
            return;
        }

        try
        {
            actionCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A ação terminou entre a leitura e o cancelamento.
        }
    }

    private async Task EmergencyReturnAsync(
        ClientSession session,
        SapherasOptions options,
        AntiOverkillOptions antiOverkill,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        _ = session.Audio.TryConsumeAlert(out _);
        session.SafeInRest = false;
        session.IsFarmingTa = false;
        session.AwaitingHuntActivationAtSpot = false;
        SetStatus(BotRunState.Running, $"{session.Options.Label}: proteção acionada", "Alerta sonoro de HP baixo · enviando TP");
        await ActivateGameForEmergencyAsync(session, cancellationToken);
        var deathBeforeTeleport = await FindDeathOnClientAsync(session, cancellationToken);
        if (Volatile.Read(ref session.PendingVisualDeath) != 0 || deathBeforeTeleport.Found)
        {
            WriteLog(session, $"O alerta chegou após a morte ({deathBeforeTeleport.Confidence:P0}); iniciando restauração.");
            await HandleDeathAsync(session, options, antiOverkill, pause, cancellationToken);
            return;
        }

        WriteLog(session, $"Enviando um TP de emergência ({options.EmergencyTeleportKeyName}); sem repetição cega que gaste gold.");
        await input.PressEmergencyKeyAsync(options.EmergencyTeleportVirtualKey, cancellationToken);

        var deathAfterTeleport = await WaitForDeathAfterEmergencyAsync(
            session,
            TimeSpan.FromSeconds(5),
            cancellationToken);
        if (deathAfterTeleport is not null)
        {
            WriteLog(session, $"Morte ocorreu durante a tentativa de TP ({deathAfterTeleport.Confidence:P0}); iniciando restauração.");
            await HandleDeathAsync(session, options, antiOverkill, pause, cancellationToken);
            return;
        }

        // Não tentamos deduzir qual cidade abriu por um nome de mapa. A prova
        // útil é o fluxo completo responder e a chegada ao destino configurado ser
        // confirmada antes de declarar a recuperação concluída.
        WriteLog(
            session,
            $"TP enviado sem tela de morte; retornando a {ConfiguredFarmName(session)}. " +
            "A recuperação só será concluída após a chegada ser confirmada visualmente.");
        await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: true);
    }

    private async Task RecoverAfterBackgroundEmergencyAsync(
        ClientSession session,
        SapherasOptions options,
        AntiOverkillOptions antiOverkill,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        session.SafeInRest = false;
        session.IsFarmingTa = false;
        session.AwaitingHuntActivationAtSpot = false;
        SetStatus(BotRunState.Running, $"{session.Options.Label}: proteção de HP", $"TP por {session.BackgroundEmergencySource} enviado em segundo plano");
        var death = await WaitForDeathAfterEmergencyAsync(session, TimeSpan.FromSeconds(5), cancellationToken);
        if (death is not null)
        {
            WriteLog(session, $"Morte confirmada após o TP visual ({death.Confidence:P0}); iniciando restauração.");
            await HandleDeathAsync(session, options, antiOverkill, pause, cancellationToken);
            return;
        }

        // PostMessage confirma somente que a mensagem entrou na fila da janela;
        // jogos DirectX podem ignorá-la. Não presumir que o TP ocorreu.
        var arrivedInTown = false;
        foreach (var reference in new[] { "ta1_chegada", "ta2_chegada", "ta3_chegada" })
        {
            if ((await FindReferenceOnClientAsync(session, reference, cancellationToken, requireObservable: true)).Found)
            {
                arrivedInTown = true;
                break;
            }
        }

        if (!arrivedInTown)
        {
            WriteLog(session, "O TP em segundo plano não teve chegada confirmada. Dando foco apenas para o TP de emergência.");
            await ActivateGameForEmergencyAsync(session, cancellationToken);
            await input.PressEmergencyKeyAsync(options.EmergencyTeleportVirtualKey, cancellationToken);
            death = await WaitForDeathAfterEmergencyAsync(session, TimeSpan.FromSeconds(4), cancellationToken);
            if (death is not null)
            {
                await HandleDeathAsync(session, options, antiOverkill, pause, cancellationToken);
                return;
            }
        }

        await RecordAbbeyExitAsync(session);
        WriteLog(session, $"Proteção visual executada sem morte; retomando {ConfiguredFarmName(session)}.");
        await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: true);
    }

    private async Task<RecognitionResult?> WaitForDeathAfterEmergencyAsync(
        ClientSession session,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var death = await FindDeathOnClientAsync(session, cancellationToken);
            if (death.Found)
            {
                Interlocked.Exchange(ref session.PendingVisualDeath, 1);
                return death;
            }

            if (Volatile.Read(ref session.PendingVisualDeath) != 0)
            {
                return new RecognitionResult(true, death.Confidence, death.X, death.Y);
            }

            await Task.Delay(300, cancellationToken);
        }

        return null;
    }

    private async Task HandleDeathAsync(
        ClientSession session,
        SapherasOptions sapheras,
        AntiOverkillOptions antiOverkill,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        if (session.HandlingDeath)
        {
            return;
        }

        session.HandlingDeath = true;
        session.Audio.Armed = false;
        _ = session.Audio.TryConsumeAlert(out _);
        Interlocked.Exchange(ref session.PendingVisualDeath, 0);
        session.SafeInRest = false;
        session.IsFarmingTa = false;
        session.AwaitingHuntActivationAtSpot = false;
        try
        {
            await ClearRestorationIconAbsenceAsync(session);
            await RecordAbbeyExitAsync(session);
            SetStatus(BotRunState.Running, $"{session.Options.Label}: personagem morreu", "Ressuscitando e restaurando recursos");
            // A tela de morte tem contagem regressiva curta; devolver o foco
            // rapidamente evita que o renascimento automático passe antes do
            // clique no botão inferior de Ressuscitar.
            await ActivateGameForEmergencyAsync(session, cancellationToken);
            await RestoreDeathResourcesAsync(session, pause, cancellationToken);

            if (RegisterDeath(session, antiOverkill))
            {
                session.Deaths.Clear();
                await StartAgendaAsync(session, antiOverkill, pause, cancellationToken);
                return;
            }

            WriteLog(session, $"Restauração concluída; retomando {ConfiguredFarmName(session)}.");
            await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: true);
        }
        finally
        {
            session.HandlingDeath = false;
            if (!session.InAgenda && session.IsFarmingTa)
            {
                session.Audio.Armed = true;
            }
        }
    }

    private bool RegisterDeath(ClientSession session, AntiOverkillOptions antiOverkill)
    {
        if (!session.Options.EnableAntiOverkill)
        {
            WriteLog(session, "Morte registrada; Anti Over Kill desativado para este cliente.");
            return false;
        }

        var now = DateTime.Now;
        session.Deaths.Enqueue(now);
        while (session.Deaths.Count > 0 && now - session.Deaths.Peek() > antiOverkill.DeathWindow)
        {
            session.Deaths.Dequeue();
        }

        WriteLog(
            session,
            $"Morte registrada: {session.Deaths.Count}/{antiOverkill.DeathThreshold} " +
            $"na janela de {antiOverkill.DeathWindow.TotalMinutes:F0} min.");
        return session.Deaths.Count >= antiOverkill.DeathThreshold;
    }

    private async Task RememberRestorationIconAbsenceAsync(ClientSession session)
    {
        session.RestorationAbsentCycle = DailyCycleKey(DateTime.Now);
        await database.SaveSettingAsync(
            $"{SessionSettingPrefix(session)}.restoration.absentCycle",
            session.RestorationAbsentCycle);
    }

    private async Task ClearRestorationIconAbsenceAsync(ClientSession session)
    {
        session.RestorationAbsentCycle = null;
        await database.SaveSettingAsync($"{SessionSettingPrefix(session)}.restoration.absentCycle", "");
    }

    private async Task RestoreDeathResourcesAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        session.NeedsDeathRestoration = true;
        // O Cliente 2 pode sinalizar "Morte" primeiro dentro do descanso e só
        // depois abrir a tela completa com o botão Ressuscitar. Esperamos essa
        // transição sem clicar às cegas na tela normal.
        var fullDeath = await FindFullDeathOnClientAsync(session, cancellationToken);
        if (!fullDeath.Found)
        {
            WriteLog(session, "Estado Morte reconhecido; aguardando a tela completa de Ressuscitar.");
            var transitionStartedAt = DateTime.UtcNow;
            var transitionDeadline = transitionStartedAt + TimeSpan.FromSeconds(12);
            while (DateTime.UtcNow < transitionDeadline)
            {
                await CheckpointAsync(pause, cancellationToken);
                fullDeath = await FindFullDeathOnClientAsync(session, cancellationToken);
                if (fullDeath.Found)
                {
                    break;
                }

                var anyDeath = await FindDeathOnClientAsync(session, cancellationToken);
                if (DateTime.UtcNow - transitionStartedAt >= TimeSpan.FromSeconds(3) && !anyDeath.Found)
                {
                    break;
                }

                await Task.Delay(220, cancellationToken);
            }
        }

        var confirmedDeathScreen = fullDeath.Found;
        if (fullDeath.Found)
        {
            WriteLog(
                session,
                $"Tela completa de morte confirmada ({fullDeath.Confidence:P0}); aguardando 5 segundos antes de Ressuscitar.");
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            await ActivateGameForEmergencyAsync(session, cancellationToken);
            fullDeath = await FindFullDeathOnClientAsync(session, cancellationToken);
            if (!fullDeath.Found)
            {
                WriteLog(session, "O renascimento automático ocorreu durante a espera; não clicando na tela normal.");
            }

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                if (!fullDeath.Found)
                {
                    break;
                }

                WriteLog(session, $"Clicando em Ressuscitar (1811, 994) — tentativa {attempt}/2.");
                await input.ClickAsync(1811, 994, cancellationToken);
                var disappearDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                while (DateTime.UtcNow < disappearDeadline)
                {
                    await CheckpointAsync(pause, cancellationToken);
                    if (!(await FindFullDeathOnClientAsync(session, cancellationToken)).Found)
                    {
                        break;
                    }

                    await Task.Delay(220, cancellationToken);
                }

                if (!(await FindFullDeathOnClientAsync(session, cancellationToken)).Found)
                {
                    break;
                }

                fullDeath = await FindFullDeathOnClientAsync(session, cancellationToken);

                if (attempt < 2)
                {
                    WriteLog(session, $"Ressuscitar ainda visível; repetindo o clique — tentativa {attempt + 1}/2.");
                }
                else
                {
                    var diagnostic = await recognition.SaveDiagnosticAsync($"ressuscitar_{session.Options.Priority}");
                    throw new TimeoutException(
                        $"{session.Options.Label}: o botão Ressuscitar permaneceu aberto após duas tentativas. Diagnóstico: {diagnostic}");
                }
            }
        }
        else
        {
            WriteLog(session, "O renascimento automático já ocorreu; seguindo para restaurar a Perda de EXP.");
        }

        WriteLog(session, "Aguardando o personagem e os indicadores de perda estabilizarem.");
        await ActionDelayAsync(cancellationToken, 1800, 2600);
        await ActivateGameForEmergencyAsync(session, cancellationToken);
        var panelCounter = await WaitForRestorationCounterAsync(
            session, TimeSpan.FromSeconds(4), pause, cancellationToken);
        if (panelCounter.State != RestorationCountState.Unknown)
        {
            WriteLog(session, "O painel de restauração já está aberto; evitando clique desnecessário.");
        }
        else
        {
            if ((await FindReferenceOnClientAsync(
                    session, "painel_restauracao", cancellationToken, requireObservable: true)).Found)
            {
                var diagnostic = await recognition.SaveDiagnosticAsync($"restauracao_contador_{session.Options.Priority}");
                WriteLog(session, $"Painel de restauração aberto, mas contador ilegível; fechando sem clicar em itens e retomando o fluxo. Diagnóstico: {diagnostic}");
                await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
                session.NeedsDeathRestoration = false;
                return;
            }

            WriteLog(session, "Verificando se esta morte gerou lápide de restauração.");
            // Após um TP por HP, a ausência estável da lápide não é uma falha
            // recuperável: pode não ter ocorrido morte nem perda neste servidor.
            var iconTimeout = confirmedDeathScreen ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(6);
            var iconDeadline = DateTime.UtcNow + iconTimeout;
            var iconFound = false;
            var iconConfirmations = 0;
            while (DateTime.UtcNow < iconDeadline)
            {
                await CheckpointAsync(pause, cancellationToken);
                var iconFrame = await CaptureClientFrameAsync(session, cancellationToken);
                var icon = await recognition.FindAsync("icone_perda_exp", iconFrame, cancellationToken);
                if (icon.Found && TombstoneIconAnalyzer.HasRedIcon(iconFrame))
                {
                    iconConfirmations++;
                    if (iconConfirmations >= 2)
                    {
                        iconFound = true;
                        WriteLog(session, $"Ícone de perda confirmado em dois quadros ({icon.Confidence:P0}); abrindo a lápide em (1537, 72).");
                        break;
                    }
                }
                else
                {
                    iconConfirmations = 0;
                }

                await Task.Delay(400, cancellationToken);
            }

            if (!iconFound)
            {
                WriteLog(
                    session,
                    "Lápide ausente após verificação limitada; não há EXP ou equipamento para restaurar. Retomando o fluxo sem repetir a busca.");
                session.NeedsDeathRestoration = false;
                await RememberRestorationIconAbsenceAsync(session);
                return;
            }

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await EnsureGameForegroundAsync(session, cancellationToken);
                WriteLog(session, $"Clicando na lápide em (1537, 72) — tentativa {attempt}/3.");
                await input.MoveAndClickAsync(1537, 72, TimeSpan.FromMilliseconds(450), cancellationToken);
                panelCounter = await WaitForRestorationCounterAsync(
                    session, TimeSpan.FromSeconds(6), pause, cancellationToken);
                if (panelCounter.State != RestorationCountState.Unknown)
                {
                    break;
                }
            }

            if (panelCounter.State == RestorationCountState.Unknown)
            {
                if (await ConfirmRestorationIconAbsentAsync(session, pause, cancellationToken))
                {
                    WriteLog(session, "Lápide não está presente após os cliques; não há restauração desta morte. Seguindo para o farm.");
                    session.NeedsDeathRestoration = false;
                    await RememberRestorationIconAbsenceAsync(session);
                    return;
                }

                if (!(await FindReferenceOnClientAsync(
                        session, "painel_restauracao", cancellationToken, requireObservable: true)).Found)
                {
                    WriteLog(session, "A lápide não abriu um painel após três tentativas; deixando de procurar nesta ocorrência e retomando o farm.");
                    session.NeedsDeathRestoration = false;
                    await RememberRestorationIconAbsenceAsync(session);
                    return;
                }

                var diagnosticFrame = await CaptureClientFrameAsync(session, cancellationToken);
                var diagnostic = await recognition.SaveDiagnosticAsync(
                    $"painel_restauracao_{session.Options.Priority}",
                    diagnosticFrame);
                WriteLog(session, $"Painel da lápide não pôde ser lido após três tentativas. Fechando e retomando sem clicar às cegas. Diagnóstico: {diagnostic}");
                await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
                session.NeedsDeathRestoration = false;
                return;
            }
        }

        WriteLog(session, "Painel confirmado pelo contador; restaurando até cada lista ficar vazia.");
        if (panelCounter.Tab == RestorationTab.Experience)
        {
            await RestoreVisibleResourceTabAsync(
                session, RestorationTab.Experience, 285, 886, pause, cancellationToken);
            WriteLog(session, "Verificando a aba de equipamento em (47, 275).");
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await EnsureGameForegroundAsync(session, cancellationToken);
                await input.ClickAsync(47, 275, cancellationToken);
                panelCounter = await WaitForRestorationCounterAsync(
                    session, TimeSpan.FromSeconds(5), pause, cancellationToken,
                    RestorationTab.Equipment);
                if (panelCounter.Tab == RestorationTab.Equipment)
                {
                    break;
                }
            }

            if (panelCounter.Tab == RestorationTab.Equipment)
            {
                await RestoreVisibleResourceTabAsync(
                    session, RestorationTab.Equipment, 259, 886, pause, cancellationToken);
            }
            else if (panelCounter.Tab == RestorationTab.Experience)
            {
                WriteLog(session, "A aba de equipamento não apareceu; apenas a perda de EXP estava disponível.");
            }
            else
            {
                throw new InvalidOperationException(
                    $"{session.Options.Label}: não foi possível identificar a segunda aba de restauração.");
            }
        }
        else if (panelCounter.Tab == RestorationTab.Equipment)
        {
            await RestoreVisibleResourceTabAsync(
                session, RestorationTab.Equipment, 259, 886, pause, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException(
                $"{session.Options.Label}: o painel abriu, mas a aba de recursos não pôde ser identificada.");
        }

        WriteLog(session, "Contadores de restauração zerados; fechando o painel com Esc.");
        await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
        var closeDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var closedConfirmations = 0;
        while (DateTime.UtcNow < closeDeadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            panelCounter = await ReadRestorationCounterAsync(session, cancellationToken);
            if (panelCounter.State == RestorationCountState.Unknown)
            {
                closedConfirmations++;
                if (closedConfirmations >= 3)
                {
                    break;
                }
            }
            else
            {
                closedConfirmations = 0;
            }

            await Task.Delay(350, cancellationToken);
        }

        if (closedConfirmations < 3)
        {
            throw new TimeoutException($"{session.Options.Label}: o painel de restauração não fechou após Esc.");
        }

        WriteLog(session, "Painel de restauração fechado.");
        session.NeedsDeathRestoration = false;
    }

    private async Task<bool> ConfirmRestorationIconAbsentAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < 3; index++)
        {
            await CheckpointAsync(pause, cancellationToken);
            var icon = await FindReferenceOnClientAsync(
                session, "icone_perda_exp", cancellationToken, requireObservable: true);
            if (icon.Found)
            {
                return false;
            }

            await Task.Delay(350, cancellationToken);
        }

        return true;
    }

    private async Task RestoreVisibleResourceTabAsync(
        ClientSession session,
        RestorationTab expectedTab,
        int clickX,
        int clickY,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var tabName = expectedTab == RestorationTab.Experience ? "EXP" : "equipamento";
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await CheckpointAsync(pause, cancellationToken);
            await EnsureGameForegroundAsync(session, cancellationToken);
            var current = await WaitForRestorationCounterAsync(
                session, TimeSpan.FromSeconds(5), pause, cancellationToken);
            if (current.Tab != expectedTab)
            {
                throw new InvalidOperationException(
                    $"{session.Options.Label}: aba de {tabName} não identificada antes do clique. OCR: '{current.RawText}'.");
            }

            if (current.State == RestorationCountState.Empty)
            {
                WriteLog(session, $"Lista de {tabName} já vazia ({current.Count}); nenhum clique necessário.");
                return;
            }

            WriteLog(session, $"Restaurando aba de {tabName} em ({clickX}, {clickY}) — tentativa {attempt}/3.");
            await input.ClickAsync(clickX, clickY, cancellationToken);
            var afterClick = await WaitForRestorationCounterAsync(
                session, TimeSpan.FromSeconds(8), pause, cancellationToken,
                expectedTab, RestorationCountState.Empty);
            if (afterClick.Tab == expectedTab && afterClick.State == RestorationCountState.Empty)
            {
                WriteLog(session, $"Lista de {tabName} vazia confirmada pelo contador.");
                return;
            }

            if (attempt < 3)
            {
                WriteLog(session, $"A lista de {tabName} ainda não zerou; repetindo somente este clique.");
            }
        }

        throw new InvalidOperationException(
            $"{session.Options.Label}: a lista de {tabName} não ficou vazia após três tentativas; " +
            "não vou retornar ao farm com restauração pendente.");
    }

    private async Task<RestorationCounterResult> ReadRestorationCounterAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        PixelFrame frame;
        try
        {
            frame = await CaptureClientFrameAsync(session, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (!gameWindows.IsForeground(session.Options.Target))
            {
                throw new InvalidOperationException(
                    $"{session.Options.Label}: a aba de restauração não pôde ser observada com segurança.",
                    exception);
            }

            frame = capture.CapturePrimaryScreen();
        }

        return await _restorationCounterReader.ReadClientFrameAsync(frame, cancellationToken);
    }

    private async Task<RestorationCounterResult> WaitForRestorationCounterAsync(
        ClientSession session,
        TimeSpan timeout,
        PauseController pause,
        CancellationToken cancellationToken,
        RestorationTab? expectedTab = null,
        RestorationCountState? expectedState = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = new RestorationCounterResult(RestorationTab.Unknown, null, null, string.Empty);
        var confirmations = 0;
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            last = await ReadRestorationCounterAsync(session, cancellationToken);
            var matches = last.State != RestorationCountState.Unknown &&
                          (expectedTab is null || last.Tab == expectedTab) &&
                          (expectedState is null || last.State == expectedState);
            confirmations = matches ? confirmations + 1 : 0;
            if (confirmations >= 2)
            {
                return last;
            }

            await Task.Delay(300, cancellationToken);
        }

        return last;
    }

    private async Task StartAgendaAsync(
        ClientSession session,
        AntiOverkillOptions antiOverkill,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        SetStatus(BotRunState.Running, $"{session.Options.Label}: Anti Over Kill", "Iniciando período seguro na Agenda");
        // Todas as ações da Agenda são enviadas ao cliente correto, mesmo quando
        // o outro cliente ou qualquer outra janela estiver em primeiro plano.
        await ActivateGameAsync(session, cancellationToken);
        await ExitRestIfNeededAsync(session, pause, cancellationToken);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var agendaButton = await recognition.FindAsync("menu_agenda", cancellationToken);
            var taButton = agendaButton.Found
                ? new RecognitionResult(false, 0, 0, 0)
                : await recognition.FindAsync("menu_ta", cancellationToken);
            if (agendaButton.Found || taButton.Found)
            {
                break;
            }

            await input.PressKeyAsync(KeyEquals, cancellationToken: cancellationToken);
            var menuOpened = await WaitForAnyReferenceInRegionAsync(
                new[] { "menu_agenda", "menu_ta", "menu_masmorra" },
                1550, 160, 340, 520,
                TimeSpan.FromSeconds(7), pause, cancellationToken);
            if (menuOpened is not null)
            {
                break;
            }

            if (attempt == 3)
            {
                var diagnostic = await recognition.SaveDiagnosticAsync($"menu_agenda_{session.Options.Priority}");
                throw new TimeoutException($"{session.Options.Label}: não foi possível abrir o menu da Agenda. Diagnóstico: {diagnostic}");
            }
        }

        WriteLog(session, "Abrindo Agenda em (1740, 514).");
        await input.ClickAsync(1740, 514, cancellationToken);
        await WaitForReferenceAsync("agenda_tela", "tela da Agenda", TimeSpan.FromSeconds(15), pause, cancellationToken);
        WriteLog(session, "Redefinindo a Agenda e iniciando o modo seguro.");
        await input.ClickAsync(1359, 1004, cancellationToken);
        await input.ClickAsync(1783, 1006, cancellationToken);
        await ActionDelayAsync(cancellationToken, 2200, 3000);
        if ((await recognition.FindAsync("agenda_tela", cancellationToken)).Found)
        {
            await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
        }

        session.InAgenda = true;
        session.AgendaUntil = DateTime.Now + antiOverkill.AgendaDuration;
        session.SafeInRest = true;
        session.IsFarmingTa = false;
        session.Audio.Armed = false;
        WriteLog(session, $"Agenda ativa até {session.AgendaUntil:HH:mm:ss}; este cliente está em segurança.");
    }

    private async Task ExitAgendaAsync(
        ClientSession session,
        SapherasOptions sapheras,
        PauseController pause,
        CancellationToken cancellationToken,
        bool resumeFarm)
    {
        SetStatus(BotRunState.Running, $"{session.Options.Label}: saindo da Agenda", "Retornando à cidade");
        await ActivateGameAsync(session, cancellationToken);
        await ExitRestIfNeededAsync(session, pause, cancellationToken);
        WriteLog(session, $"Encerrando Agenda com um TP {sapheras.EmergencyTeleportKeyName}.");
        await input.PressEmergencyKeyAsync(sapheras.EmergencyTeleportVirtualKey, cancellationToken);

        await ActionDelayAsync(cancellationToken, 4200, 5600);
        await input.PressKeyAsync(KeyY, cancellationToken: cancellationToken);
        await ActionDelayAsync(cancellationToken, 1800, 2200);
        session.InAgenda = false;
        session.SafeInRest = false;
        session.AgendaUntil = default;
        WriteLog(session, "Popup de encerramento da Agenda tratado com Y.");
        if (resumeFarm)
        {
            await EnterConfiguredFarmAsync(session, pause, cancellationToken, isEmergency: true);
        }
    }

    private async Task EnsureHigherPriorityClientsSafeAsync(
        IReadOnlyList<ClientSession> sessions,
        ClientSession current,
        CancellationToken cancellationToken)
    {
        foreach (var previous in sessions.Where(session => session.Options.Priority < current.Options.Priority))
        {
            if (!previous.SafeInRest)
            {
                // A prioridade controla a ordem das ações, mas nunca pode bloquear
                // permanentemente o outro cliente. O watchdog de cada janela segue
                // ativo e o cliente atual continua seu fluxo de forma independente.
                WriteLog(current,
                    $"{previous.Options.Label} ainda não confirmou descanso; prosseguindo com este cliente " +
                    "sem interromper a proteção independente.");
            }
        }

        await Task.Delay(100, cancellationToken);
    }

    private async Task ExitRestIfNeededAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        await CheckpointAsync(pause, cancellationToken);
        if (await FindRestStateAsync(cancellationToken) is null)
        {
            return;
        }

        if (!await TryCloseRestPanelAsync(
                session,
                pause,
                "abrir menus",
                cancellationToken))
        {
            var diagnostic = await recognition.SaveDiagnosticAsync(
                $"descanso_nao_fechou_menu_{session.Options.Priority}");
            throw new TimeoutException(
                $"{session.Options.Label}: a tela de descanso não fechou após três comandos L. " +
                $"Diagnóstico: {diagnostic}");
        }
    }

    private async Task<bool> TryCloseRestPanelAsync(
        ClientSession session,
        PauseController pause,
        string purpose,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await CheckpointAsync(pause, cancellationToken);
            var state = await FindRestStateAsync(cancellationToken);
            if (state is null)
            {
                return true;
            }

            WriteLog(
                session,
                $"Saindo do descanso para {purpose} — comando L {attempt}/3 " +
                $"('{RestStateDescription(state.Value.ReferenceId)}', {state.Value.Result.Confidence:P0}).");
            await EnsureGameForegroundAsync(session, cancellationToken);
            if (await FindRestStateAsync(cancellationToken) is null)
            {
                return true;
            }

            await input.PressKeyAsync(
                KeyL,
                TimeSpan.FromMilliseconds(90 + (attempt * 55)),
                cancellationToken);
            if (await WaitForRestStateToDisappearAsync(
                    TimeSpan.FromSeconds(7), pause, cancellationToken))
            {
                WriteLog(session, "Tela de descanso fechada e confirmada.");
                return true;
            }

            WriteLog(session, "O comando L não fechou o descanso; reafirmando o foco e repetindo localmente.");
        }

        return false;
    }

    private async Task<(string ReferenceId, RecognitionResult Result)?> TryOpenRestPanelAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await CheckpointAsync(pause, cancellationToken);
            var existing = await FindRestStateAsync(cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            WriteLog(session, $"Reabrindo o descanso com L — tentativa {attempt}/3.");
            await EnsureGameForegroundAsync(session, cancellationToken);
            existing = await FindRestStateAsync(cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            await input.PressKeyAsync(
                KeyL,
                TimeSpan.FromMilliseconds(90 + (attempt * 55)),
                cancellationToken);
            var opened = await WaitForRestStateAsync(
                TimeSpan.FromSeconds(8), pause, cancellationToken);
            if (opened is not null)
            {
                return opened;
            }

            WriteLog(session, "O descanso ainda não abriu; reafirmando o foco sem repetir Q.");
        }

        return null;
    }

    private async Task<(string ReferenceId, RecognitionResult Result)?> FindRestStateAsync(
        CancellationToken cancellationToken)
    {
        var frame = capture.CapturePrimaryScreen();
        foreach (var referenceId in RestStateReferences)
        {
            var result = await recognition.FindAsync(referenceId, frame, cancellationToken);
            if (result.Found)
            {
                return (referenceId, result);
            }
        }

        return null;
    }

    private async Task<(string ReferenceId, RecognitionResult Result)?> WaitForRestStateAsync(
        TimeSpan timeout,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            await CheckpointAsync(pause, cancellationToken);
            var state = await FindRestStateAsync(cancellationToken);
            if (state is not null)
            {
                return state;
            }

            await Task.Delay(350, cancellationToken);
        }

        return null;
    }

    private async Task<bool> WaitForRestStateToDisappearAsync(
        TimeSpan timeout,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var clearConfirmations = 0;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            await CheckpointAsync(pause, cancellationToken);
            if (await FindRestStateAsync(cancellationToken) is null)
            {
                clearConfirmations++;
                if (clearConfirmations >= 2)
                {
                    return true;
                }
            }
            else
            {
                clearConfirmations = 0;
            }

            await Task.Delay(350, cancellationToken);
        }

        return false;
    }

    private static string RestStateDescription(string referenceId) => referenceId switch
    {
        "caca_automatica" => "Caça automática",
        "descanso_aguardando_spot" => "Aguardando",
        "descanso_movendo" => "Movendo-se",
        "descanso_ponto_fixo" => "Ponto fixo",
        "descanso_morte" => "Morte no descanso",
        _ => "Tela de descanso"
    };

    private async Task<bool> TryDismissAgendaAsync(ClientSession session, CancellationToken cancellationToken)
    {
        var agenda = await recognition.FindAsync("aviso_agenda", cancellationToken);
        var genericAgendaPopup = agenda.Found
            ? new RecognitionResult(false, 0, 0, 0)
            : await recognition.FindAsync("agenda_popup_ok", cancellationToken);
        if (!agenda.Found && !genericAgendaPopup.Found)
        {
            return false;
        }

        var confidence = Math.Max(agenda.Confidence, genericAgendaPopup.Confidence);
        WriteLog(session, $"Aviso central da Agenda detectado ({confidence:P0}); fechando com Y.");
        await input.PressKeyAsync(KeyY, cancellationToken: cancellationToken);
        return true;
    }

    private async Task<bool> WaitForReferenceToAppearAsync(
        string referenceId,
        TimeSpan timeout,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            await CheckpointAsync(pause, cancellationToken);
            if ((await recognition.FindAsync(referenceId, cancellationToken)).Found)
            {
                return true;
            }

            await Task.Delay(180, cancellationToken);
        }

        return false;
    }

    private async Task WaitForReferenceToDisappearAsync(
        string referenceId,
        TimeSpan timeout,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            await CheckpointAsync(pause, cancellationToken);
            if (!(await recognition.FindAsync(referenceId, cancellationToken)).Found)
            {
                return;
            }

            await Task.Delay(450, cancellationToken);
        }

        throw new TimeoutException($"A tela '{referenceId}' não desapareceu dentro do tempo esperado.");
    }

    private async Task<RecognitionResult?> WaitForAnyReferenceInRegionAsync(
        IReadOnlyList<string> referenceIds,
        int searchX,
        int searchY,
        int searchWidth,
        int searchHeight,
        TimeSpan timeout,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            await CheckpointAsync(pause, cancellationToken);
            var current = await FindAnyReferenceInRegionOnceAsync(
                referenceIds, searchX, searchY, searchWidth, searchHeight, cancellationToken);
            if (current is not null)
            {
                return current;
            }

            await Task.Delay(450, cancellationToken);
        }

        return null;
    }

    private async Task<RecognitionResult?> FindAnyReferenceInRegionOnceAsync(
        IReadOnlyList<string> referenceIds,
        int searchX,
        int searchY,
        int searchWidth,
        int searchHeight,
        CancellationToken cancellationToken)
    {
        foreach (var referenceId in referenceIds)
        {
            var current = await recognition.FindAsync(
                referenceId, searchX, searchY, searchWidth, searchHeight, cancellationToken);
            if (current.Found)
            {
                return current;
            }
        }

        return null;
    }

    private async Task<RecognitionResult> WaitForReferenceAsync(
        string referenceId,
        string description,
        TimeSpan timeout,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var grace = TimeSpan.FromSeconds(Math.Min(20, Math.Max(5, timeout.TotalSeconds * 0.75)));
        var graceLogged = false;
        var best = new RecognitionResult(false, 0, 0, 0);
        while (DateTime.UtcNow - startedAt < timeout + grace)
        {
            await CheckpointAsync(pause, cancellationToken);
            if (!graceLogged && DateTime.UtcNow - startedAt >= timeout)
            {
                graceLogged = true;
                WriteLog($"{description}: aguardando mais {grace.TotalSeconds:F0}s por carregamento lento; melhor confiança até agora {best.Confidence:P0}.");
            }
            // Mesmo quadro para alvo e aviso; as duas leituras são paralelas.
            // Isso reage imediatamente quando o painel aparece, sem sacrificar
            // a prioridade de fechar um aviso que esteja por cima dele.
            var frame = capture.CapturePrimaryScreen();
            var targetTask = recognition.FindAsync(referenceId, frame, cancellationToken);
            var agendaTask = referenceId == "aviso_agenda"
                ? targetTask
                : recognition.FindAsync("aviso_agenda", frame, cancellationToken);
            await Task.WhenAll(targetTask, agendaTask);
            var agenda = await agendaTask;
            if (referenceId != "aviso_agenda" && agenda.Found)
            {
                WriteLog($"Aviso de agenda reconhecido ({agenda.Confidence:P0}); fechando com Y e retomando a etapa.");
                await input.PressKeyAsync(KeyY, cancellationToken: cancellationToken);
                continue;
            }

            var current = await targetTask;
            if (current.Confidence > best.Confidence)
            {
                best = current;
            }

            if (current.Found)
            {
                return current;
            }

            await Task.Delay(180, cancellationToken);
        }

        var diagnostic = await recognition.SaveDiagnosticAsync(referenceId);
        throw new TimeoutException($"Tempo esgotado procurando {description}. Melhor confiança: {best.Confidence:P0}. Diagnóstico: {diagnostic}");
    }

    private async Task ActivateGameAsync(ClientSession session, CancellationToken cancellationToken)
    {
        var target = session.Options.Target;
        if (!gameWindows.Activate(target))
        {
            throw new InvalidOperationException($"Não foi possível ativar a janela {target.Title}.");
        }

        // Activate já confirma foco e janela maximizada. A captura abaixo espera
        // o próximo quadro do jogo, sem impor 1,8 s em todo PC.
        await Task.Delay(180, cancellationToken);
        await DismissWemadeOfferIfPresentAsync(session, cancellationToken);
    }

    private async Task ActivateGameForEmergencyAsync(ClientSession session, CancellationToken cancellationToken)
    {
        var target = session.Options.Target;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (gameWindows.IsForeground(target) || gameWindows.Activate(target))
            {
                await Task.Delay(180, cancellationToken);
                if (gameWindows.IsForeground(target))
                {
                    await DismissWemadeOfferIfPresentAsync(session, cancellationToken);
                    return;
                }
            }

            if (attempt < 3)
            {
                await Task.Delay(300, cancellationToken);
            }
        }

        throw new InvalidOperationException($"Não foi possível ativar a janela {target.Title} para o TP de emergência.");
    }

    private async Task EnsureGameForegroundAsync(ClientSession session, CancellationToken cancellationToken)
    {
        var target = session.Options.Target;
        if (gameWindows.IsForeground(target))
        {
            await DismissWemadeOfferIfPresentAsync(session, cancellationToken);
            return;
        }

        if (!gameWindows.Activate(target))
        {
            throw new InvalidOperationException($"Não foi possível devolver o foco à janela {target.Title}.");
        }

        await Task.Delay(180, cancellationToken);
        await DismissWemadeOfferIfPresentAsync(session, cancellationToken);
    }

    private async Task<bool> DismissWemadeOfferIfPresentAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var offer = await FindReferenceOnClientAsync(
                session, "oferta_wemade", cancellationToken, requireObservable: true);
            if (!offer.Found)
            {
                return attempt > 1;
            }

            WriteLog(
                session,
                $"Oferta da WeMade detectada ({offer.Confidence:P0}); fechando em (1233, 387) — tentativa {attempt}/3.");
            await input.MoveAndClickAsync(
                1233, 387, TimeSpan.FromMilliseconds(320), cancellationToken);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(300, cancellationToken);
                var remaining = await FindReferenceOnClientAsync(
                    session, "oferta_wemade", cancellationToken, requireObservable: true);
                if (!remaining.Found)
                {
                    WriteLog(session, "Oferta da WeMade fechada; retomando a etapa atual.");
                    return true;
                }
            }
        }

        var frame = await CaptureClientFrameAsync(session, cancellationToken);
        var diagnostic = await recognition.SaveDiagnosticAsync(
            $"oferta_wemade_{session.Options.Priority}", frame);
        throw new TimeoutException(
            $"{session.Options.Label}: a oferta da WeMade permaneceu aberta após três tentativas. Diagnóstico: {diagnostic}");
    }

    private async Task CheckpointAsync(PauseController pause, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await pause.WaitIfPausedAsync(cancellationToken);
    }

    private Task ActionDelayAsync(CancellationToken cancellationToken, int minimumMilliseconds = 1800, int maximumMilliseconds = 2200) =>
        Task.Delay(_random.Next(minimumMilliseconds, maximumMilliseconds + 1), cancellationToken);

    private int ChooseNextSpot(ClientSession session, int level, int count)
    {
        if (session.LastFarmSpotLevel != level)
        {
            session.LastFarmSpot = -1;
            session.LastFarmSpotLevel = level;
        }

        var choices = Enumerable.Range(0, count).Where(index => index != session.LastFarmSpot).ToArray();
        session.LastFarmSpot = choices[_random.Next(choices.Length)];
        return session.LastFarmSpot;
    }

    private static string ArrivalReference(TaDestination destination) => destination switch
    {
        TaDestination.Ta1Codex => "ta1_chegada",
        TaDestination.Ta2 => "ta2_chegada",
        _ => "ta3_chegada"
    };
    private static string EntryReadyReference(TaDestination destination) => destination switch
    {
        TaDestination.Ta1Codex => "entrar_ta1_pronto",
        TaDestination.Ta2 => "entrar_ta2_pronto",
        _ => "entrar_ta3_pronto"
    };
    private static string TaName(TaDestination destination) => destination switch
    {
        TaDestination.Ta1Codex => "T.A 1 (Codex)",
        TaDestination.Ta2 => "T.A 2",
        _ => "T.A 3"
    };

    private static FarmScheduleStep? CurrentFarmScheduleStep(ClientSession session) =>
        session.FarmScheduleCompleted || session.FarmScheduleSteps.Count == 0
            ? null
            : session.FarmScheduleSteps[session.FarmScheduleIndex];

    private static TaDestination EffectiveTaDestination(ClientSession session) =>
        CurrentFarmScheduleStep(session)?.Destination switch
        {
            FarmScheduleDestination.Ta1 => TaDestination.Ta1Codex,
            FarmScheduleDestination.Ta2 => TaDestination.Ta2,
            FarmScheduleDestination.Ta3 => TaDestination.Ta3,
            _ => session.Options.Destination
        };

    private static FarmCoordinate? EffectiveCustomFarmCoordinate(ClientSession session)
    {
        var destination = EffectiveTaDestination(session);
        if (session.Options.CustomFarmCoordinates?.TryGetValue(destination, out var coordinate) == true)
            return coordinate;
        return destination == session.Options.Destination ? session.Options.CustomFarmCoordinate : null;
    }

    private static bool WantsAbbey(ClientSession session) =>
        CurrentFarmScheduleStep(session) is { } step
            ? step.Destination == FarmScheduleDestination.Abbey
            : session.FarmScheduleSteps.Count > 0
                ? false
                : session.Options.UseAbbey;

    private static bool WantsAnonymousDungeon(ClientSession session) =>
        CurrentFarmScheduleStep(session)?.Destination == FarmScheduleDestination.AnonymousDungeon;

    private static bool AbbeyIsAvailable(ClientSession session) =>
        !session.AbbeyTimeExhausted;

    private static bool CanPayAgendaEntry(ClientSession session) =>
        session.FarmScheduleSteps.Count == 0 ||
        session.AgendaEntries < session.Options.WeeklyAgendaEntryLimit;

    private async Task RecordAgendaPaidEntryAsync(ClientSession session, string destination)
    {
        if (session.FarmScheduleSteps.Count == 0)
            return;

        session.AgendaEntries++;
        await SaveAbbeyBudgetStateAsync(session);
        WriteLog(
            session,
            $"Entrada paga da Agenda confirmada em {destination} " +
            $"({session.AgendaEntries}/{session.Options.WeeklyAgendaEntryLimit} nesta semana).");
    }

    private static string ScheduleDestinationName(FarmScheduleDestination destination) => destination switch
    {
        FarmScheduleDestination.Abbey => "Abadia",
        FarmScheduleDestination.AnonymousDungeon => "Estreito de Tenerys",
        FarmScheduleDestination.Ta1 => "T.A 1 (legado)",
        FarmScheduleDestination.Ta2 => "T.A 2 (legado)",
        _ => "T.A 3 (legado)"
    };

    private static string AbbeyWeekKey(DateTime now)
    {
        var shifted = now.AddHours(-4);
        var daysSinceMonday = ((int)shifted.DayOfWeek + 6) % 7;
        return shifted.Date.AddDays(-daysSinceMonday).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string ConfiguredFarmName(ClientSession session) =>
        WantsAnonymousDungeon(session)
            ? "o Estreito de Tenerys"
            : WantsAbbey(session) && AbbeyIsAvailable(session) &&
              (session.AbbeyInside || (!session.AbbeyEntryMayHaveBeenCharged && CanPayAgendaEntry(session)))
            ? "a Abadia"
            : $"a {TaName(EffectiveTaDestination(session))}";

    private static string FormatDuration(TimeSpan duration)
    {
        var safe = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return safe.TotalHours >= 1
            ? safe.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : safe.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private void EnsureResolution()
    {
        var (width, height) = capture.GetPrimaryScreenSize();
        if (width != 1920 || height != 1080)
        {
            throw new InvalidOperationException($"O monitor principal precisa estar em 1920×1080. Detectado: {width}×{height}.");
        }
    }

    private void LogMovementState(ClientSession session, ref string lastState, string state)
    {
        if (lastState == state)
        {
            return;
        }

        lastState = state;
        WriteLog(session, $"Deslocamento: {state}.");
    }

    private void WriteRecognition(ClientSession session, string description, RecognitionResult result) =>
        WriteLog(session, $"{description} reconhecido ({result.Confidence:P0}).");

    private void HandleAudioStatus(ClientSession session, string message)
    {
        if (message.StartsWith("Alerta sonoro quase confirmado", StringComparison.Ordinal))
        {
            WritePersistentOnly(session, message);
            return;
        }

        if (message.StartsWith("Alerta sonoro de HP baixo CONFIRMADO", StringComparison.Ordinal))
        {
            WritePersistentOnly(session, message);
            WriteLog(session, "Alerta de HP confirmado.");
            return;
        }

        WriteLog(session, message);
    }

    private void WritePersistentOnly(ClientSession session, string message) =>
        WritePersistentOnly($"{session.Options.Label}: {message}");

    private void WritePersistentOnly(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
        try
        {
            lock (_logFileSync)
            {
                File.AppendAllText(_runtimeLogPath, line);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void WriteLog(ClientSession session, string message) => WriteLog($"{session.Options.Label}: {message}");
    private void WriteLog(string message)
    {
        WritePersistentOnly(message);
        Log?.Invoke(message);
    }
    private void SetStatus(BotRunState state, string title, string detail) => StatusChanged?.Invoke(state, title, detail);

    private static string CreateRuntimeLogPath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppIdentity.DataDirectoryName,
            "Logs");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"pexbot-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    private sealed class ClientSession(AutomationClientOptions options)
    {
        public AutomationClientOptions Options { get; } = options;
        public HpAudioAlertService Audio { get; } = new();
        public SemaphoreSlim WindowCaptureGate { get; } = new(1, 1);
        public SemaphoreSlim AudioRestartGate { get; } = new(1, 1);
        public GameWindowCaptureSession? WindowCapture { get; set; }
        public Task? WindowWatchdogTask { get; set; }
        public CancellationTokenSource? RecoveryActionCancellation;
        public int LastFarmSpot { get; set; } = -1;
        public int LastFarmSpotLevel { get; set; } = -1;
        public int PendingVisualDeath;
        public int DeathVisualHits { get; set; }
        public int ConsecutiveRecoveryFailures { get; set; }
        public bool RequiresHardFlowReset { get; set; }
        public bool AwaitingHuntActivationAtSpot { get; set; }
        public bool AwaitingFavoriteSpotRecognition { get; set; }
        public bool SafeInRest { get; set; }
        public bool IsFarmingTa { get; set; }
        public int AgendaEntries { get; set; }
        public bool AbbeyEntryMayHaveBeenCharged { get; set; }
        public bool AbbeyInside { get; set; }
        public bool AbbeyTimeExhausted { get; set; }
        public TimeSpan AbbeyUsed { get; set; }
        public DateTime AbbeyActiveSinceUtc { get; set; }
        public bool AbbeySupplyPurchasePending { get; set; }
        public string? AbbeyWeek { get; set; }
        public DateTime LastAbbeyTimeReadUtc { get; set; }
        public int AbbeyTimeLowHits { get; set; }
        public int PendingAbbeyTimeExhausted;
        public bool AnonymousDungeonInside { get; set; }
        public bool AnonymousDungeonEntryMayHaveBeenCharged { get; set; }
        public DateTime LastAnonymousTimeReadUtc { get; set; }
        public int AnonymousTimeLowHits { get; set; }
        public int PendingAnonymousTimeExhausted;
        public bool AnonymousDungeonExhausted { get; set; }
        public IReadOnlyList<FarmScheduleStep> FarmScheduleSteps { get; set; } = [];
        public bool FarmScheduleCompleted { get; set; }
        public bool FarmScheduleWaitingForWeeklyReset { get; set; }
        public bool FarmScheduleResumePending { get; set; }
        public int FarmScheduleIndex { get; set; }
        public TimeSpan FarmScheduleRemaining { get; set; }
        public DateTime FarmScheduleLastTickUtc { get; set; }
        public DateTime FarmScheduleLastSaveUtc { get; set; }
        public bool InDailyCampaign { get; set; }
        public bool DailyNeedsTeleport { get; set; }
        public string? DailyCycle { get; set; }
        public string? DailyStartedCycle { get; set; }
        public string? DailyCompletedCycle { get; set; }
        public string? DailyListToggleCycle { get; set; }
        public string? DirectiveCycle { get; set; }
        public string? DirectiveAttemptCycle { get; set; }
        public int DirectiveAttemptCount { get; set; }
        public DateTime NextDirectiveAttemptAt { get; set; }
        public string? DailyShopCycle { get; set; }
        public string? DailyShopCommonCycle { get; set; }
        public string? DailyShopSummonCycle { get; set; }
        public string? DailyShopAttemptCycle { get; set; }
        public int DailyShopAttemptCount { get; set; }
        public DateTime NextDailyShopAttemptAt { get; set; }
        public string? Mail01Date { get; set; }
        public string? Mail07Date { get; set; }
        public DateTime NextMailAttemptAt { get; set; }
        public DateTime NextDailyMissionCheckAt { get; set; }
        public DateTime NextDailyRoutineAttemptAt { get; set; }
        public int DailyRoutineFailureCount { get; set; }
        public DateTime NextVisibleDailyScanAt { get; set; }
        public DateTime NextRoutinePanelRecoveryAt { get; set; }
        public int DailyNoMissionHits { get; set; }
        public int DailyNormalHuntHits { get; set; }
        public bool InAgenda { get; set; }
        public bool HandlingDeath { get; set; }
        public bool NeedsDeathRestoration { get; set; }
        public string? RestorationAbsentCycle { get; set; }
        public bool AudioFailureLogged { get; set; }
        public bool PendingAgendaAfterSapheras { get; set; }
        public DateTime AgendaUntil { get; set; }
        public DateTime LastDeathVisualCheckAt { get; set; }
        public DateTime LastWindowFrameAt { get; set; }
        public int LowHpVisualHits { get; set; }
        public DateTime LowHpFirstObservedUtc { get; set; }
        public int PendingVisualLowHp;
        public bool VisualEmergencyIssued { get; set; }
        public string BackgroundEmergencySource { get; set; } = "imagem";
        public DateTime NextRecoveryAttemptAt { get; set; }
        public DateTime NextAudioRestartAt { get; set; }
        public bool VisualCaptureFaulted { get; set; }
        public bool AudioStartFaulted { get; set; }
        public DateTime LastAudioTelemetryLogAt { get; set; }
        public Queue<DateTime> Deaths { get; } = new();
    }
}
