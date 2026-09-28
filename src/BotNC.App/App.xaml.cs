using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BotNC.App.Models;
using BotNC.App.Services;

namespace BotNC.App;

public partial class App : Application
{
    private Mutex? _installationMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _installationMutex = new Mutex(false, AppIdentity.MutexName);
        var audioProbeIndex = Array.IndexOf(e.Args, "--audio-probe");
        if (audioProbeIndex >= 0 && audioProbeIndex + 2 < e.Args.Length)
        {
            var processId = int.Parse(e.Args[audioProbeIndex + 1]);
            var probeOutputPath = Path.GetFullPath(e.Args[audioProbeIndex + 2]);
            var monitor = new HpAudioAlertService();
            var messages = new List<string>();
            HpAudioMonitorSnapshot? lastSnapshot = null;
            monitor.Status += messages.Add;
            monitor.Telemetry += snapshot => lastSnapshot = snapshot;
            using var probeCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await monitor.StartAsync(processId, probeCancellation.Token);
            monitor.Armed = true;
            await Task.Delay(2500, probeCancellation.Token);
            messages.Add(
                $"healthy={monitor.IsHealthy}; armed={monitor.Armed}; buffers={monitor.CapturedBufferCount}; " +
                $"current={lastSnapshot?.CurrentConfidence:F4}; peak5s={lastSnapshot?.RecentPeakConfidence:F4}; " +
                $"lastAlert={lastSnapshot?.LastAlertAt:O}");
            await monitor.StopAsync();
            await File.WriteAllLinesAsync(probeOutputPath, messages);
            Shutdown();
            return;
        }

        var windowProbeIndex = Array.IndexOf(e.Args, "--window-probe");
        if (windowProbeIndex >= 0 && windowProbeIndex + 2 < e.Args.Length)
        {
            var processId = int.Parse(e.Args[windowProbeIndex + 1]);
            var probeOutputPath = Path.GetFullPath(e.Args[windowProbeIndex + 2]);
            var gameWindows = new GameWindowService();
            var target = gameWindows.Discover()
                .FirstOrDefault(candidate => candidate.ProcessId == processId);
            if (target is null)
            {
                throw new InvalidOperationException($"Janela do Night Crows não encontrada para o processo {processId}.");
            }

            var database = new AppDatabase();
            await database.InitializeAsync();
            var recognition = new VisualRecognitionService(database, new ScreenCaptureService());
            await using var windowCapture = new GameWindowCaptureSession(target);
            using var probeCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var frame = await windowCapture.CaptureAsync(probeCancellation.Token);
            var death = await recognition.FindAsync("morte_confirmada", frame, probeCancellation.Token);
            var deathClientTwo = await recognition.FindAsync("morte_confirmada_ta2", frame, probeCancellation.Token);
            var deathTitle = await recognition.FindAsync("morte_titulo", frame, probeCancellation.Token);
            var deathButton = await recognition.FindAsync("morte_ressuscitar", frame, probeCancellation.Token);
            var restDeath = await recognition.FindAsync("descanso_morte", frame, probeCancellation.Token);
            var hp = HpBarAnalyzer.Measure(frame);
            var windowSize = gameWindows.GetWindowSize(target);
            var mappedTa2 = gameWindows.MapReferencePoint(target, 842, 772);
            var mappedTa3 = gameWindows.MapReferencePoint(target, 1126, 775);
            await File.WriteAllLinesAsync(
                probeOutputPath,
                [
                    $"frame={frame.Width}x{frame.Height}",
                    $"window={windowSize.Width}x{windowSize.Height}",
                    $"mappedTa2={mappedTa2.X},{mappedTa2.Y}",
                    $"mappedTa3={mappedTa3.X},{mappedTa3.Y}",
                    $"death={death.Found};confidence={death.Confidence:F4}",
                    $"deathClientTwo={deathClientTwo.Found};confidence={deathClientTwo.Confidence:F4}",
                    $"deathTitle={deathTitle.Found};confidence={deathTitle.Confidence:F4}",
                    $"deathButton={deathButton.Found};confidence={deathButton.Confidence:F4}",
                    $"restDeath={restDeath.Found};confidence={restDeath.Confidence:F4}",
                    $"hpFound={hp.Found};hp={hp.Percent:P1}"
                ]);
            Shutdown();
            return;
        }

        var audioSelfTestIndex = Array.IndexOf(e.Args, "--audio-self-test");
        if (audioSelfTestIndex >= 0 && audioSelfTestIndex + 2 < e.Args.Length)
        {
            var result = HpAudioAlertService.TestReference(Path.GetFullPath(e.Args[audioSelfTestIndex + 1]));
            await File.WriteAllTextAsync(Path.GetFullPath(e.Args[audioSelfTestIndex + 2]), result);
            Shutdown();
            return;
        }

        var abbeyTimeProbeIndex = Array.IndexOf(e.Args, "--abbey-time-probe");
        if (abbeyTimeProbeIndex >= 0 && abbeyTimeProbeIndex + 2 < e.Args.Length)
        {
            await using var stream = File.OpenRead(Path.GetFullPath(e.Args[abbeyTimeProbeIndex + 1]));
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            var frame = new PixelFrame(converted.PixelWidth, converted.PixelHeight, stride, pixels);
            if (frame.Width is >= 1918 and <= 1922 && frame.Height is >= 1078 and <= 1082)
            {
                // Capturas enviadas incluem barra de título e barra de tarefas;
                // a captura do jogo em produção contém apenas a área cliente.
                const int titleHeight = 23;
                var clientHeight = frame.Height - titleHeight - 40;
                var clientPixels = new byte[stride * clientHeight];
                Array.Copy(pixels, stride * titleHeight, clientPixels, 0, clientPixels.Length);
                frame = new PixelFrame(frame.Width, clientHeight, stride, clientPixels);
            }
            var reader = new AbbeyTimeReader();
            var inGame = await reader.ReadAsync(frame, CancellationToken.None);
            var inMenu = await reader.ReadMenuAsync(frame, CancellationToken.None);
            var epicMenu = await reader.ReadEpicMenuAsync(frame, CancellationToken.None);
            await File.WriteAllLinesAsync(Path.GetFullPath(e.Args[abbeyTimeProbeIndex + 2]),
                [$"inGame={inGame.Remaining};text={inGame.Text}",
                 $"inMenu={inMenu.Remaining};text={inMenu.Text}",
                 $"epicMenu={epicMenu.Remaining};text={epicMenu.Text}",
                 $"tombstoneRed={TombstoneIconAnalyzer.HasRedIcon(frame)}",
                 $"hp={HpBarAnalyzer.Measure(frame)}"]);
            Shutdown();
            return;
        }

        var spotLevelProbeIndex = Array.IndexOf(e.Args, "--spot-level-probe");
        if (spotLevelProbeIndex >= 0 && spotLevelProbeIndex + 2 < e.Args.Length)
        {
            var service = new SpotLevelRecognitionService();
            var result = await service.RecognizeFirstFavoriteInImageAsync(
                Path.GetFullPath(e.Args[spotLevelProbeIndex + 1]),
                new HashSet<int> { 68, 72, 76, 80, 84, 88, 90, 92, 94, 96, 98, 100 });
            await File.WriteAllTextAsync(
                Path.GetFullPath(e.Args[spotLevelProbeIndex + 2]),
                $"level={result.Level};text={result.Text}");
            Shutdown();
            return;
        }

        var taEntryTextProbeIndex = Array.IndexOf(e.Args, "--ta-entry-text-probe");
        if (taEntryTextProbeIndex >= 0 && taEntryTextProbeIndex + 2 < e.Args.Length)
        {
            await using var stream = File.OpenRead(Path.GetFullPath(e.Args[taEntryTextProbeIndex + 1]));
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            var frame = new PixelFrame(converted.PixelWidth, converted.PixelHeight, stride, pixels);
            var found = await new TaEntryTextReader().HasFirstEntryAsync(frame, CancellationToken.None);
            await File.WriteAllTextAsync(Path.GetFullPath(e.Args[taEntryTextProbeIndex + 2]),
                $"firstEntry={found}");
            Shutdown();
            return;
        }

