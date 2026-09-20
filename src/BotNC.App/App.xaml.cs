using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BotNC.App.Services;

namespace BotNC.App;

public partial class App : Application
{
    private Mutex? _installationMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _installationMutex = new Mutex(false, "PEXBOT.ByLIPEX.AppRunning");
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

        var imageProbeIndex = Array.IndexOf(e.Args, "--image-probe");
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
                    $"mailNotification={mailNotification}"
                ]);
            Shutdown();
            return;
        }

        var selfTestIndex = Array.IndexOf(e.Args, "--self-test");
        if (selfTestIndex >= 0 && selfTestIndex + 1 < e.Args.Length)
        {
            await RunSelfTestAsync(Path.GetFullPath(e.Args[selfTestIndex + 1]));
            Shutdown();
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
        var window = new MainWindow();
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

        if (isScreenshotMode && e.Args.Contains("--updates-tab", StringComparer.Ordinal))
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

        if (!isScreenshotMode)
        {
            return;
        }

        await Task.Delay(1600);
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
        var database = new AppDatabase();
        await database.InitializeAsync();
        var recognition = new VisualRecognitionService(database, new ScreenCaptureService());
        var referenceDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "References");
        var cases = new[]
        {
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
        var requiredNewReferences = new HashSet<string>(StringComparer.Ordinal)
        {
            "guild_page", "guild_directive_page", "guild_directive_accepted", "guild_directive_in_progress",
            "campaign_page", "daily_page", "daily_all_accepted", "daily_30_accepted",
            "daily_automatic",
            "agenda_popup_ok", "ta1_chegada", "mapa_ta1", "mapa_ta1_zoom_max"
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
                throw new InvalidOperationException(
                    $"Falha no teste de restauração '{fileName}': esperado " +
                    $"{expectedTab}/{expectedCount}/{expectedState}, recebido " +
                    $"{counter.Tab}/{counter.Count}/{counter.State}.");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllLinesAsync(outputPath, lines);
    }
}
