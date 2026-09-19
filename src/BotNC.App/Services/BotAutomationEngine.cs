using System.Globalization;
using System.IO;
using BotNC.App.Models;

namespace BotNC.App.Services;

public sealed class BotAutomationEngine(
    GameWindowService gameWindows,
    WindowsInputService input,
    VisualRecognitionService recognition,
    ScreenCaptureService capture)
{
    private const int KeyEscape = 0x1B;
    private const int KeyW = 0x57;
    private const int KeyQ = 0x51;
    private const int KeyL = 0x4C;
    private const int KeyM = 0x4D;
    private const int KeyY = 0x59;
    private const int KeyEquals = 0xBB;
    private const double VisualHpEmergencyThreshold = 0.55;
    private const double FullDeathConfidence = 0.66;
    private const double FixedDeathElementConfidence = 0.82;
    private const double RestDeathConfidence = 0.68;
    private const double TaContextConfidence = 0.48;
    private readonly Random _random = new();
    private readonly object _logFileSync = new();
    private readonly string _runtimeLogPath = CreateRuntimeLogPath();
    private readonly SpotLevelRecognitionService _spotLevelRecognition = new();

    private static readonly IReadOnlyDictionary<TaDestination, (int X, int Y)> TaEntryPoints =
        new Dictionary<TaDestination, (int X, int Y)>
        {
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
                session.WindowWatchdogTask = MonitorClientWindowAsync(session, watchdogCancellation.Token);
            }

            WriteLog($"Log persistente desta execução: {_runtimeLogPath}");

            await RunCoreAsync(sessions, runOptions.Sapheras, runOptions.AntiOverkill, pause, cancellationToken);
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

    private async Task RunCoreAsync(
        IReadOnlyList<ClientSession> sessions,
        SapherasOptions sapheras,
        AntiOverkillOptions antiOverkill,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var sapherasSessions = sessions.Where(session => session.Options.UseSapheras).ToArray();
        if (sapherasSessions.Length == 0)
        {
            WriteLog("Sapheras desativada para todos os clientes. Mantendo o farm contínuo nas T.A configuradas.");
            foreach (var session in sessions)
            {
                await RunSessionActionSafelyAsync(
                    session,
                    "preparação inicial do farm",
                    () => PrepareClientForFarmAsync(
                        sessions, session, sapheras, antiOverkill, pause, cancellationToken, "contínuo"),
                    cancellationToken);
            }

            await MonitorFarmsAsync(sessions, sapheras, antiOverkill, pause, cancellationToken, stopAt: null);
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
                    await RunSessionActionSafelyAsync(
                        session,
                        "preparação do farm antes de Sapheras",
                        () => PrepareClientForFarmAsync(
                            sessions, session, sapheras, antiOverkill, pause, sapherasPriority.Token, "antes de Sapheras"),
                        sapherasPriority.Token);
                }

                await MonitorFarmsAsync(sessions, sapheras, antiOverkill, pause, sapherasPriority.Token, sapheras.ScheduledAt);
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
                    session.SafeInRest = false;
                    SetStatus(BotRunState.Running, $"{session.Options.Label}: entrando em Sapheras", "Preparando o cliente");
                    await ActivateGameAsync(session.Options.Target, cancellationToken);
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
                    WriteLog(session, $"Cliente sem passe não estava no farm; retomando a {TaName(session.Options.Destination)}.");
                    await RunSessionActionSafelyAsync(
                        session,
                        "retomada do cliente sem passe após Sapheras",
                        () => EnterTaAndStartFarmAsync(session, pause, cancellationToken, isEmergency: false),
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
                    () => EnterTaAndStartFarmAsync(session, pause, cancellationToken, isEmergency: false),
                    cancellationToken);
            }
        }

        var nextSapheras = sapheras with { ScheduledAt = sapheras.ScheduledAt.AddDays(1) };
        WriteLog($"Ciclo diário mantido: próxima Sapheras programada para {nextSapheras.ScheduledAt:dd/MM HH:mm:ss}.");
        await RunCoreAsync(sessions, nextSapheras, antiOverkill, pause, cancellationToken);
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

        await ActivateGameAsync(session.Options.Target, cancellationToken);
        var death = await FindDeathOnClientAsync(session, cancellationToken);
        if (death.Found)
        {
            WriteLog(session, $"O bot iniciou com a tela de morte aberta ({death.Confidence:P0}); restaurando antes de {context}.");
            await HandleDeathAsync(session, sapheras, antiOverkill, pause, cancellationToken);
            return;
        }

        var currentHunt = await FindReferenceOnClientAsync(
            session,
            "caca_automatica",
            cancellationToken);
        if (currentHunt.Found)
        {
            session.SafeInRest = true;
            session.IsFarmingTa = true;
            session.Audio.Armed = true;
            WriteLog(
                session,
                $"Bot iniciado com descanso e caça automática ativos ({currentHunt.Confidence:P0}); " +
                $"mantendo o farm atual e seguindo diretamente para o monitoramento {context}.");
            return;
        }

        await EnterTaAndStartFarmAsync(session, pause, cancellationToken, isEmergency: false);
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
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (gameWindows.IsForeground(session.Options.Target))
            {
                return await FindFullDeathInFrameAsync(capture.CapturePrimaryScreen(), cancellationToken);
            }

            return new RecognitionResult(false, 0, 0, 0);
        }
    }

    private async Task<RecognitionResult> FindReferenceOnClientAsync(
        ClientSession session,
        string referenceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var frame = await CaptureClientFrameAsync(session, cancellationToken);
            return await recognition.FindAsync(referenceId, frame, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (gameWindows.IsForeground(session.Options.Target))
            {
                return await recognition.FindAsync(referenceId, cancellationToken);
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
            if ((await FindReferenceOnClientAsync(session, referenceId, cancellationToken)).Found)
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
                                if (session.Options.UseSapheras)
                                {
                                    await ActivateGameAsync(session.Options.Target, actionToken);
                                    session.IsFarmingTa = false;
                                    session.SafeInRest = false;
                                    await EnterSapherasAsync(session, sapheras, pause, actionToken);
                                    session.SafeInRest = true;
                                }
                                else
                                {
                                    await EnterTaAndStartFarmAsync(session, pause, actionToken, isEmergency: true);
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
                        WriteLog(session, "Captura de áudio indisponível; mantendo a proteção visual independente e tentando reconectar.");
                    }

                    if (Interlocked.Exchange(ref session.PendingVisualLowHp, 0) != 0)
                    {
                        WriteLog(session, $"HP crítico visual durante Sapheras ({session.LastVisualHpPercent:P0}).");
                        session.Audio.Armed = false;
                        if (session.Options.UseSapheras)
                        {
                            await RunSessionActionSafelyAsync(
                                session,
                                "TP visual em Sapheras",
                                () => EmergencyReturnToSapherasAsync(session, sapheras, antiOverkill, finishesAt, pause, cancellationToken),
                                cancellationToken);
                        }
                        else
                        {
                            await RunSessionActionSafelyAsync(
                                session,
                                "TP visual na T.A durante Sapheras",
                                () => EmergencyReturnAsync(session, sapheras, antiOverkill, pause, cancellationToken),
                                cancellationToken);
                        }

                        session.Audio.Armed = session.NextRecoveryAttemptAt == default && session.IsFarmingTa;
                        break;
                    }

                    if (!session.Audio.TryConsumeAlert(out var confidence))
                    {
                        continue;
                    }

                    WriteLog(session, $"HP baixo durante Sapheras ({confidence:P0}). Acionando proteção.");
                    session.Audio.Armed = false;
                    if (session.Options.UseSapheras)
                    {
                        await RunSessionActionSafelyAsync(
                            session,
                            "TP de áudio em Sapheras",
                            () => EmergencyReturnToSapherasAsync(session, sapheras, antiOverkill, finishesAt, pause, cancellationToken),
                            cancellationToken);
                    }
                    else
                    {
                        await RunSessionActionSafelyAsync(
                            session,
                            "TP de áudio na T.A durante Sapheras",
                            () => EmergencyReturnAsync(session, sapheras, antiOverkill, pause, cancellationToken),
                            cancellationToken);
                    }

                    session.Audio.Armed = session.NextRecoveryAttemptAt == default && session.IsFarmingTa;
                    break;
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
        Interlocked.Exchange(ref session.PendingVisualLowHp, 0);
        session.SafeInRest = false;
        await ActivateGameForEmergencyAsync(session.Options.Target, cancellationToken);
        if ((await FindDeathOnClientAsync(session, cancellationToken)).Found)
        {
            await RecoverDeathDuringSapherasAsync(session, sapheras, antiOverkill, finishesAt, pause, cancellationToken);
            return;
        }

        var attempts = _random.Next(3, 6);
        WriteLog(session, $"Proteção em Sapheras: TP {sapheras.EmergencyTeleportKeyName} {attempts} vez(es).");
        for (var index = 0; index < attempts; index++)
        {
            await input.PressEmergencyKeyAsync(sapheras.EmergencyTeleportVirtualKey, cancellationToken);
            if (Volatile.Read(ref session.PendingVisualDeath) != 0)
            {
                break;
            }
        }

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
            WriteLog(session, "Retorno seguro confirmado; reentrando em Sapheras por prioridade máxima.");
            await EnterSapherasAsync(session, sapheras, pause, cancellationToken);
            session.SafeInRest = true;
        }
        else
        {
            session.SafeInRest = true;
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
        _ = session.Audio.TryConsumeAlert(out _);
        Interlocked.Exchange(ref session.PendingVisualDeath, 0);
        Interlocked.Exchange(ref session.PendingVisualLowHp, 0);
        try
        {
            SetStatus(BotRunState.Running, $"{session.Options.Label}: morte em Sapheras", "Restaurando antes de retomar a prioridade");
            await ActivateGameAsync(session.Options.Target, cancellationToken);
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
        await ExitRestIfNeededAsync(pause, cancellationToken);
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

        await StartAutomaticHuntAsync(session, pause, cancellationToken);
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
            await input.PressKeyAsync(KeyEquals, cancellationToken: cancellationToken);
            if (await WaitForReferenceToAppearAsync("menu_masmorra", TimeSpan.FromSeconds(8), pause, cancellationToken))
            {
                return;
            }
        }

        var diagnostic = await recognition.SaveDiagnosticAsync("prioridade_sapheras_menu");
        throw new TimeoutException($"{session.Options.Label}: não foi possível abrir Masmorras para a entrada prioritária em Sapheras. Diagnóstico: {diagnostic}");
    }

    private async Task EnterTaAndStartFarmAsync(
        ClientSession session,
        PauseController pause,
        CancellationToken cancellationToken,
        bool isEmergency)
    {
        var taName = TaName(session.Options.Destination);
        session.Audio.Armed = false;
        session.SafeInRest = false;
        session.IsFarmingTa = false;
        SetStatus(BotRunState.Running, $"{session.Options.Label}: entrando na {taName}", "Abrindo Terra Avassaladora");
        await ActivateGameAsync(session.Options.Target, cancellationToken);
        await AbortWorkflowIfDeathDetectedAsync(session, $"antes de abrir a {taName}", cancellationToken);
        await ExitRestIfNeededAsync(pause, cancellationToken);
        await AbortWorkflowIfDeathDetectedAsync(session, $"antes de abrir o menu da {taName}", cancellationToken);
        var readyReference = EntryReadyReference(session.Options.Destination);
        if (await IsTaSelectorContextVisibleAsync(readyReference, cancellationToken))
        {
            WriteLog(session, "O seletor da T.A já está aberto; retomando exatamente desta etapa.");
        }
        else
        {
            await OpenTaMenuAsync(session, pause, cancellationToken);
            WriteLog(session, "Abrindo Terra Avassaladora em (1742, 431).");
            await input.ClickAsync(1742, 431, cancellationToken);
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
            await input.PressKeyAsync(KeyEquals, cancellationToken: cancellationToken);
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
        var destination = session.Options.Destination;
        var taName = TaName(destination);
        SetStatus(BotRunState.Running, $"{session.Options.Label}: entrando na {taName}", isEmergency ? "Recuperação após alerta de HP" : "Selecionando a T.A");
        var readyReference = EntryReadyReference(destination);
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
        var entryVisuallyReady = await WaitForTaEntryReadyAsync(
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
            await EnsureGameForegroundAsync(session.Options.Target, cancellationToken);
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
                TimeSpan.FromMilliseconds(attempt == 1 ? 900 : 650),
                cancellationToken);

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
                    await IsTaSelectorContextVisibleAsync(readyReference, cancellationToken))
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

        WriteLog(session, $"{taName} reconhecida; aguardando o respawn estabilizar.");
        await ActionDelayAsync(cancellationToken, 2200, 3400);
        await BuySuppliesInsideTaAsync(session, pause, cancellationToken);
        await TravelToFarmSpotAsync(session, pause, cancellationToken);
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
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            await AbortWorkflowIfDeathDetectedAsync(
                session,
                "enquanto aguardava o seletor da T.A",
                cancellationToken);
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

            await Task.Delay(450, cancellationToken);
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
            await Task.Delay(400, cancellationToken);
        }

        return false;
    }

    private async Task<bool> IsTaSelectorContextVisibleAsync(
        string readyReference,
        CancellationToken cancellationToken)
    {
        var selector = await recognition.FindAsync("seletor_ta", cancellationToken);
        var button = await recognition.FindAsync(readyReference, cancellationToken);
        return selector.Found || button.Found ||
               (selector.Confidence >= TaContextConfidence &&
                button.Confidence >= TaContextConfidence);
    }

    private async Task BuySuppliesInsideTaAsync(ClientSession session, PauseController pause, CancellationToken cancellationToken)
    {
        var taName = TaName(session.Options.Destination);
        SetStatus(BotRunState.Running, $"{session.Options.Label}: comprando suprimentos", $"NPC Artigos dentro da {taName}");
        await WaitForReferenceAsync(ArrivalReference(session.Options.Destination), "painel Artigos", TimeSpan.FromSeconds(12), pause, cancellationToken);
        await input.ClickAsync(187, 129, cancellationToken);
        await WaitForReferenceAsync("loja_artigos", "Mercador de Artigos", TimeSpan.FromSeconds(15), pause, cancellationToken);
        WriteLog(session, "Loja reconhecida; aguardando uma estabilização curta antes de verificar a compra em lote.");
        await ActionDelayAsync(cancellationToken, 700, 1100);

        var buyButtonLuma = VisualRecognitionService.MeasureAverageLuma(
            capture.CapturePrimaryScreen(),
            300,
            980,
            165,
            45);
        if (buyButtonLuma < 72)
        {
            WriteLog(
                session,
                $"Comprar (Lote) está apagado (brilho {buyButtonLuma:F0}). Nada a repor; fechando a loja com Esc.");
            await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
            await WaitForReferenceAsync(
                ArrivalReference(session.Options.Destination),
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
        await WaitForReferenceAsync(ArrivalReference(session.Options.Destination), $"retorno à {taName}", TimeSpan.FromSeconds(15), pause, cancellationToken);
    }

    private async Task TravelToFarmSpotAsync(ClientSession session, PauseController pause, CancellationToken cancellationToken)
    {
        var taName = TaName(session.Options.Destination);
        SetStatus(BotRunState.Running, $"{session.Options.Label}: indo ao spot", $"Lendo Favoritos da {taName}");
        await input.PressKeyAsync(KeyM, cancellationToken: cancellationToken);
        await WaitForReferenceAsync("mapa_aberto", "mapa aberto", TimeSpan.FromSeconds(15), pause, cancellationToken);
        await OpenFavoritesAsync(session, pause, cancellationToken);

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
        if (session.Options.CustomFarmCoordinate is null)
        {
            spotLevel = await RecognizeFavoriteSpotLevelAsync(session, pause, cancellationToken);
            WriteLog(session, $"Primeiro favorito reconhecido como spot Nv. {spotLevel}.");
        }
        else
        {
            WriteLog(
                session,
                $"Coordenada personalizada ativa: ({session.Options.CustomFarmCoordinate.X}, " +
                $"{session.Options.CustomFarmCoordinate.Y}). A leitura do nível e o sorteio serão ignorados.");
        }

        WriteLog(session, "Selecionando o primeiro favorito (área de farm) em (1702, 285).");
        await input.ClickAsync(1702, 285, cancellationToken);
        await ActionDelayAsync(cancellationToken, 1800, 2200);
        await input.ClickAsync(444, 531, cancellationToken);
        await input.ClickAsync(1484, 532, cancellationToken);

        (int X, int Y) spot;
        if (session.Options.CustomFarmCoordinate is { } customCoordinate)
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
            var spots = FarmSpots[session.Options.Destination][confirmedLevel];
            var spotIndex = ChooseNextSpot(session, confirmedLevel, spots.Length);
            spot = spots[spotIndex];
            WriteLog(
                session,
                $"Spot Nv. {confirmedLevel}, posição aleatória {spotIndex + 1}/{spots.Length}: " +
                $"({spot.X}, {spot.Y}), sem repetir a anterior.");
        }

        var goReferences = session.Options.Destination == TaDestination.Ta2
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
        await StartAutomaticHuntAsync(session, pause, cancellationToken);
        session.IsFarmingTa = true;
        session.SafeInRest = true;
        session.Audio.Armed = true;
        WriteLog(session, $"Farm da {taName} iniciado e confirmado.");
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
        var levels = FarmSpots[session.Options.Destination].Keys.ToHashSet();
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
            $"{TaName(session.Options.Destination)}. Texto lido: '{lastText}'. Diagnóstico: {diagnostic}");
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
        await EnsureGameForegroundAsync(session.Options.Target, cancellationToken);
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
            await EnsureGameForegroundAsync(session.Options.Target, cancellationToken);
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
                await EnsureGameForegroundAsync(session.Options.Target, cancellationToken);
                await input.PressKeyAsync(KeyM, cancellationToken: cancellationToken);
            }

            WriteLog(session, $"Abrindo a tela de descanso com L — tentativa {attempt}/3.");
            await EnsureGameForegroundAsync(session.Options.Target, cancellationToken);
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
        var references = session.Options.Destination == TaDestination.Ta2
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

    private async Task WaitForFarmArrivalAsync(ClientSession session, PauseController pause, CancellationToken cancellationToken)
    {
        var taName = TaName(session.Options.Destination);
        SetStatus(BotRunState.Running, $"{session.Options.Label}: indo ao spot", "Aguardando o personagem chegar");
        var startedAt = DateTime.UtcNow;
        var confirmations = 0;
        var lastState = string.Empty;
        while (DateTime.UtcNow - startedAt < TimeSpan.FromMinutes(8))
        {
            await CheckpointAsync(pause, cancellationToken);
            if ((await recognition.FindAsync("descanso_ponto_fixo", cancellationToken)).Found)
            {
                confirmations = 0;
                LogMovementState(session, ref lastState, "Aguardando no ponto fixo");
                await Task.Delay(700, cancellationToken);
                continue;
            }

            if ((await recognition.FindAsync("descanso_movendo", cancellationToken)).Found)
            {
                confirmations = 0;
                LogMovementState(session, ref lastState, "Movendo-se");
                await Task.Delay(700, cancellationToken);
                continue;
            }

            var waiting = await recognition.FindAsync("descanso_aguardando_spot", cancellationToken);
            if (waiting.Found)
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
                return;
            }

            var restState = await FindRestStateAsync(cancellationToken);
            if (restState is not null)
            {
                WriteLog(session, $"Saindo do descanso para ativar a caça — tentativa {attempt}/3.");
                await input.PressKeyAsync(KeyL, cancellationToken: cancellationToken);
                await WaitForRestStateToDisappearAsync(TimeSpan.FromSeconds(20), pause, cancellationToken);
            }

            WriteLog(session, "Ativando caça automática com Q e reabrindo o descanso com L.");
            await input.PressKeyAsync(KeyQ, cancellationToken: cancellationToken);
            await input.PressKeyAsync(KeyL, cancellationToken: cancellationToken);
            if (await WaitForReferenceToAppearAsync("caca_automatica", TimeSpan.FromSeconds(25), pause, cancellationToken))
            {
                WriteLog(session, "Caça automática ativada e confirmada.");
                session.SafeInRest = true;
                return;
            }

            var resultingState = await FindRestStateAsync(cancellationToken);
            if (resultingState is null)
            {
                WriteLog(session, "O descanso ainda não abriu; tentando novamente sem presumir o estado da tela.");
            }
            else
            {
                WriteLog(session, $"Descanso abriu como '{RestStateDescription(resultingState.Value.ReferenceId)}', mas a caça não foi confirmada; repetindo o ciclo seguro L → Q → L.");
            }

        }

        var diagnostic = await recognition.SaveDiagnosticAsync($"caca_automatica_{session.Options.Label.Replace(' ', '_')}");
        throw new TimeoutException($"{session.Options.Label}: não foi possível confirmar a caça automática após três ciclos seguros. Diagnóstico: {diagnostic}");
    }

    private async Task MonitorFarmsAsync(
        IReadOnlyList<ClientSession> sessions,
        SapherasOptions sapheras,
        AntiOverkillOptions antiOverkill,
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
        try
        {
            while (true)
            {
                await CheckpointAsync(pause, cancellationToken);
                if (stopAt.HasValue && DateTime.Now >= stopAt.Value)
                {
                    return;
                }

                foreach (var session in sessions.OrderBy(session => session.Options.Priority))
                {
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
                        WriteLog(session, "Proteção rearmada após falha recuperável; retomando a vigilância.");
                        if (!session.IsFarmingTa)
                        {
                            var resumed = await RunRecoveryActionSafelyAsync(
                                session,
                                "retomada do fluxo após falha",
                                async actionToken =>
                                {
                                    if (session.NeedsDeathRestoration)
                                    {
                                        WriteLog(session, "Restauração pendente: concluindo a lápide antes de voltar à T.A.");
                                        await ActivateGameForEmergencyAsync(session.Options.Target, actionToken);
                                        await RestoreDeathResourcesAsync(session, pause, actionToken);
                                        session.NeedsDeathRestoration = false;
                                    }

                                    await EnterTaAndStartFarmAsync(session, pause, actionToken, isEmergency: true);
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

                    session.Audio.Armed = true;
                    if (!session.Audio.IsHealthy && !session.AudioFailureLogged)
                    {
                        session.AudioFailureLogged = true;
                        WriteLog(session, "Captura de áudio indisponível; a proteção visual independente permanece ativa.");
                    }

                    if (Interlocked.Exchange(ref session.PendingVisualLowHp, 0) != 0)
                    {
                        WriteLog(session, $"Atendendo HP crítico visual ({session.LastVisualHpPercent:P0}).");
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
                            RecoverSessionAfterActionFailure(session, exception, "TP visual de emergência");
                        }

                        if (!session.InAgenda && session.NextRecoveryAttemptAt == default && session.IsFarmingTa)
                        {
                            session.Audio.Armed = true;
                        }

                        break;
                    }

                    if (!session.Audio.TryConsumeAlert(out var confidence))
                    {
                        continue;
                    }

                    WriteLog(session, $"HP baixo confirmado pelo Som 2 ({confidence:P0}). Atendendo este cliente agora.");
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

                    if (!session.InAgenda && session.NextRecoveryAttemptAt == default && session.IsFarmingTa)
                    {
                        session.Audio.Armed = true;
                    }

                    break;
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

    private async Task MonitorClientWindowAsync(
        ClientSession session,
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
                    session.VisualLowHpHits = 0;
                    session.DeathVisualHits = 0;
                    await Task.Delay(120, cancellationToken);
                    continue;
                }

                var death = await FindDeathInFrameAsync(frame, cancellationToken);
                if (death.Found)
                {
                    session.VisualLowHpHits = 0;
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

                if (!session.Audio.Armed)
                {
                    session.VisualLowHpHits = 0;
                    await Task.Delay(120, cancellationToken);
                    continue;
                }

                var hp = HpBarAnalyzer.Measure(frame);
                if (!hp.Found || hp.Percent > VisualHpEmergencyThreshold)
                {
                    session.VisualLowHpHits = 0;
                    if (hp.Found)
                    {
                        session.LastVisualHpPercent = hp.Percent;
                    }

                    await Task.Delay(120, cancellationToken);
                    continue;
                }

                session.LastVisualHpPercent = hp.Percent;
                session.VisualLowHpHits++;
                if (session.VisualLowHpHits >= 2 &&
                    Interlocked.Exchange(ref session.PendingVisualLowHp, 1) == 0)
                {
                    WriteLog(
                        session,
                        $"HP crítico confirmado visualmente em segundo plano ({hp.Percent:P0}); proteção solicitada.");
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
        WriteLog(
            session,
            $"FALHA RECUPERÁVEL durante {action}: {exception.GetBaseException().Message}. " +
            "O outro cliente continua sendo monitorado; nova tentativa será feita no próximo ciclo.");
        WritePersistentOnly(session, exception.ToString());
        session.NextRecoveryAttemptAt = DateTime.UtcNow + TimeSpan.FromSeconds(6);
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
        Interlocked.Exchange(ref session.PendingVisualLowHp, 0);
        session.SafeInRest = false;
        session.IsFarmingTa = false;
        SetStatus(BotRunState.Running, $"{session.Options.Label}: proteção acionada", "Som 2 reconhecido · enviando TP");
        await ActivateGameForEmergencyAsync(session.Options.Target, cancellationToken);
        var deathBeforeTeleport = await FindDeathOnClientAsync(session, cancellationToken);
        if (Volatile.Read(ref session.PendingVisualDeath) != 0 || deathBeforeTeleport.Found)
        {
            WriteLog(session, $"O alerta chegou após a morte ({deathBeforeTeleport.Confidence:P0}); iniciando restauração.");
            await HandleDeathAsync(session, options, antiOverkill, pause, cancellationToken);
            return;
        }

        var attempts = _random.Next(3, 6);
        WriteLog(session, $"Usando TP {options.EmergencyTeleportKeyName} {attempts} vez(es).");
        for (var index = 0; index < attempts; index++)
        {
            await input.PressEmergencyKeyAsync(options.EmergencyTeleportVirtualKey, cancellationToken);
            if (Volatile.Read(ref session.PendingVisualDeath) != 0)
            {
                break;
            }
        }

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

        WriteLog(session, $"TP concluído sem tela de morte; reentrando na {TaName(session.Options.Destination)} e recompondo o farm.");
        // O TP pode terminar com o cliente ainda na tela de descanso. O fluxo
        // completo sai com L antes de tentar abrir qualquer menu.
        await EnterTaAndStartFarmAsync(session, pause, cancellationToken, isEmergency: true);
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
        Interlocked.Exchange(ref session.PendingVisualLowHp, 0);
        session.SafeInRest = false;
        session.IsFarmingTa = false;
        try
        {
            SetStatus(BotRunState.Running, $"{session.Options.Label}: personagem morreu", "Ressuscitando e restaurando recursos");
            // A tela de morte tem contagem regressiva curta; devolver o foco
            // rapidamente evita que o renascimento automático passe antes do
            // clique no botão inferior de Ressuscitar.
            await ActivateGameForEmergencyAsync(session.Options.Target, cancellationToken);
            await RestoreDeathResourcesAsync(session, pause, cancellationToken);

            if (RegisterDeath(session, antiOverkill))
            {
                session.Deaths.Clear();
                await StartAgendaAsync(session, antiOverkill, pause, cancellationToken);
                return;
            }

            WriteLog(session, $"Restauração concluída; retomando a {TaName(session.Options.Destination)}.");
            await EnterTaAndStartFarmAsync(session, pause, cancellationToken, isEmergency: true);
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

        if (fullDeath.Found)
        {
            WriteLog(
                session,
                $"Tela completa de morte confirmada ({fullDeath.Confidence:P0}); aguardando 5 segundos antes de Ressuscitar.");
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            await ActivateGameForEmergencyAsync(session.Options.Target, cancellationToken);
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
        await ActivateGameForEmergencyAsync(session.Options.Target, cancellationToken);
        if ((await FindReferenceOnClientAsync(session, "painel_restauracao", cancellationToken)).Found)
        {
            WriteLog(session, "O painel de restauração já está aberto; evitando clique desnecessário.");
        }
        else
        {
            WriteLog(session, "Procurando o ícone vermelho de perda de EXP/item no canto superior.");
            var iconDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            var iconFound = false;
            while (DateTime.UtcNow < iconDeadline)
            {
                await CheckpointAsync(pause, cancellationToken);
                var icon = await FindReferenceOnClientAsync(session, "icone_perda_exp", cancellationToken);
                if (icon.Found)
                {
                    iconFound = true;
                    WriteLog(session, $"Ícone de perda reconhecido ({icon.Confidence:P0}); abrindo a lápide em (1537, 72).");
                    break;
                }

                await Task.Delay(400, cancellationToken);
            }

            if (!iconFound)
            {
                WriteLog(
                    session,
                    "Nenhum ícone de perda apareceu após o renascimento; esta morte não gerou recursos restauráveis.");
                session.NeedsDeathRestoration = false;
                return;
            }

            var panelOpened = false;
            for (var attempt = 1; attempt <= 3 && !panelOpened; attempt++)
            {
                await EnsureGameForegroundAsync(session.Options.Target, cancellationToken);
                WriteLog(session, $"Clicando na lápide em (1537, 72) — tentativa {attempt}/3.");
                await input.MoveAndClickAsync(1537, 72, TimeSpan.FromMilliseconds(450), cancellationToken);
                panelOpened = await WaitForReferenceOnClientAsync(
                    session,
                    "painel_restauracao",
                    TimeSpan.FromSeconds(5),
                    pause,
                    cancellationToken);
            }

            if (!panelOpened)
            {
                var diagnosticFrame = await CaptureClientFrameAsync(session, cancellationToken);
                var diagnostic = await recognition.SaveDiagnosticAsync(
                    $"painel_restauracao_{session.Options.Priority}",
                    diagnosticFrame);
                throw new TimeoutException(
                    $"{session.Options.Label}: o painel de restauração não abriu após três tentativas. Diagnóstico: {diagnostic}");
            }
        }

        WriteLog(session, "Painel confirmado; restaurando separadamente as duas abas.");
        await ActionDelayAsync(cancellationToken, 900, 1400);
        await EnsureGameForegroundAsync(session.Options.Target, cancellationToken);
        var xpRestored = await RestoreVisibleResourceTabAsync(
            session, "EXP", 285, 886, pause, cancellationToken);

        WriteLog(session, "Selecionando a segunda aba de restauração em (47, 275).");
        var secondTabOpened = false;
        for (var attempt = 1; attempt <= 3 && !secondTabOpened; attempt++)
        {
            await EnsureGameForegroundAsync(session.Options.Target, cancellationToken);
            var beforeSecondTab = await CaptureClientFrameAsync(session, cancellationToken);
            await input.ClickAsync(47, 275, cancellationToken);
            secondTabOpened = await WaitForRegionChangeAsync(
                session,
                beforeSecondTab,
                0, 175, 460, 760,
                TimeSpan.FromSeconds(4),
                pause,
                cancellationToken);
        }

        var equipmentRestored = false;
        if (secondTabOpened)
        {
            await ActionDelayAsync(cancellationToken, 900, 1400);
            equipmentRestored = await RestoreVisibleResourceTabAsync(
                session, "item/equipamento", 259, 886, pause, cancellationToken);
        }
        else
        {
            WriteLog(session, "A segunda aba não abriu; ela pode estar indisponível nesta morte.");
        }

        if (!xpRestored && !equipmentRestored)
        {
            throw new InvalidOperationException(
                $"{session.Options.Label}: nenhum recurso respondeu à restauração; " +
                "não é seguro retornar ao farm enquanto a perda indicada continuar pendente.");
        }

        if (!(await FindReferenceOnClientAsync(session, "painel_restauracao", cancellationToken)).Found)
        {
            var diagnosticFrame = await CaptureClientFrameAsync(session, cancellationToken);
            var diagnostic = await recognition.SaveDiagnosticAsync(
                $"restauracao_fechou_antes_{session.Options.Priority}",
                diagnosticFrame);
            throw new InvalidOperationException(
                $"{session.Options.Label}: o painel de restauração fechou antes da conclusão das duas abas. Diagnóstico: {diagnostic}");
        }

        WriteLog(session, "As duas abas foram processadas; fechando o painel com Esc.");
        await input.PressKeyAsync(KeyEscape, cancellationToken: cancellationToken);
        var closeDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < closeDeadline &&
               (await FindReferenceOnClientAsync(session, "painel_restauracao", cancellationToken)).Found)
        {
            await CheckpointAsync(pause, cancellationToken);
            await Task.Delay(350, cancellationToken);
        }

        if ((await FindReferenceOnClientAsync(session, "painel_restauracao", cancellationToken)).Found)
        {
            throw new TimeoutException($"{session.Options.Label}: o painel de restauração não fechou após Esc.");
        }

        WriteLog(session, "Painel de restauração fechado.");
        session.NeedsDeathRestoration = false;
    }

    private async Task<bool> RestoreVisibleResourceTabAsync(
        ClientSession session,
        string tabName,
        int clickX,
        int clickY,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await CheckpointAsync(pause, cancellationToken);
            await EnsureGameForegroundAsync(session.Options.Target, cancellationToken);
            var beforeClick = await CaptureClientFrameAsync(session, cancellationToken);
            WriteLog(session, $"Restaurando aba de {tabName} em ({clickX}, {clickY}) — tentativa {attempt}/3.");
            await input.ClickAsync(clickX, clickY, cancellationToken);
            var changed = await WaitForRegionChangeAsync(
                session,
                beforeClick,
                70, 105, 390, 830,
                TimeSpan.FromSeconds(4),
                pause,
                cancellationToken);
            if (changed)
            {
                WriteLog(session, $"Aba de {tabName} respondeu ao clique de restauração.");
                await ActionDelayAsync(cancellationToken, 900, 1400);
                return true;
            }

            if (attempt < 3)
            {
                WriteLog(session, $"Aba de {tabName} ainda não respondeu; repetindo somente este clique.");
            }
        }

        WriteLog(session, $"Aba de {tabName} não apresentou mudança visual; ela pode estar sem perda restaurável.");
        return false;
    }

    private async Task<bool> WaitForRegionChangeAsync(
        ClientSession session,
        PixelFrame baseline,
        int x,
        int y,
        int width,
        int height,
        TimeSpan timeout,
        PauseController pause,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, cancellationToken);
            var current = await CaptureClientFrameAsync(session, cancellationToken);
            if (MeasureRegionDifference(baseline, current, x, y, width, height) >= 5.5)
            {
                return true;
            }

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }

    private static double MeasureRegionDifference(
        PixelFrame first,
        PixelFrame second,
        int referenceX,
        int referenceY,
        int referenceWidth,
        int referenceHeight)
    {
        if (first.Width != second.Width || first.Height != second.Height)
        {
            return double.MaxValue;
        }

        var scaleX = first.Width / 1920d;
        var scaleY = first.Height / 1040d;
        var left = Math.Clamp((int)Math.Round(referenceX * scaleX), 0, first.Width - 1);
        var top = Math.Clamp((int)Math.Round(referenceY * scaleY), 0, first.Height - 1);
        var right = Math.Clamp((int)Math.Round((referenceX + referenceWidth) * scaleX), left + 1, first.Width);
        var bottom = Math.Clamp((int)Math.Round((referenceY + referenceHeight) * scaleY), top + 1, first.Height);
        double difference = 0;
        var samples = 0;
        for (var sampleY = top; sampleY < bottom; sampleY += 4)
        {
            for (var sampleX = left; sampleX < right; sampleX += 4)
            {
                var offset = (sampleY * first.Stride) + (sampleX * 4);
                var firstLuma = ((first.Pixels[offset + 2] * 77) +
                                 (first.Pixels[offset + 1] * 150) +
                                 (first.Pixels[offset] * 29)) >> 8;
                var secondLuma = ((second.Pixels[offset + 2] * 77) +
                                  (second.Pixels[offset + 1] * 150) +
                                  (second.Pixels[offset] * 29)) >> 8;
                difference += Math.Abs(firstLuma - secondLuma);
                samples++;
            }
        }

        return samples == 0 ? 0 : difference / samples;
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
        await ActivateGameAsync(session.Options.Target, cancellationToken);
        await ExitRestIfNeededAsync(pause, cancellationToken);
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
        await ActivateGameAsync(session.Options.Target, cancellationToken);
        await ExitRestIfNeededAsync(pause, cancellationToken);
        var attempts = _random.Next(3, 6);
        WriteLog(session, $"Encerrando Agenda com TP {sapheras.EmergencyTeleportKeyName} ({attempts} tentativas).");
        for (var index = 0; index < attempts; index++)
        {
            await input.PressEmergencyKeyAsync(sapheras.EmergencyTeleportVirtualKey, cancellationToken);
        }

        await ActionDelayAsync(cancellationToken, 4200, 5600);
        await input.PressKeyAsync(KeyY, cancellationToken: cancellationToken);
        await ActionDelayAsync(cancellationToken, 1800, 2200);
        session.InAgenda = false;
        session.SafeInRest = false;
        session.AgendaUntil = default;
        WriteLog(session, "Popup de encerramento da Agenda tratado com Y.");
        if (resumeFarm)
        {
            await EnterTaAndStartFarmAsync(session, pause, cancellationToken, isEmergency: true);
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

    private async Task ExitRestIfNeededAsync(PauseController pause, CancellationToken cancellationToken)
    {
        await CheckpointAsync(pause, cancellationToken);
        var restState = await FindRestStateAsync(cancellationToken);
        if (restState is not null)
        {
            WriteLog($"Tela de descanso detectada por '{RestStateDescription(restState.Value.ReferenceId)}' ({restState.Value.Result.Confidence:P0}); saindo com L antes de abrir menus.");
            await input.PressKeyAsync(KeyL, cancellationToken: cancellationToken);
            await WaitForRestStateToDisappearAsync(TimeSpan.FromSeconds(20), pause, cancellationToken);
        }
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

    private async Task WaitForRestStateToDisappearAsync(
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
                    return;
                }
            }
            else
            {
                clearConfirmations = 0;
            }

            await Task.Delay(350, cancellationToken);
        }

        throw new TimeoutException("A tela de descanso não fechou dentro do limite adaptativo.");
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
        if (!agenda.Found)
        {
            return false;
        }

        WriteLog(session, $"Aviso de agenda detectado ({agenda.Confidence:P0}); fechando com Y.");
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

            await Task.Delay(450, cancellationToken);
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
        var best = new RecognitionResult(false, 0, 0, 0);
        while (DateTime.UtcNow - startedAt < timeout)
        {
            await CheckpointAsync(pause, cancellationToken);
            if (referenceId != "aviso_agenda")
            {
                var agenda = await recognition.FindAsync("aviso_agenda", cancellationToken);
                if (agenda.Found)
                {
                    WriteLog($"Aviso de agenda reconhecido ({agenda.Confidence:P0}); fechando com Y e retomando a etapa.");
                    await input.PressKeyAsync(KeyY, cancellationToken: cancellationToken);
                    continue;
                }
            }

            var current = await recognition.FindAsync(referenceId, cancellationToken);
            if (current.Confidence > best.Confidence)
            {
                best = current;
            }

            if (current.Found)
            {
                return current;
            }

            await Task.Delay(450, cancellationToken);
        }

        var diagnostic = await recognition.SaveDiagnosticAsync(referenceId);
        throw new TimeoutException($"Tempo esgotado procurando {description}. Melhor confiança: {best.Confidence:P0}. Diagnóstico: {diagnostic}");
    }

    private async Task ActivateGameAsync(GameWindowTarget target, CancellationToken cancellationToken)
    {
        if (!gameWindows.Activate(target))
        {
            throw new InvalidOperationException($"Não foi possível ativar a janela {target.Title}.");
        }

        await Task.Delay(1800, cancellationToken);
    }

    private async Task ActivateGameForEmergencyAsync(GameWindowTarget target, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (gameWindows.IsForeground(target) || gameWindows.Activate(target))
            {
                await Task.Delay(180, cancellationToken);
                if (gameWindows.IsForeground(target))
                {
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

    private async Task EnsureGameForegroundAsync(GameWindowTarget target, CancellationToken cancellationToken)
    {
        if (gameWindows.IsForeground(target))
        {
            return;
        }

        if (!gameWindows.Activate(target))
        {
            throw new InvalidOperationException($"Não foi possível devolver o foco à janela {target.Title}.");
        }

        await Task.Delay(1800, cancellationToken);
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

    private static string ArrivalReference(TaDestination destination) => destination == TaDestination.Ta2 ? "ta2_chegada" : "ta3_chegada";
    private static string EntryReadyReference(TaDestination destination) => destination == TaDestination.Ta2 ? "entrar_ta2_pronto" : "entrar_ta3_pronto";
    private static string TaName(TaDestination destination) => destination == TaDestination.Ta2 ? "T.A 2" : "T.A 3";

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
        if (message.StartsWith("Som 2 quase confirmado", StringComparison.Ordinal))
        {
            WritePersistentOnly(session, message);
            return;
        }

        if (message.StartsWith("Alerta de HP Som 2 CONFIRMADO", StringComparison.Ordinal))
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
            "PEXBOT",
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
        public int PendingVisualLowHp;
        public int VisualLowHpHits { get; set; }
        public int DeathVisualHits { get; set; }
        public double LastVisualHpPercent { get; set; }
        public bool SafeInRest { get; set; }
        public bool IsFarmingTa { get; set; }
        public bool InAgenda { get; set; }
        public bool HandlingDeath { get; set; }
        public bool NeedsDeathRestoration { get; set; }
        public bool AudioFailureLogged { get; set; }
        public bool PendingAgendaAfterSapheras { get; set; }
        public DateTime AgendaUntil { get; set; }
        public DateTime LastDeathVisualCheckAt { get; set; }
        public DateTime LastWindowFrameAt { get; set; }
        public DateTime NextRecoveryAttemptAt { get; set; }
        public DateTime NextAudioRestartAt { get; set; }
        public bool VisualCaptureFaulted { get; set; }
        public bool AudioStartFaulted { get; set; }
        public DateTime LastAudioTelemetryLogAt { get; set; }
        public Queue<DateTime> Deaths { get; } = new();
    }
}
