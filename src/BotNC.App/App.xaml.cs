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
            var target = new GameWindowService().Discover()
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
            await File.WriteAllLinesAsync(
                probeOutputPath,
                [
                    $"frame={frame.Width}x{frame.Height}",
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
            await File.WriteAllLinesAsync(
                imageProbeOutputPath,
                [
                    $"primaryDeath={primaryDeath.Found};confidence={primaryDeath.Confidence:F4}",
                    $"clientTwoDeath={clientTwoDeath.Found};confidence={clientTwoDeath.Confidence:F4}",
                    $"deathTitle={deathTitle.Found};confidence={deathTitle.Confidence:F4}",
                    $"deathButton={deathButton.Found};confidence={deathButton.Confidence:F4}",
                    $"restDeath={restDeath.Found};confidence={restDeath.Confidence:F4}",
                    $"ta2EntryReady={ta2EntryReady.Found};confidence={ta2EntryReady.Confidence:F4}",
                    $"ta3EntryReady={ta3EntryReady.Found};confidence={ta3EntryReady.Confidence:F4}"
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
            ("mapa_aberto_ta2_ir", "botao_ir_ta2_tela.png"),
            ("mapa_aberto_ta2_ir", "mapa_ta2_favorito_unico.png"),
            ("mapa_aberto_ta2_ir", "ta2_chegada.png"),
            ("aba_favoritos", "mapa_posto_favoritos.png"),
            ("aba_favoritos", "mapa_ta2_favorito_unico.png"),
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
            ("icone_perda_exp", "perda_exp.png"),
            ("icone_perda_exp", "agenda_tela.png"),
            ("agenda_tela", "agenda_tela.png"),
            ("agenda_tela", "perda_exp.png"),
            ("aviso_agenda", "aviso_agenda.png"),
            ("aviso_agenda", "ta2_chegada.png")
        };
        var lines = new List<string>();
        foreach (var (referenceId, fileName) in cases)
        {
            var result = await recognition.FindInImageAsync(
                referenceId,
                Path.Combine(referenceDirectory, fileName));
            lines.Add(
                $"{referenceId}|image={fileName}|found={result.Found}|confidence={result.Confidence:F4}|x={result.X}|y={result.Y}");
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

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllLinesAsync(outputPath, lines);
    }
}
