namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    internal enum StartupRestorationObservation { Unknown, Absent, Present }

    internal static StartupRestorationObservation ClassifyStartupRestoration(
        bool hud, bool redIcon, bool iconTemplate, bool readableCounter, bool panelHeading)
    {
        if (readableCounter || redIcon && iconTemplate) return StartupRestorationObservation.Present;
        if (hud && !redIcon && !panelHeading) return StartupRestorationObservation.Absent;
        return StartupRestorationObservation.Unknown;
    }

    private async Task<StartupRestorationObservation> ReadStartupRestorationAsync(
        ClientSession session, PauseController pause, CancellationToken token)
    {
        var previous = StartupRestorationObservation.Unknown;
        var hits = 0;
        for (var sample = 0; sample < 3; sample++)
        {
            await CheckpointAsync(pause, token);
            var frame = VisualRecognitionService.NormalizeForReferenceMatching(await CaptureClientFrameAsync(session, token));
            var hud = await recognition.FindAsync("game_hud_menu", frame, token);
            var hp = HpBarAnalyzer.Measure(frame);
            var icon = await recognition.FindAsync("icone_perda_exp", frame, token);
            var counter = await _restorationCounterReader.ReadClientFrameAsync(frame, token);
            var red = TombstoneIconAnalyzer.HasRedIcon(frame);
            var heading = RestorationCounterReader.HasRestorationHeading(counter.RawText);
            var current = ClassifyStartupRestoration(hud.Found || hp.Found, red, icon.Found,
                counter.State != RestorationCountState.Unknown, heading);
            hits = current == previous ? hits + 1 : 1;
            previous = current;
            WritePersistentOnly(session, $"startup_restoration sample={sample + 1}; decision={current}; hud={hud.Found}/{hud.Confidence:F3}; hp={hp.Found}; icon={icon.Found}/{icon.Confidence:F3}; red={red}; tab={counter.Tab}; count={counter.Count}; heading={heading}; ocr={counter.RawText}");
            if (hits >= 2 && current != StartupRestorationObservation.Unknown) return current;
            await Task.Delay(250, token);
        }
        var diagnostic = await recognition.SaveDiagnosticAsync($"startup_observation_{session.Options.Priority}",
            await CaptureClientFrameAsync(session, token));
        WritePersistentOnly(session, $"startup_restoration inconclusive; diagnostic={diagnostic}; noInput=true");
        return StartupRestorationObservation.Unknown;
    }

    internal static void VerifyStartupObservationPolicy()
    {
        if (ClassifyStartupRestoration(true, false, true, false, false) != StartupRestorationObservation.Absent ||
            ClassifyStartupRestoration(true, false, false, false, false) != StartupRestorationObservation.Absent ||
            ClassifyStartupRestoration(false, false, false, false, false) != StartupRestorationObservation.Unknown ||
            ClassifyStartupRestoration(true, true, true, false, false) != StartupRestorationObservation.Present ||
            ClassifyStartupRestoration(false, false, false, true, true) != StartupRestorationObservation.Present ||
            ClassifyStartupRestoration(true, false, false, false, true) != StartupRestorationObservation.Unknown)
            throw new InvalidOperationException("Regressão: ausência, perda confirmada e captura desconhecida se confundem.");
    }
}