        var directiveCounterProbeIndex = Array.IndexOf(e.Args, "--directive-counter-probe");
        if (directiveCounterProbeIndex >= 0 && directiveCounterProbeIndex + 2 < e.Args.Length)
        {
            await using var stream = File.OpenRead(Path.GetFullPath(e.Args[directiveCounterProbeIndex + 1]));
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            var frame = new PixelFrame(converted.PixelWidth, converted.PixelHeight, stride, pixels);
            var completed = await new GuildDirectiveCounterReader().IsCompleteAsync(frame, CancellationToken.None);
            await File.WriteAllTextAsync(Path.GetFullPath(e.Args[directiveCounterProbeIndex + 2]),
                $"completed={completed}");
            Shutdown();
            return;
        }

        var shopStatusProbeIndex = Array.IndexOf(e.Args, "--shop-status-probe");
        var guildDonationProbeIndex = Array.IndexOf(e.Args, "--guild-donation-probe");
        if (guildDonationProbeIndex >= 0 && guildDonationProbeIndex + 2 < e.Args.Length)
        {
            await using var stream = File.OpenRead(Path.GetFullPath(e.Args[guildDonationProbeIndex + 1]));
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            var frame = new PixelFrame(converted.PixelWidth, converted.PixelHeight, stride, pixels);
            var remaining = await new GuildDonationCounterReader().ReadAsync(frame, CancellationToken.None);
            await File.WriteAllTextAsync(Path.GetFullPath(e.Args[guildDonationProbeIndex + 2]),
                $"remaining={remaining?.ToString() ?? "unknown"}");
            Shutdown();
            return;
        }
        var guildReferenceProbeIndex = Array.IndexOf(e.Args, "--guild-reference-probe");
        if (guildReferenceProbeIndex >= 0 && guildReferenceProbeIndex + 1 < e.Args.Length)
        {
            var database = new AppDatabase();
            await database.InitializeAsync();
            var recognition = new VisualRecognitionService(database, new ScreenCaptureService());
            var directory = Path.Combine(AppContext.BaseDirectory, "Assets", "References");
            var lines = new List<string>();
            foreach (var (reference, file) in new[]
            {
                ("guild_checkin_available", "guild_checkin_page.png"),
                ("guild_checkin_reward", "guild_checkin_reward.png"),
                ("guild_donation_panel", "guild_donation_panel.png")
            })
            {
                var result = await recognition.FindInImageAsync(reference, Path.Combine(directory, file));
                lines.Add($"{reference}={result.Found};confidence={result.Confidence:F3}");
            }
            var rewardOnPage = await recognition.FindInImageAsync(
                "guild_checkin_reward", Path.Combine(directory, "guild_checkin_page.png"));
            var checkinOnReward = await recognition.FindInImageAsync(
                "guild_checkin_available", Path.Combine(directory, "guild_checkin_reward.png"));
            lines.Add($"rewardOnPage={rewardOnPage.Found};confidence={rewardOnPage.Confidence:F3}");
            lines.Add($"checkinOnReward={checkinOnReward.Found};confidence={checkinOnReward.Confidence:F3}");
            await File.WriteAllLinesAsync(Path.GetFullPath(e.Args[guildReferenceProbeIndex + 1]), lines);
            Shutdown();
            return;
        }
        if (shopStatusProbeIndex >= 0 && shopStatusProbeIndex + 3 < e.Args.Length)
        {
            await using var stream = File.OpenRead(Path.GetFullPath(e.Args[shopStatusProbeIndex + 1]));
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            var frame = new PixelFrame(converted.PixelWidth, converted.PixelHeight, stride, pixels);
            var reader = new DailyShopStatusReader();
            var result = string.Equals(e.Args[shopStatusProbeIndex + 2], "summon", StringComparison.OrdinalIgnoreCase)
                ? await reader.ReadSummonAsync(frame, CancellationToken.None)
                : await reader.ReadCommonAsync(frame, CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.GetFullPath(e.Args[shopStatusProbeIndex + 3]),
                $"exhausted={result.Exhausted};evidence={result.Evidence}");
            Shutdown();
            return;
        }

        var directiveSidebarProbeIndex = Array.IndexOf(e.Args, "--directive-sidebar-probe");
        if (directiveSidebarProbeIndex >= 0 && directiveSidebarProbeIndex + 2 < e.Args.Length)
        {
            await using var stream = File.OpenRead(Path.GetFullPath(e.Args[directiveSidebarProbeIndex + 1]));
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            var frame = new PixelFrame(converted.PixelWidth, converted.PixelHeight, stride, pixels);
            var result = await new GuildDirectiveSidebarReader().ReadAsync(frame, CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.GetFullPath(e.Args[directiveSidebarProbeIndex + 2]),
                $"state={result.State};green={result.GreenPixels};counters={result.GenericCounters};evidence={result.Evidence}");
            Shutdown();
            return;
        }

