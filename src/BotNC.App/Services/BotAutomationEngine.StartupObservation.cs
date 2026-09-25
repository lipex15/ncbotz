namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    internal enum StartupRestorationObservation { Unknown, Absent, Present }

    internal static StartupRestorationObservation ClassifyStartupRestoration(
        bool hud, bool redIcon, bool iconTemplate, bool readableCounter, bool panelHeading,
        bool strongUnconfirmedShape = false)
    {
        if (readableCounter || redIcon && iconTemplate) return StartupRestorationObservation.Present;
        // Red decoration alone is not a pending restoration and must not start a hunt.
        if (hud && !(redIcon && iconTemplate) && !panelHeading && !strongUnconfirmedShape)
            return StartupRestorationObservation.Absent;
        return StartupRestorationObservation.Unknown;
    }

    private async Task<StartupRestorationObservation> ReadStartupRestorationAsync(
        ClientSession session, PauseController pause, CancellationToken token)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var previous = StartupRestorationObservation.Unknown;
        var hits = 0;
        TombstoneIconReading? previousIcon = null;
        var loadingDeadline = DateTime.UtcNow.AddSeconds(60);
        for (var sample = 0; sample < 3; sample++)
        {
            await CheckpointAsync(pause, token);
            var frame = VisualRecognitionService.NormalizeForReferenceMatching(await CaptureClientFrameAsync(session, token));
            if (await LoadingScreenReader.IsLoadingAsync(frame, token))
            {
                WritePersistentOnly(session, $"restoration_loading elapsedMs={elapsed.ElapsedMilliseconds}; decision=wait; noInput=true; frame={frame.Width}x{frame.Height}");
                if (DateTime.UtcNow >= loadingDeadline) break;
                previous = StartupRestorationObservation.Unknown;
                hits = 0;
                sample--;
                await Task.Delay(1000, token);
                continue;
            }
            var hud = await recognition.FindAsync("game_hud_menu", frame, token);
            var hp = HpBarAnalyzer.Measure(frame);
            var icon = await TombstoneIconReader.ReadAsync(recognition, frame, token);
            var counter = await _restorationCounterReader.ReadClientFrameAsync(frame, token);
            var red = icon.HasRedSignal;
            var heading = RestorationCounterReader.HasRestorationHeading(counter.RawText);
            var current = ClassifyStartupRestoration(hud.Found || hp.Found, red, icon.Found,
                counter.State != RestorationCountState.Unknown, heading, icon.Confidence >= 0.90 && !icon.Found);
            var positionStable = current != StartupRestorationObservation.Present ||
                counter.State != RestorationCountState.Unknown || icon.AgreesWith(previousIcon);
            hits = current == previous && positionStable ? hits + 1 : 1;
            previousIcon = icon;
            previous = current;
            WritePersistentOnly(session, $"startup_restoration sample={sample + 1}; decision={current}; hud={hud.Found}/{hud.Confidence:F3}; hp={hp.Found}; icon={icon.Found}/{icon.Confidence:F3}; xy={icon.X},{icon.Y}; frame={frame.Width}x{frame.Height}; stable={positionStable}; red={red}; tab={counter.Tab}; count={counter.Count}; heading={heading}; ocr={counter.RawText}");
            if (hits >= 2 && current != StartupRestorationObservation.Unknown)
            {
                WritePersistentOnly(session, $"restoration_decision={current}; elapsedMs={elapsed.ElapsedMilliseconds}; samples={sample + 1}; noInput=true");
                return current;
            }
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
            ClassifyStartupRestoration(true, true, false, false, false) != StartupRestorationObservation.Absent ||
            ClassifyStartupRestoration(true, false, false, false, false, true) != StartupRestorationObservation.Unknown ||
            ClassifyStartupRestoration(false, false, false, false, false) != StartupRestorationObservation.Unknown ||
            ClassifyStartupRestoration(true, true, true, false, false) != StartupRestorationObservation.Present ||
            ClassifyStartupRestoration(false, false, false, true, true) != StartupRestorationObservation.Present ||
            ClassifyStartupRestoration(true, false, false, false, true) != StartupRestorationObservation.Unknown)
            throw new InvalidOperationException("Regressão: ausência, perda confirmada e captura desconhecida se confundem.");
    }
}
