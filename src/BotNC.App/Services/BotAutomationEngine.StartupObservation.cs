namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    internal enum StartupRestorationObservation { Unknown, Absent, Present }

    internal static StartupRestorationObservation ClassifyStartupRestoration(
        bool hud, bool redIcon, bool iconTemplate, bool readableCounter, bool panelHeading,
        bool strongUnconfirmedShape = false, bool restorationPending = false, bool completeHud = true,
        bool restVisible = false)
    {
        if (restVisible) return StartupRestorationObservation.Unknown;
        if (readableCounter || redIcon && iconTemplate) return StartupRestorationObservation.Present;
        if (restorationPending && redIcon) return StartupRestorationObservation.Unknown;
        // Red decoration alone is not a pending restoration and must not start a hunt.
        if (hud && completeHud && !(redIcon && iconTemplate) && !panelHeading && !strongUnconfirmedShape)
            return StartupRestorationObservation.Absent;
        return StartupRestorationObservation.Unknown;
    }

    private async Task<StartupRestorationObservation> ReadStartupRestorationAsync(
        ClientSession session, PauseController pause, CancellationToken token,
        Action<TombstoneIconReading>? onConfirmedIcon = null)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var previous = StartupRestorationObservation.Unknown;
        var hits = 0;
        TombstoneIconReading? previousIcon = null;
        var loadingDeadline = DateTime.UtcNow.AddSeconds(60);
        // Clear evidence still returns on frame two. Only incomplete post-respawn
        // frames get a short local grace period, instead of restarting recovery.
        for (var sample = 0; sample < (session.NeedsDeathRestoration ? 6 : 3); sample++)
        {
            await CheckpointAsync(pause, token);
            // Rest may reappear after the respawn loading frame. Its HP bar does
            // not prove that the (hidden) tombstone is absent.
            if (await FindRestStateAsync(session, token) is not null)
            {
                WritePersistentOnly(session, "restoration_hidden_by_rest; decision=uncover; absenceForbidden=true");
                await ExitRestIfNeededAsync(session, pause, token);
            }
            await DismissWemadeOfferIfPresentAsync(session, token);
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
            if ((await recognition.FindAsync("rest_unlock_instruction", frame, token)).Found ||
                (await recognition.FindAsync("tela_descanso", frame, token)).Found)
            {
                previous = StartupRestorationObservation.Unknown;
                hits = 0;
                continue;
            }
            var hp = HpBarAnalyzer.Measure(frame);
            // The health bar also exists behind rest. Accept a second, independent
            // open-world control; do not depend on one decorative menu template.
            var openControls = hud.Found ||
                (await recognition.FindAsync("hud_auto_label", frame, token)).Found ||
                (await recognition.FindAsync("reconnect_skill_off", frame, token)).Found ||
                (await recognition.FindAsync("reconnect_skill_on", frame, token)).Found;
            var icon = await TombstoneIconReader.ReadAsync(recognition, frame, token, session.NeedsDeathRestoration);
            var counter = await _restorationCounterReader.ReadClientFrameAsync(frame, token);
            // Some PCs render the menu differently. A readable town NPC shortcut
            // with distance is independent evidence of the uncovered world HUD.
            // Never use this startup-only fallback to clear a known death loss.
            if (!openControls && HasStartupTownHudEvidence(session.NeedsDeathRestoration,
                    hp.Found, hud.Confidence, counter.RawText))
            {
                openControls = true;
                WritePersistentOnly(session, "restoration_world_fallback=npc_shortcut_and_menu_and_hp; pendingDeath=false");
            }
            if (session.NeedsDeathRestoration && hp.Found && icon.AreaInspection)
            {
                onConfirmedIcon?.Invoke(icon);
                WritePersistentOnly(session, $"restoration_decision=Present; reason=fixed_area_inspection; templateConfidence={icon.Confidence:F3}; panelMustConfirm=true; xy={icon.X},{icon.Y}");
                return StartupRestorationObservation.Present;
            }
            var red = icon.HasRedSignal;
            var heading = RestorationCounterReader.HasRestorationHeading(counter.RawText);
            var current = ClassifyStartupRestoration(hud.Found || hp.Found, red, icon.Found,
                counter.State != RestorationCountState.Unknown, heading,
                icon.Confidence >= (session.NeedsDeathRestoration ? 0.78 : 0.90) && !icon.Found,
                session.NeedsDeathRestoration,
                completeHud: HasSufficientRestorationHud(openControls, hp.Found, session.NeedsDeathRestoration));
            WritePersistentOnly(session, $"restoration_world_evidence openControls={openControls}; hp={hp.Found}/{hp.Percent:F1}; absenceRequiresOpenControls=true");
            WritePersistentOnly(session, $"restoration_context pending={session.NeedsDeathRestoration}; red={red}; icon={icon.Found}; confidence={icon.Confidence:F3}; contextualInspection={session.NeedsDeathRestoration && icon.Found && icon.Confidence < 0.78}; panelVerificationRequired=true");
            var positionStable = current != StartupRestorationObservation.Present ||
                counter.State != RestorationCountState.Unknown || icon.AgreesWith(previousIcon);
            hits = current == previous && positionStable ? hits + 1 : 1;
            previousIcon = icon;
            previous = current;
            WritePersistentOnly(session, $"startup_restoration sample={sample + 1}; decision={current}; hud={hud.Found}/{hud.Confidence:F3}; hp={hp.Found}; icon={icon.Found}/{icon.Confidence:F3}; xy={icon.X},{icon.Y}; frame={frame.Width}x{frame.Height}; stable={positionStable}; red={red}; tab={counter.Tab}; count={counter.Count}; heading={heading}; ocr={counter.RawText}");
            if (hits >= 2 && current != StartupRestorationObservation.Unknown)
            {
                if (current == StartupRestorationObservation.Present && positionStable && icon.Found &&
                    counter.State == RestorationCountState.Unknown)
                    onConfirmedIcon?.Invoke(icon);
                WritePersistentOnly(session, $"restoration_decision={current}; elapsedMs={elapsed.ElapsedMilliseconds}; samples={sample + 1}; noInput=true");
                return current;
            }
            if (session.NeedsDeathRestoration && current == StartupRestorationObservation.Unknown && icon.Found)
            {
                onConfirmedIcon?.Invoke(icon);
                WritePersistentOnly(session, "restoration_decision=Present; reason=confirmed_death_fixed_area; noInput=true");
                return StartupRestorationObservation.Present;
            }
            await Task.Delay(250, token);
        }
        if (DateTime.UtcNow - session.LastRestorationDiagnostic >= TimeSpan.FromMinutes(1))
        {
            session.LastRestorationDiagnostic = DateTime.UtcNow;
            var diagnostic = await recognition.SaveDiagnosticAsync($"startup_observation_{session.Options.Priority}",
                await CaptureClientFrameAsync(session, token));
            WritePersistentOnly(session, $"startup_restoration inconclusive; diagnostic={diagnostic}; noInput=true");
        }
        else WritePersistentOnly(session, "startup_restoration inconclusive; diagnostic=unchanged_state_throttled; noInput=true");
        return StartupRestorationObservation.Unknown;
    }

    internal static bool HasSufficientRestorationHud(bool menu, bool hp, bool pending) =>
        menu && hp;

    internal static bool HasStartupTownHudEvidence(bool pending, bool hp, double menuConfidence, string text) =>
        !pending && hp && menuConfidence >= 0.65 &&
        System.Text.RegularExpressions.Regex.IsMatch(text, @"\b[A-Z,]*RTIGOS\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
        System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\d{1,3}\s*M\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    internal static void VerifyStartupObservationPolicy()
    {
        const string town = ",RTIGOS NEW 15M | ,RTIGOS";
        if (!HasStartupTownHudEvidence(false, true, 0.681, town) ||
            HasStartupTownHudEvidence(true, true, 0.681, town) ||
            HasStartupTownHudEvidence(false, false, 0.681, town) ||
            HasStartupTownHudEvidence(false, true, 0.4, town) ||
            HasStartupTownHudEvidence(false, true, 0.681, "ARTIGOS") ||
            HasStartupTownHudEvidence(false, true, 0.681, "15M"))
            throw new InvalidOperationException("Town HUD fallback requires independent startup evidence and cannot clear death recovery.");
        if (ClassifyStartupRestoration(true, false, false, false, false,
                restorationPending: true, restVisible: true) != StartupRestorationObservation.Unknown)
            throw new InvalidOperationException("HP no descanso não prova ausência de lápide.");
        if (HasSufficientRestorationHud(false, true, false) ||
            HasSufficientRestorationHud(false, true, true) ||
            !HasSufficientRestorationHud(true, true, true) ||
            HasSufficientRestorationHud(false, false, false) ||
            HasSufficientRestorationHud(true, false, true))
            throw new InvalidOperationException("HP isolado não prova HUD aberto; menu ou Auto mais HP devem permitir progresso.");
        if (ClassifyStartupRestoration(true, false, false, false, false,
                restorationPending: true, completeHud: HasSufficientRestorationHud(false, true, true)) != StartupRestorationObservation.Unknown)
            throw new InvalidOperationException("Regressão do diagnóstico: HP com controles ocultos não pode zerar perdas.");
        if (ClassifyStartupRestoration(true, false, false, false, false,
                restorationPending: true, completeHud: false) != StartupRestorationObservation.Unknown ||
            ClassifyStartupRestoration(true, true, true, false, false,
                restorationPending: true, completeHud: false) != StartupRestorationObservation.Present)
            throw new InvalidOperationException("HUD incompleto não prova ausência; lápide clara não deve aguardar o HUD.");
        if (ClassifyStartupRestoration(true, true, false, false, false, false, true) != StartupRestorationObservation.Unknown ||
            !TombstoneIconReader.MayInspectAfterDeath(true, true, 0.617, 1535, 63) ||
            TombstoneIconReader.MayInspectAfterDeath(false, true, 0.625, 1535, 63) ||
            TombstoneIconReader.MayInspectAfterDeath(true, false, 0.625, 1535, 63) ||
            TombstoneIconReader.MayInspectAfterDeath(true, true, 0.59, 1535, 63) ||
            TombstoneIconReader.MayInspectAfterDeath(true, true, 0.625, 1700, 63))
            throw new InvalidOperationException("Perda após morte foi ignorada ou inspeção liberada sem contexto.");
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