        var imageProbeIndex = Array.IndexOf(e.Args, "--image-probe");
        var flowProbeIndex = Array.IndexOf(e.Args, "--flow-probe");
        if (flowProbeIndex >= 0 && flowProbeIndex + 2 < e.Args.Length)
        {
            var output = Path.GetFullPath(e.Args[flowProbeIndex + 2]);
            var db = new AppDatabase(output + ".data");
            await db.InitializeAsync();
            var service = new VisualRecognitionService(db, new ScreenCaptureService());
            var frame = LoadReferenceFrame(Path.GetFullPath(e.Args[flowProbeIndex + 1]));
            var lines = new List<string>();
            foreach (var id in new[] { "hud_auto_label", "hud_auto_active", "hud_auto_inactive", "lapide_vermelha", "boss_entry_icon", "sapheras_map", "game_hud_menu", "rest_unlock_instruction", "tela_descanso", "descanso_morte", "morte_ressuscitar" })
            {
                var r = await service.FindAsync(id, frame);
                lines.Add($"{id}: found={r.Found}; confidence={r.Confidence:F4}; x={r.X}; y={r.Y}");
            }
            lines.Add($"auto={await OpenHudHuntReader.ReadAsync(service, frame, CancellationToken.None)}");
            lines.Add($"tombstone={await TombstoneIconReader.ReadAsync(service, frame, CancellationToken.None)}");
            lines.Add($"purpleDaily={BotAutomationEngine.HasPurpleDailyMission(frame)}");
            var normalized = VisualRecognitionService.NormalizeForReferenceMatching(frame);
            var hp = HpBarAnalyzer.Measure(normalized);
            var controls = (await service.FindAsync("game_hud_menu", normalized)).Found ||
                (await service.FindAsync("hud_auto_label", normalized)).Found;
            lines.Add($"restorationHud={BotAutomationEngine.HasSufficientRestorationHud(controls, hp.Found, true)}; hp={hp}; controls={controls}");
            await File.WriteAllLinesAsync(output, lines);
            Shutdown();
            return;
        }
        if (imageProbeIndex >= 0 && imageProbeIndex + 2 < e.Args.Length)
        {
            var imagePath = Path.GetFullPath(e.Args[imageProbeIndex + 1]);
            var imageProbeOutputPath = Path.GetFullPath(e.Args[imageProbeIndex + 2]);
            var database = new AppDatabase();
            await database.InitializeAsync();
            var recognition = new VisualRecognitionService(database, new ScreenCaptureService());
            var primaryDeath = await recognition.FindInImageAsync("morte_confirmada", imagePath);
            var clientTwoDeath = await recognition.FindInImageAsync("morte_confirmada_ta2", imagePath);
            var deathTitle = await recognition.FindInImageAsync("morte_titulo", imagePath);
            var deathButton = await recognition.FindInImageAsync("morte_ressuscitar", imagePath);
            var restDeath = await recognition.FindInImageAsync("descanso_morte", imagePath);
            var ta2EntryReady = await recognition.FindInImageAsync("entrar_ta2_pronto", imagePath);
            var ta3EntryReady = await recognition.FindInImageAsync("entrar_ta3_pronto", imagePath);
            var taSelector = await recognition.FindInImageAsync("seletor_ta", imagePath);
            var ta1EntryReady = await recognition.FindInImageAsync("entrar_ta1_pronto", imagePath);
            var ta1FirstCard = await recognition.FindInImageAsync("ta1_primeiro_cartao", imagePath);
            var dailyTeleportResource = await recognition.FindInImageAsync("daily_teleport_resource", imagePath);
            var dailyTeleportOk = await recognition.FindInImageAsync("daily_teleport_ok", imagePath);
            var dailyPage = await recognition.FindInImageAsync("daily_page", imagePath);
            var guildDirectiveCompleted = await recognition.FindInCroppedImageAsync("guild_directive_completed", imagePath);
            var guildDirectiveCompletedAlt = await recognition.FindInImageAsync("guild_directive_completed_alt", imagePath);
            var dailyThirtyCounter = await recognition.HasDailyThirtyCounterInImageAsync(imagePath);
            var dimmedDailyRows = await recognition.CountDimmedDailyMissionRowsInImageAsync(imagePath);
            var mailPage = await recognition.FindInImageAsync("mail_page", imagePath);
            var menuMail = await recognition.FindInImageAsync("menu_mail", imagePath);
            var mailReceiveAll = await recognition.FindInImageAsync("mail_receive_all", imagePath);
            var mailItems = await recognition.FindInImageAsync("mail_item_obtained", imagePath);
            var mailEmpty = await recognition.FindInImageAsync("mail_empty", imagePath);
            var mailNotification = await recognition.HasUnclaimedServerMailInImageAsync(imagePath);
            var restorationIcon = await recognition.FindInImageAsync("icone_perda_exp", imagePath);
            var ta1Arrival = await recognition.FindInImageAsync("ta1_chegada", imagePath);
            var shop = await recognition.FindInImageAsync("loja_artigos", imagePath);
            var directiveAccept = await recognition.FindInImageAsync("guild_directive_accept_button", imagePath);
            var directiveDecline = await recognition.FindInImageAsync("guild_directive_decline_button", imagePath);
            await File.WriteAllLinesAsync(
                imageProbeOutputPath,
                [
                    $"primaryDeath={primaryDeath.Found};confidence={primaryDeath.Confidence:F4}",
                    $"clientTwoDeath={clientTwoDeath.Found};confidence={clientTwoDeath.Confidence:F4}",
                    $"deathTitle={deathTitle.Found};confidence={deathTitle.Confidence:F4}",
                    $"deathButton={deathButton.Found};confidence={deathButton.Confidence:F4}",
                    $"restDeath={restDeath.Found};confidence={restDeath.Confidence:F4}",
                    $"ta2EntryReady={ta2EntryReady.Found};confidence={ta2EntryReady.Confidence:F4}",
                    $"ta3EntryReady={ta3EntryReady.Found};confidence={ta3EntryReady.Confidence:F4}",
                    $"taSelector={taSelector.Found};confidence={taSelector.Confidence:F4}",
                    $"ta1EntryReady={ta1EntryReady.Found};confidence={ta1EntryReady.Confidence:F4}",
                    $"ta1FirstCard={ta1FirstCard.Found};confidence={ta1FirstCard.Confidence:F4}",
                    $"dailyTeleportResource={dailyTeleportResource.Found};confidence={dailyTeleportResource.Confidence:F4}",
                    $"dailyTeleportOk={dailyTeleportOk.Found};confidence={dailyTeleportOk.Confidence:F4}",
                    $"dailyPage={dailyPage.Found};confidence={dailyPage.Confidence:F4}",
                    $"guildDirectiveCompleted={guildDirectiveCompleted.Found};confidence={guildDirectiveCompleted.Confidence:F4}",
                    $"guildDirectiveCompletedAlt={guildDirectiveCompletedAlt.Found};confidence={guildDirectiveCompletedAlt.Confidence:F4}",
                    $"dailyThirtyCounter={dailyThirtyCounter}",
                    $"dimmedDailyRows={dimmedDailyRows}",
                    $"menuMail={menuMail.Found};confidence={menuMail.Confidence:F4}",
                    $"mailPage={mailPage.Found};confidence={mailPage.Confidence:F4}",
                    $"mailReceiveAll={mailReceiveAll.Found};confidence={mailReceiveAll.Confidence:F4}",
                    $"mailItems={mailItems.Found};confidence={mailItems.Confidence:F4}",
                    $"mailEmpty={mailEmpty.Found};confidence={mailEmpty.Confidence:F4}",
                    $"mailNotification={mailNotification}",
                    $"restorationIcon={restorationIcon.Found};confidence={restorationIcon.Confidence:F4}",
                    $"ta1Arrival={ta1Arrival.Found};confidence={ta1Arrival.Confidence:F4}",
                    $"shop={shop.Found};confidence={shop.Confidence:F4}",
                    $"directiveAccept={directiveAccept.Found};confidence={directiveAccept.Confidence:F4}",
                    $"directiveDecline={directiveDecline.Found};confidence={directiveDecline.Confidence:F4}"
                ]);
            Shutdown();
            return;
        }

        var selfTestIndex = Array.IndexOf(e.Args, "--self-test");
        if (selfTestIndex >= 0 && selfTestIndex + 1 < e.Args.Length)
        {
            var reportPath = Path.GetFullPath(e.Args[selfTestIndex + 1]);
            try
            {
                await RunSelfTestAsync(reportPath);
                Shutdown();
            }
            catch (Exception exception)
            {
                await File.WriteAllTextAsync(reportPath + ".error.txt", exception.ToString());
                Shutdown(1);
            }
            return;
        }

        var updateProbeIndex = Array.IndexOf(e.Args, "--update-probe");
        if (updateProbeIndex >= 0 && updateProbeIndex + 1 < e.Args.Length)
        {
            var update = await new AppUpdateService().CheckAsync(new Version(0, 0), CancellationToken.None);
            await File.WriteAllLinesAsync(
                Path.GetFullPath(e.Args[updateProbeIndex + 1]),
                update is null
                    ? ["available=false"]
                    :
                    [
                        "available=true",
                        $"version={update.Version.ToString(3)}",
                        $"installer={update.InstallerUrl}",
                        $"checksum={update.ChecksumUrl}"
                    ]);
            Shutdown();
            return;
        }

        var screenshotIndex = Array.IndexOf(e.Args, "--screenshot");
        var isScreenshotMode = screenshotIndex >= 0 && screenshotIndex + 1 < e.Args.Length;
        try
        {
            var activation = new ActivationService();
            if (activation.IsRequired && !activation.HasValidActivation())
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                if (new ActivationWindow(activation).ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
                ShutdownMode = ShutdownMode.OnLastWindowClose;
            }
        }

        catch (Exception exception)
        {
            MessageBox.Show($"Não foi possível verificar a ativação: {exception.Message}",
                "PEXBOT", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        var window = new MainWindow();
        _ = Task.Run(() => VisualRecognitionService.CleanupOldDiagnosticsAsync());
        if (isScreenshotMode)
        {
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -32000;
            window.Top = -32000;
        }

        MainWindow = window;
        window.Show();

        if (isScreenshotMode && e.Args.Contains("--statistics-tab", StringComparer.Ordinal))
        {
            window.ShowStatisticsForScreenshot();
        }
        else if (isScreenshotMode && e.Args.Contains("--diagnostics-tab", StringComparer.Ordinal))
        {
            window.ShowDiagnosticsForScreenshot();
        }
        else if (isScreenshotMode && e.Args.Contains("--updates-tab", StringComparer.Ordinal))
        {
            window.ShowUpdatesForScreenshot();
        }
        else if (isScreenshotMode && e.Args.Contains("--abbey-tab", StringComparer.Ordinal))
        {
            window.ShowAbbeyForScreenshot();
        }
        else if (isScreenshotMode && e.Args.Contains("--protection-tab", StringComparer.Ordinal))
        {
            window.ShowProtectionForScreenshot();
        }
        else if (isScreenshotMode && e.Args.Contains("--routines-tab", StringComparer.Ordinal))
        {
            window.ShowRoutinesForScreenshot();
        }
        else if (isScreenshotMode && e.Args.Contains("--farm-schedule-tab", StringComparer.Ordinal))
        {
            window.ShowFarmScheduleForScreenshot();
        }
        else if (isScreenshotMode && e.Args.Contains("--execution-view", StringComparer.Ordinal))
        {
            window.ShowExecutionPreviewForScreenshot();
        }

        if (!isScreenshotMode)
        {
            return;
        }

        await Task.Delay(e.Args.Contains("--statistics-tab", StringComparer.Ordinal) ? 5000 : 1600);
        if (e.Args.Contains("--execution-view", StringComparer.Ordinal))
            window.ShowExecutionPreviewForScreenshot();
        if (e.Args.Contains("--start-preview", StringComparer.Ordinal))
            window.ShowStartPreviewForScreenshot();
        if (e.Args.Contains("--party-preview", StringComparer.Ordinal))
            window.ShowPartyForScreenshot();
        if (e.Args.Contains("--special-routines-preview", StringComparer.Ordinal))
            window.ShowSpecialRoutinesForScreenshot();
        var outputPath = Path.GetFullPath(e.Args[screenshotIndex + 1]);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        window.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using (var stream = File.Create(outputPath))
        {
            encoder.Save(stream);
        }

        window.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _installationMutex?.Dispose();
        base.OnExit(e);
    }

    private static async Task RunSelfTestAsync(string outputPath)
    {
        await WindowsInputService.VerifyDeferredFocusAsync();
        BotAutomationEngine.VerifyHuntActivationPolicy();
        BotAutomationEngine.VerifyRecoveryObservationPolicy();
        BotAutomationEngine.VerifySchedulePolicy();
        BotAutomationEngine.VerifyDeathRestorationPolicy();
        BotAutomationEngine.VerifyStartupObservationPolicy();
        BotAutomationEngine.VerifyWorkflowStabilityPolicy();
        BotAutomationEngine.VerifyDeathPriorityPolicy();
        BotAutomationEngine.VerifyAutoStoragePolicy();
        BotAutomationEngine.VerifyScheduleClockPolicy();
        if (!HumanInteractionMonitor.IsPhysicalEvent(true, 0) || !HumanInteractionMonitor.IsPhysicalEvent(false, 0) ||
            HumanInteractionMonitor.IsPhysicalEvent(true, 1) || HumanInteractionMonitor.IsPhysicalEvent(false, 16))
            throw new InvalidOperationException("Comandos do bot confundidos com interação manual.");
        LoveBossSchedule.VerifyPolicy();
        var database = new AppDatabase(outputPath + ".data");
        await database.InitializeAsync();
        await database.SaveSettingAsync("client1.routines.dailyCycle", "2026-09-23");
        await database.MigrateRuntimeProfileAsync("first-user");
        await database.MigrateRuntimeProfileAsync("second-user");
        if (await database.GetSettingAsync("users.first-user.client1.routines.dailyCycle") != "2026-09-23" ||
            !string.IsNullOrEmpty(await database.GetSettingAsync("users.second-user.client1.routines.dailyCycle")))
            throw new InvalidOperationException("Migração de estado vazou entre usuários.");
        var recognition = new VisualRecognitionService(database, new ScreenCaptureService());
        var referenceDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "References");
        await BotAutomationEngine.VerifyReconnectFixturesAsync(recognition, file => LoadReferenceFrame(Path.Combine(referenceDirectory, file)));
        await PartyRegression.VerifyAsync(recognition, file => LoadReferenceFrame(Path.Combine(referenceDirectory, file)));
        await SpecialRoutineRegression.VerifyAsync(recognition, file => LoadReferenceFrame(Path.Combine(referenceDirectory, file)));
        await FlowVisualRegression.VerifyAsync(recognition, file => LoadReferenceFrame(Path.Combine(referenceDirectory, file)));
        foreach (var loadingFixture in new[] { "regression_loading_city.png", "regression_loading_landscape.png" })
            if (!await LoadingScreenReader.IsLoadingAsync(LoadReferenceFrame(Path.Combine(referenceDirectory, loadingFixture)), CancellationToken.None))
                throw new InvalidOperationException($"Tela real de carregamento não reconhecida: {loadingFixture}");
        if (await LoadingScreenReader.IsLoadingAsync(LoadReferenceFrame(Path.Combine(referenceDirectory, "hud_auto_active.png")), CancellationToken.None))
            throw new InvalidOperationException("HUD de farm confundido com carregamento.");
        var occupiedTa = LoadReferenceFrame(Path.Combine(referenceDirectory, "ta1_occupied_regression.png"));
        if (TaEntryButtonAnalyzer.Read(occupiedTa, TaDestination.Ta1Codex).State != TaEntryButtonState.Disabled ||
            !(await recognition.FindAsync("ta1_primeiro_cartao", occupiedTa, CancellationToken.None)).Found ||
            !await new TaEntryTextReader().HasFirstEntryAsync(occupiedTa, CancellationToken.None))
            throw new InvalidOperationException("Regressão: T.A 1 ocupada não reconhecida na captura real.");
        foreach (var (file, expected) in new[] { ("hud_auto_active.png", OpenHudHuntState.Active), ("hud_auto_inactive.png", OpenHudHuntState.Inactive), ("guild_treasure_empty.png", OpenHudHuntState.Unknown) })
        {
            var actual = await OpenHudHuntReader.ReadAsync(recognition, LoadReferenceFrame(Path.Combine(referenceDirectory, file)), CancellationToken.None);
            if (actual != expected) throw new InvalidOperationException($"Auto HUD {file}: esperado={expected}; observado={actual}");
        }
        foreach (var id in new[] { "ta1_left_open", "ta1_right_open" })
        foreach (var file in new[] { "mapa_ta2_favorito_unico.png", "ta1_map.png", "ta_selector_user_active.png", "daily_page.png" })
        {
            var result = await recognition.FindInImageAsync(id, Path.Combine(referenceDirectory, file));
            await File.AppendAllTextAsync(outputPath + ".ta1.txt", $"{id}; {file}; found={result.Found}; score={result.Confidence:F4}\n");
            if (result.Found != (file == "mapa_ta2_favorito_unico.png"))
                throw new InvalidOperationException($"Estado da lateral aberta incorreto: {id}, {file}, {result.Confidence}");
        }
        foreach (var id in new[] { "ta1_left_collapsed", "ta1_right_collapsed" })
        foreach (var file in new[] { "ta1_map.png", "ta1_map_zoom_max.png", "ta_selector_user_active.png", "daily_page.png" })
        {
            var result = await recognition.FindInImageAsync(id, Path.Combine(referenceDirectory, file));
            await File.AppendAllTextAsync(outputPath + ".ta1.txt", $"{id}; {file}; found={result.Found}; score={result.Confidence:F4}\n");
            if (result.Found != file.StartsWith("ta1_map", StringComparison.Ordinal))
                throw new InvalidOperationException($"Estado da lateral incorreto: {id}, {file}, {result.Confidence}");
        }
        await StatisticsRegression.VerifyAsync(outputPath + ".statistics.txt", recognition,
            name => LoadReferenceFrame(Path.Combine(referenceDirectory, name)));
        var treasureChecks = await GuildTreasureRegression.VerifyAsync(recognition, database,
            name => LoadReferenceFrame(Path.Combine(referenceDirectory, name)));
        await File.WriteAllLinesAsync(outputPath + ".treasure.txt", treasureChecks);
        var tombstoneChecks = await TombstoneIconRegression.VerifyAsync(recognition,
            name => LoadReferenceFrame(Path.Combine(referenceDirectory, name)));
        await File.WriteAllLinesAsync(outputPath + ".tombstone.txt", tombstoneChecks);
        foreach (var file in new[] { "anonymous_exit_confirmation.png", "anonymous_arrival_regression.png", "ta_selector_user_active.png" })
        {
            var popup = await recognition.FindInImageAsync("anonymous_exit_confirmation", Path.Combine(referenceDirectory, file));
            if (popup.Found != (file == "anonymous_exit_confirmation.png"))
                throw new InvalidOperationException($"Confirmação de saída da Anônima incorreta: {file}; score={popup.Confidence}");
        }
        foreach (var file in new[] { "anonymous_arrival_regression.png", "anonymous_arrival_inventory.png", "ta_selector_user_active.png" })
        {
            var evidence = new List<string>();
            var inside = await BotAutomationEngine.ReadAnonymousFrameAsync(recognition,
                LoadReferenceFrame(Path.Combine(referenceDirectory, file)), CancellationToken.None, evidence.Add);
            await File.AppendAllTextAsync(outputPath, $"Anonymous regression {file}: {inside}; {string.Join("; ", evidence)}\n");
            if (inside != file.StartsWith("anonymous_", StringComparison.Ordinal))
                throw new InvalidOperationException($"Chegada à Anônima incorreta: {file}; {string.Join("; ", evidence)}");
        }
        foreach (var file in new[] { "boss_room.png", "daily_automatic.png", "ta1_arrival.png", "agenda_tela.png", "daily_page.png" })
        {
            var negativePanel = await recognition.FindInImageAsync("painel_restauracao", Path.Combine(referenceDirectory, file));
            if (negativePanel.Found)
                throw new InvalidOperationException($"Painel de restauração falsamente reconhecido em {file}: {negativePanel.Confidence}");
        }
        var restorationFixture = LoadReferenceFrame(Path.Combine(referenceDirectory, "perda_exp.png"));
        var restorationIcon = await recognition.FindAsync("icone_perda_exp", restorationFixture);
        if (!restorationIcon.Found || !TombstoneIconAnalyzer.HasRedIcon(restorationFixture))
            throw new InvalidOperationException("Lápide real deixou de ser reconhecida.");
        BotAutomationEngine.VerifyDailyListFixture(LoadReferenceFrame(Path.Combine(referenceDirectory, "daily_automatic.png")));
        var bossRewards = LoadReferenceFrame(Path.Combine(referenceDirectory, "boss_reward_panel.png"));
        var bossWindowPixels = new byte[bossRewards.Stride * 1040];
        Buffer.BlockCopy(bossRewards.Pixels, 0, bossWindowPixels, 0, bossWindowPixels.Length);
        var bossWindow = new PixelFrame(bossRewards.Width, 1040, bossRewards.Stride, bossWindowPixels);
        var bossReader = new LoveBossReader();
        foreach (var rewardFrame in new[] { bossRewards, bossWindow })
        {
            var mission = await bossReader.ReadMissionAsync(rewardFrame, CancellationToken.None);
            if (mission.DailyCompleted != 1 || mission.WeeklyCompleted != 1 ||
                await bossReader.IsRewardClaimedAsync(rewardFrame, false, CancellationToken.None))
                throw new InvalidOperationException($"Contadores/recompensa do Boss não reconhecidos na captura {rewardFrame.Height}: {mission.Evidence}");
        }
        var activeTaSelector = LoadReferenceFrame(
            Path.Combine(referenceDirectory, "seletor_ta_tela.png"));
        var userSelector = LoadReferenceFrame(Path.Combine(referenceDirectory, "ta_selector_user_active.png"));
        var borderedPixels = new byte[1936 * 1056 * 4];
        for (var row = 0; row < 1040; row++)
            Buffer.BlockCopy(userSelector.Pixels, row * userSelector.Stride,
                borderedPixels, ((row + 8) * 1936 + 8) * 4, 1920 * 4);
        var borderedSelector = new PixelFrame(1936, 1056, 1936 * 4, borderedPixels);
        foreach (var selectorFrame in new[] { userSelector, activeTaSelector, borderedSelector })
        {
            var normalized = VisualRecognitionService.NormalizeForReferenceMatching(selectorFrame);
            var located = new RecognitionResult[3];
            for (var index = 0; index < 3; index++)
                located[index] = await recognition.FindAsync("entrar_ta2_pronto", normalized,
                    435 + index * 272, 730, 135, 80, CancellationToken.None);
            if (located.Any(result => !result.Found))
                throw new InvalidOperationException("Localização das letras Entrar falhou na captura completa.");
            var actual = TaEntryButtonAnalyzer.Read(normalized, TaDestination.Ta3, located);
            if (actual.State != TaEntryButtonState.Active)
                throw new InvalidOperationException($"T.A 3 ativa não reconhecida: {actual}");
            var ta1 = await recognition.FindAsync("ta1_entry_label", normalized,
                435, 730, 135, 80, CancellationToken.None);
            var isolated = Enumerable.Repeat(new RecognitionResult(false, 0, 0, 0), 3).ToArray();
            isolated[0] = ta1;
            if (!ta1.Found || TaEntryButtonAnalyzer.Read(normalized, TaDestination.Ta1Codex, isolated).State != TaEntryButtonState.Active)
                throw new InvalidOperationException($"T.A 1 deve funcionar mesmo sem reconhecer os outros cartões: {ta1}");
            isolated[0] = new RecognitionResult(false, 0, 0, 0);
            if (TaEntryButtonAnalyzer.Read(normalized, TaDestination.Ta1Codex, isolated).State != TaEntryButtonState.Unknown)
                throw new InvalidOperationException("T.A 1 não pode ser autorizada sem localizar seu próprio botão.");
        }
        var realButtons = LoadReferenceFrame(Path.Combine(referenceDirectory, "ta_entry_states_real.png"));
        var disabledSelector = VisualRecognitionService.NormalizeForReferenceMatching(
            LoadReferenceFrame(Path.Combine(referenceDirectory, "ta1_disabled_regression.png")));
        var disabledLocated = new RecognitionResult[3];
        for (var index = 0; index < 3; index++)
            disabledLocated[index] = await recognition.FindAsync(index == 0 ? "ta1_entry_label" : "entrar_ta2_pronto",
                disabledSelector, 435 + index * 272, 730, 135, 80, CancellationToken.None);
        var disabledReading = TaEntryButtonAnalyzer.Read(disabledSelector, TaDestination.Ta1Codex, disabledLocated);
        if (disabledReading.State != TaEntryButtonState.Disabled)
            throw new InvalidOperationException($"T.A 1 indisponível no diagnóstico real: {disabledReading}");
        var recoveryMap = VisualRecognitionService.NormalizeForReferenceMatching(
            LoadReferenceFrame(Path.Combine(referenceDirectory, "anonymous_map_recovery_regression.png")));
        if (!await BotAutomationEngine.ReadAnonymousFrameAsync(recognition, recoveryMap, CancellationToken.None))
            throw new InvalidOperationException("Mapa real da Anônima foi confundido com cidade na recuperação de Artigos.");
        if (TaEntryButtonAnalyzer.MeasureLabelInk(realButtons, 28, 24, 75, 25) >= 155 ||
            TaEntryButtonAnalyzer.MeasureLabelInk(realButtons, 300, 24, 75, 25) < 180)
            throw new InvalidOperationException("Os estados reais de Entrar foram confundidos.");
        var restHud = await recognition.FindInImageAsync("game_hud_menu", Path.Combine(referenceDirectory, "descanso_generico.png"));
        var normalHud = await recognition.FindInImageAsync("game_hud_menu", Path.Combine(referenceDirectory, "boss_room.png"));
        if (restHud.Found || !normalHud.Found)
            throw new InvalidOperationException($"HUD incorreto: descanso={restHud.Confidence:F3}, jogo={normalHud.Confidence:F3}.");
        foreach (var destination in new[]
                 { TaDestination.Ta1Codex, TaDestination.Ta2, TaDestination.Ta3 })
        {
            var button = TaEntryButtonAnalyzer.Read(activeTaSelector, destination);
            if (button.State != TaEntryButtonState.Active)
                throw new InvalidOperationException(
                    $"Entrar da {destination} ativo não reconhecido ({button.TargetLuma:F0}).");

            var dimmedPixels = (byte[])activeTaSelector.Pixels.Clone();
            var buttonX = destination switch
            {
                TaDestination.Ta1Codex => 462,
                TaDestination.Ta2 => 733,
                _ => 1007
            };
            for (var y = 757; y < 789; y++)
            for (var x = buttonX; x < buttonX + 75; x++)
            {
                var offset = y * activeTaSelector.Stride + x * 4;
                for (var channel = 0; channel < 3; channel++)
                    dimmedPixels[offset + channel] = (byte)(dimmedPixels[offset + channel] * 0.45);
            }
            var dimmed = new PixelFrame(
                activeTaSelector.Width, activeTaSelector.Height,
                activeTaSelector.Stride, dimmedPixels);
            if (TaEntryButtonAnalyzer.Read(dimmed, destination).State != TaEntryButtonState.Disabled)
                throw new InvalidOperationException($"Entrar apagado da {destination} não reconhecido.");
        }
        foreach (var (referenceId, fileName) in new[]
        {
            ("ta3_entry_disabled", "ta3_entry_disabled.png"),
            ("boss_auto_on", "boss_auto_on.png"),
            ("boss_victory", "boss_victory.png"),
            ("boss_exit_timer", "boss_exit_timer.png"),
            ("boss_exit_popup", "boss_exit_popup.png"),
            ("boss_exit_ok", "boss_exit_popup.png"),
            ("boss_reward_button", "boss_reward_button.png")
        })
        {
            var own = await recognition.FindInCroppedImageAsync(
                referenceId, Path.Combine(referenceDirectory, fileName));
            if (!own.Found)
                throw new InvalidOperationException($"Referência {referenceId} falhou na própria imagem ({own.Confidence:P0}).");
        }
        var disabledOnReadySelector = await recognition.FindInImageAsync(
            "ta3_entry_disabled", Path.Combine(referenceDirectory, "seletor_ta_tela.png"));
        if (disabledOnReadySelector.Found)
            throw new InvalidOperationException(
                "O botão apagado da T.A 3 foi confundido com o botão ativo do seletor.");
        var cases = new[]
        {
            ("guild_checkin_available", "guild_checkin_page.png"),
            ("guild_checkin_reward", "guild_checkin_reward.png"),
            ("guild_donation_panel", "guild_donation_panel.png"),
            ("guild_page", "guild_page.png"),
            ("guild_directive_page", "guild_directive_page.png"),
            ("guild_directive_accepted", "guild_directive_accepted.png"),
            ("guild_directive_in_progress", "guild_directive_in_progress.png"),
            ("campaign_page", "campaign_page.png"),
            ("daily_page", "daily_page.png"),
            ("daily_all_accepted", "daily_all_accepted.png"),
            ("daily_30_accepted", "daily_all_accepted.png"),
            ("daily_automatic", "daily_automatic.png"),
            ("agenda_popup_ok", "aviso_agenda.png"),
            ("ta1_chegada", "ta1_arrival.png"),
            ("mapa_ta1", "ta1_map.png"),
            ("mapa_ta1_zoom_max", "ta1_map_zoom_max.png"),
            ("abadia_especial", "abadia_pagina_especial.png"),
            ("abadia_cartao", "abadia_pagina_especial.png"),
            ("abadia_chegada_silencio", "abadia_chegada_silencio.png"),
            ("abadia_chegada_apreciacao", "abadia_chegada_apreciacao.png"),
            ("abadia_chegada_silencio", "abadia_chegada_apreciacao.png"),
            ("abadia_chegada_apreciacao", "abadia_chegada_silencio.png"),
            ("mapa_abadia", "mapa_abadia.png"),
            ("mapa_abadia", "abadia_chegada_apreciacao.png"),
            ("anonymous_epic_tab", "anonymous_epic_page.png"),
            ("anonymous_tenerys_card", "anonymous_tenerys_page.png"),
            ("anonymous_level_panel", "anonymous_tenerys_page.png"),
            ("anonymous_entry_confirmation", "anonymous_entry_confirmation.png"),
            ("anonymous_arrival", "anonymous_arrival.png"),
            ("anonymous_map", "anonymous_map.png"),
            ("anonymous_go", "anonymous_map_selected.png"),
            ("daily_shop_page", "daily_shop_open.png"),
            ("daily_shop_coins", "daily_shop_common.png"),
            ("daily_shop_common", "daily_shop_common.png"),
            ("daily_shop_bulk", "daily_shop_common.png"),
            ("daily_shop_bulk_popup", "daily_shop_bulk_popup_full.png"),
            ("daily_shop_bulk_title", "daily_shop_bulk_popup_full.png"),
            ("daily_shop_result", "daily_shop_result.png"),
            ("daily_shop_summon", "daily_shop_summon.png"),
            ("boss_entry_panel", "boss_entry_panel.png"),
            ("boss_entry_button", "boss_entry_panel.png"),
            ("boss_room", "boss_room.png"),
            ("boss_alive", "boss_alive.png"),
            ("boss_reward_panel", "boss_reward_panel.png"),
            ("boss_reward_received", "boss_reward_received.png"),
            ("menu_masmorra", "menu_aberto.png"),
            ("tela_masmorras", "tela_masmorras.png"),
            ("confirmar_sepheras", "confirmar_sepheras.png"),
            ("atalaia_erodida", "atalaia_erodida_tela.png"),
            ("caca_automatica", "descanso_sepheras.png"),
            ("tela_descanso", "descanso_generico.png"),
            ("tela_descanso", "descanso_sepheras.png"),
            ("tela_descanso", "descanso_movendo.png"),
            ("tela_descanso", "descanso_aguardando_spot.png"),
            ("tela_descanso", "ta2_chegada.png"),
            ("menu_ta", "teste_menu_ta.png"),
            ("seletor_ta", "seletor_ta_tela.png"),
            ("entrar_ta2_pronto", "seletor_ta_tela.png"),
            ("entrar_ta3_pronto", "seletor_ta_tela.png"),
            ("ta3_chegada", "ta3_chegada.png"),
            ("ta2_chegada", "ta2_chegada.png"),
            ("ta3_artigos", "teste_ta3_artigos.png"),
            ("loja_artigos", "loja_artigos.png"),
            ("compra_concluida", "compra_concluida.png"),
            ("compra_concluida", "compra_concluida_v2.png"),
            ("mapa_ta3", "mapa_ta3.png"),
            ("mapa_aberto", "mapa_ta3.png"),
            ("mapa_aberto", "mapa_ta2_favorito_unico.png"),
            ("mapa_aberto", "botao_ir_ta2_tela.png"),
            ("mapa_aberto", "teste_favorito_cliente2.png"),
            ("mapa_aberto_ta2_ir", "botao_ir_ta2_tela.png"),
            ("mapa_aberto_ta2_ir", "mapa_ta2_favorito_unico.png"),
            ("mapa_aberto_ta2_ir", "ta2_chegada.png"),
            ("aba_favoritos", "mapa_posto_favoritos.png"),
            ("aba_favoritos", "mapa_ta2_favorito_unico.png"),
            ("aba_favoritos", "teste_favorito_cliente2.png"),
            ("segundo_favorito_teleporte", "mapa_posto_favoritos.png"),
            ("segundo_favorito_teleporte", "mapa_ta2_favorito_unico.png"),
            ("mapa_favoritos", "teste_mapa_favoritos.png"),
            ("posto_patrulha_sul", "posto_patrulha_sul.png"),
            ("mapa_posto_informacoes", "mapa_posto_informacoes.png"),
            ("mapa_terra_fogo_silex", "mapa_terra_fogo_silex.png"),
            ("mapa_posto_favoritos", "mapa_posto_favoritos.png"),
            ("botao_ir", "teste_botao_ir.png"),
            ("botao_ir", "botao_ir_tela_v2.png"),
            ("botao_ir", "botao_ir_ta2_tela.png"),
            ("botao_ir_legado", "teste_botao_ir.png"),
            ("botao_ir_legado", "botao_ir_tela_v2.png"),
            ("botao_ir_legado", "botao_ir_ta2_tela.png"),
            ("botao_ir_ta2", "botao_ir_ta2_tela.png"),
            ("botao_ir_ta2", "mapa_ta2_favorito_unico.png"),
            ("botao_ir_ta2", "botao_ir_tela_v2.png"),
            ("descanso_ponto_fixo", "descanso_ponto_fixo.png"),
            ("descanso_movendo", "descanso_movendo.png"),
            ("descanso_aguardando_spot", "descanso_aguardando_spot.png"),
            ("descanso_movendo", "descanso_ponto_fixo.png"),
            ("descanso_aguardando_spot", "descanso_ponto_fixo.png"),
            ("descanso_ponto_fixo", "descanso_movendo.png"),
            ("descanso_aguardando_spot", "descanso_movendo.png"),
            ("descanso_ponto_fixo", "descanso_aguardando_spot.png"),
            ("descanso_movendo", "descanso_aguardando_spot.png"),
            ("morte_confirmada", "morte_confirmada.png"),
            ("morte_confirmada", "perda_exp.png"),
            ("morte_confirmada_ta2", "morte_confirmada_ta2.png"),
            ("morte_confirmada_ta2", "morte_confirmada.png"),
            ("morte_confirmada_ta2", "descanso_generico.png"),
            ("morte_titulo", "morte_confirmada.png"),
            ("morte_titulo", "descanso_generico.png"),
            ("morte_ressuscitar", "morte_confirmada.png"),
            ("morte_ressuscitar", "descanso_generico.png"),
            ("morte_ressuscitar", "perda_exp.png"),
            ("descanso_morte", "descanso_morte_ta2.png"),
            ("descanso_morte", "descanso_generico.png"),
            ("descanso_morte", "descanso_aguardando_spot.png"),
            ("menu_agenda", "agenda_tela.png"),
            ("perda_exp", "perda_exp.png"),
            ("perda_exp", "agenda_tela.png"),
            ("painel_restauracao", "perda_exp.png"),
            ("painel_restauracao", "agenda_tela.png"),
            ("icone_perda_exp", "perda_exp.png"),
            ("icone_perda_exp", "agenda_tela.png"),
            ("agenda_tela", "agenda_tela.png"),
            ("agenda_tela", "perda_exp.png"),
            ("aviso_agenda", "aviso_agenda.png"),
            ("aviso_agenda", "ta2_chegada.png"),
            ("oferta_wemade", "oferta_wemade.png"),
            ("oferta_wemade", "agenda_tela.png")
        };
        var lines = new List<string>();
        var tombstonePresent = TombstoneIconAnalyzer.HasRedIcon(
            LoadReferenceFrame(Path.Combine(referenceDirectory, "perda_exp.png")));
        var tombstoneAbsent = TombstoneIconAnalyzer.HasRedIcon(
            LoadReferenceFrame(Path.Combine(referenceDirectory, "agenda_tela.png")));
        lines.Add($"tombstone_color|present={tombstonePresent}|absent={tombstoneAbsent}");
        if (!tombstonePresent || tombstoneAbsent)
            throw new InvalidOperationException("A checagem de cor da lápide não distinguiu perda real de tela normal.");
        var bulkTitleOnSummonPage = await recognition.FindInImageAsync(
            "daily_shop_bulk_title", Path.Combine(referenceDirectory, "daily_shop_summon.png"));
        if (bulkTitleOnSummonPage.Found)
            throw new InvalidOperationException("Popup da compra em lote confundido com a página de Invocação.");
        var requiredNewReferences = new HashSet<string>(StringComparer.Ordinal)
        {
            "guild_checkin_available", "guild_checkin_reward", "guild_donation_panel",
            "guild_page", "guild_directive_page", "guild_directive_accepted", "guild_directive_in_progress",
            "campaign_page", "daily_page", "daily_all_accepted", "daily_30_accepted",
            "daily_automatic",
            "agenda_popup_ok", "ta1_chegada", "mapa_ta1", "mapa_ta1_zoom_max",
            "anonymous_epic_tab", "anonymous_tenerys_card", "anonymous_level_panel",
            "anonymous_entry_confirmation", "anonymous_arrival", "anonymous_map", "anonymous_go"
            , "daily_shop_page", "daily_shop_coins", "daily_shop_common", "daily_shop_bulk", "daily_shop_bulk_popup", "daily_shop_bulk_title"
        };
        foreach (var (referenceId, fileName) in cases)
        {
            var result = await recognition.FindInImageAsync(
                referenceId,
                Path.Combine(referenceDirectory, fileName));
            lines.Add(
                $"{referenceId}|image={fileName}|found={result.Found}|confidence={result.Confidence:F4}|x={result.X}|y={result.Y}");
            if (requiredNewReferences.Contains(referenceId) && !result.Found)
            {
                throw new InvalidOperationException(
                    $"A nova referência {referenceId} não reconheceu sua própria imagem ({result.Confidence:P0}).");
            }
        }

        var dailyTeleport = await recognition.FindInCroppedImageAsync(
            "daily_teleport", Path.Combine(referenceDirectory, "daily_teleport.png"));
        lines.Add($"daily_teleport|image=daily_teleport.png|found={dailyTeleport.Found}|confidence={dailyTeleport.Confidence:F4}");
        if (!dailyTeleport.Found)
        {
            throw new InvalidOperationException(
                $"A confirmação de teleporte das Diárias não foi reconhecida no recorte ({dailyTeleport.Confidence:P0}).");
        }

        foreach (var referenceId in new[] { "daily_teleport_resource", "daily_teleport_ok" })
        {
            var popupPart = await recognition.FindInCroppedImageAsync(
                referenceId, Path.Combine(referenceDirectory, "daily_teleport.png"));
            lines.Add($"{referenceId}|image=daily_teleport.png|found={popupPart.Found}|confidence={popupPart.Confidence:F4}");
            if (!popupPart.Found)
            {
                throw new InvalidOperationException(
                    $"A referência {referenceId} não reconheceu seu próprio recorte ({popupPart.Confidence:P0}).");
            }
        }

        var completedGuildDirective = await recognition.FindInCroppedImageAsync(
            "guild_directive_completed", Path.Combine(referenceDirectory, "guild_directive_completed.png"));
        lines.Add($"guild_directive_completed|image=guild_directive_completed.png|found={completedGuildDirective.Found}|confidence={completedGuildDirective.Confidence:F4}");
        if (!completedGuildDirective.Found)
        {
            throw new InvalidOperationException(
                $"A Diretiva concluída não foi reconhecida no recorte ({completedGuildDirective.Confidence:P0}).");
        }

        var alternateCompletedDirective = await recognition.FindInCroppedImageAsync(
            "guild_directive_completed_alt",
            Path.Combine(referenceDirectory, "guild_directive_completed_alt.png"));
        lines.Add($"guild_directive_completed_alt|own={alternateCompletedDirective.Found}|confidence={alternateCompletedDirective.Confidence:F4}");
        if (!alternateCompletedDirective.Found)
        {
            throw new InvalidOperationException("A referência alternativa da Diretiva 5/5 não reconheceu sua própria imagem.");
        }

        var activeDirective = await recognition.FindInImageAsync(
            "guild_directive_completed_alt",
            Path.Combine(referenceDirectory, "guild_directive_in_progress.png"));
        lines.Add($"guild_directive_completed_alt|active={activeDirective.Found}|confidence={activeDirective.Confidence:F4}");
        if (activeDirective.Found)
        {
            throw new InvalidOperationException("A referência alternativa 5/5 confundiu uma Diretiva ativa com conclusão.");
        }

        var taEntryText = new TaEntryTextReader();
        var taSelectorText = await taEntryText.HasFirstEntryAsync(
            LoadReferenceFrame(Path.Combine(referenceDirectory, "seletor_ta_tela.png")),
            CancellationToken.None);
        var taArrivalText = await taEntryText.HasFirstEntryAsync(
            LoadReferenceFrame(Path.Combine(referenceDirectory, "ta1_arrival.png")),
            CancellationToken.None);
        lines.Add($"ta1_entry_text|selector={taSelectorText}|arrival={taArrivalText}");
        if (!taSelectorText || taArrivalText)
        {
            throw new InvalidOperationException("A leitura do botão Entrar da T.A 1 confundiu o seletor com a chegada.");
        }

        var taFirstCardSelector = await recognition.FindInImageAsync(
            "ta1_primeiro_cartao", Path.Combine(referenceDirectory, "seletor_ta_tela.png"));
        var taFirstCardArrival = await recognition.FindInImageAsync(
            "ta1_primeiro_cartao", Path.Combine(referenceDirectory, "ta1_arrival.png"));
        lines.Add($"ta1_first_card|selector={taFirstCardSelector.Found}|arrival={taFirstCardArrival.Found}");
        if (!taFirstCardSelector.Found || taFirstCardArrival.Found)
        {
            throw new InvalidOperationException("O primeiro cartão da T.A 1 confundiu o seletor com a chegada.");
        }

        var acceptedThirty = await recognition.HasDailyThirtyCounterInImageAsync(
            Path.Combine(referenceDirectory, "daily_all_accepted.png"));
        var unacceptedThirty = await recognition.HasDailyThirtyCounterInImageAsync(
            Path.Combine(referenceDirectory, "daily_page.png"));
        lines.Add($"daily_30_color|accepted={acceptedThirty}|unaccepted={unacceptedThirty}");
        if (!acceptedThirty || unacceptedThirty)
        {
            throw new InvalidOperationException(
                "O detector por cor do contador diário não separou corretamente 30/30 de 0/30.");
        }

        foreach (var mailReference in new[]
        {
            "menu_mail", "mail_page", "mail_receive_all", "mail_item_obtained", "mail_empty"
        })
        {
            var mailImage = Path.Combine(referenceDirectory, mailReference == "menu_mail"
                ? "mail_icon.png"
                : $"{mailReference}.png");
            var match = await recognition.FindInCroppedImageAsync(mailReference, mailImage);
            lines.Add($"{mailReference}|own={match.Found}|confidence={match.Confidence:F4}");
            if (!match.Found)
            {
                throw new InvalidOperationException($"A referência visual {mailReference} não reconheceu sua própria imagem.");
            }
        }

        var abbeyConfirmation = await recognition.FindInCroppedImageAsync(
            "abadia_confirmacao",
            Path.Combine(referenceDirectory, "abadia_confirmacao.png"));
        lines.Add($"abadia_confirmacao|image=abadia_confirmacao.png|found={abbeyConfirmation.Found}|confidence={abbeyConfirmation.Confidence:F4}");
        if (!abbeyConfirmation.Found)
        {
            throw new InvalidOperationException($"A confirmação de entrada paga na Abadia não foi reconhecida no recorte fornecido ({abbeyConfirmation.Confidence:P0}, {abbeyConfirmation.X}, {abbeyConfirmation.Y}).");
        }

        foreach (var (referenceId, ownImage, otherImage) in new[]
        {
            ("abadia_chegada_silencio", "abadia_chegada_silencio.png", "abadia_chegada_apreciacao.png"),
            ("abadia_chegada_apreciacao", "abadia_chegada_apreciacao.png", "abadia_chegada_silencio.png")
        })
        {
            var own = await recognition.FindInImageAsync(referenceId, Path.Combine(referenceDirectory, ownImage));
            var other = await recognition.FindInImageAsync(referenceId, Path.Combine(referenceDirectory, otherImage));
            if (!own.Found || other.Found)
            {
                throw new InvalidOperationException(
                    $"A confirmação da chegada na Abadia não distinguiu os dois pontos: {referenceId}, próprio={own.Confidence:P0}, outro={other.Confidence:P0}.");
            }
        }

        var offerPresent = await recognition.FindInImageAsync(
            "oferta_wemade",
            Path.Combine(referenceDirectory, "oferta_wemade.png"));
        var offerAbsent = await recognition.FindInImageAsync(
            "oferta_wemade",
            Path.Combine(referenceDirectory, "agenda_tela.png"));
        if (!offerPresent.Found || offerAbsent.Found)
        {
            throw new InvalidOperationException(
                $"Falha no teste da oferta WeMade: presente={offerPresent.Found} " +
                $"({offerPresent.Confidence:P0}), ausente={offerAbsent.Found} ({offerAbsent.Confidence:P0}).");
        }

        foreach (var fileName in new[] { "loja_artigos.png", "loja_compra_indisponivel.png" })
        {
            var luma = await recognition.MeasureAverageLumaInImageAsync(
                Path.Combine(referenceDirectory, fileName),
                300,
                980,
                165,
                45);
            lines.Add($"comprar_lote|image={fileName}|luma={luma:F2}|available={luma >= 72}");
        }

        var spotLevel = await new SpotLevelRecognitionService().RecognizeFirstFavoriteInImageAsync(
            Path.Combine(referenceDirectory, "mapa_ta2_favorito_unico.png"),
            new HashSet<int> { 68, 72, 76, 80, 84, 88 });
        lines.Add(
            $"nivel_spot|image=mapa_ta2_favorito_unico.png|level={spotLevel.Level}|text={spotLevel.Text}");
        var clientTwoSpotLevel = await new SpotLevelRecognitionService().RecognizeFirstFavoriteInImageAsync(
            Path.Combine(referenceDirectory, "teste_favorito_cliente2.png"),
            new HashSet<int> { 68, 72, 76, 80, 84, 88 });
        lines.Add(
            $"nivel_spot|image=teste_favorito_cliente2.png|level={clientTwoSpotLevel.Level}|text={clientTwoSpotLevel.Text}");
        if (spotLevel.Level != 68 || clientTwoSpotLevel.Level != 68)
        {
            throw new InvalidOperationException(
                $"Falha no teste dos favoritos: referência={spotLevel.Level}, cliente 2={clientTwoSpotLevel.Level}.");
        }
        var clientTwoMap = await recognition.FindInImageAsync(
            "mapa_aberto", Path.Combine(referenceDirectory, "teste_favorito_cliente2.png"));
        var clientTwoFavorites = await recognition.FindInImageAsync(
            "aba_favoritos", Path.Combine(referenceDirectory, "teste_favorito_cliente2.png"));
        if (!clientTwoMap.Found || !clientTwoFavorites.Found)
        {
            throw new InvalidOperationException(
                $"Falha no teste de retomada do Cliente 2: mapa={clientTwoMap.Found}, " +
                $"favoritos={clientTwoFavorites.Found}.");
        }

        var restorationReader = new RestorationCounterReader();
        var restorationCases = new[]
        {
            ("perda_exp.png", RestorationTab.Experience, 1, RestorationCountState.Pending),
            ("restauracao_exp_vazia.png", RestorationTab.Experience, 0, RestorationCountState.Empty),
            ("restauracao_equipamento_vazia.png", RestorationTab.Equipment, 0, RestorationCountState.Empty),
            ("agenda_tela.png", RestorationTab.Unknown, (int?)null, RestorationCountState.Unknown)
        };
        foreach (var (fileName, expectedTab, expectedCount, expectedState) in restorationCases)
        {
            var counter = await restorationReader.ReadImageAsync(
                Path.Combine(referenceDirectory, fileName));
            lines.Add(
                $"restauracao_contador|image={fileName}|tab={counter.Tab}|count={counter.Count}|" +
                $"capacity={counter.Capacity}|state={counter.State}|text={counter.RawText}");
            if (counter.Tab != expectedTab ||
                counter.Count != expectedCount ||
                counter.State != expectedState)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await File.WriteAllLinesAsync(outputPath, lines);
                throw new InvalidOperationException(
                    $"Falha no teste de restauração '{fileName}': esperado " +
                    $"{expectedTab}/{expectedCount}/{expectedState}, recebido " +
                    $"{counter.Tab}/{counter.Count}/{counter.State}.");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllLinesAsync(outputPath, lines);
    }

    private static PixelFrame LoadReferenceFrame(string imagePath)
    {
        using var stream = File.OpenRead(imagePath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return new PixelFrame(converted.PixelWidth, converted.PixelHeight, stride, pixels);
    }
}
