using BotNC.App.Models;

namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    internal enum ReconnectScreen { Unknown, LoginNotice, Touch, Promotion, ServerReady, Character, World }
    private sealed class ReconnectTransitionException : InvalidOperationException;

    internal static async Task<ReconnectScreen> ReadReconnectScreenAsync(VisualRecognitionService visual,
        PixelFrame frame, CancellationToken token, Action<string>? trace = null)
    {
        frame = VisualRecognitionService.NormalizeForReferenceMatching(frame);
        var character = await visual.FindAsync("reconnect_character", frame, token);
        var start = await visual.FindAsync("reconnect_start", frame, token);
        var hp = HpBarAnalyzer.Measure(frame);
        var menu = await visual.FindAsync("game_hud_menu", frame, token);
        var auto = await visual.FindAsync("hud_auto_label", frame, token);
        var controls = menu.Found || auto.Found;
        trace?.Invoke($"reconnect_evidence character={character.Found}/{character.Confidence:F3}; start={start.Found}/{start.Confidence:F3}; hp={hp.Found}/{hp.Percent:F3}; menu={menu.Found}/{menu.Confidence:F3}; auto={auto.Found}/{auto.Confidence:F3}");
        // The title identifies this screen independently of character appearance,
        // slot count and the localized/animated Start button. Click its known slot.
        if (character.Found) return ReconnectScreen.Character;
        if (!controls)
        {
            const int x = 25, y = 20, width = 600, height = 100;
            var pixels = new byte[width * height * 4];
            for (var row = 0; row < height; row++)
                Buffer.BlockCopy(frame.Pixels, (y + row) * frame.Stride + x * 4,
                    pixels, row * width * 4, width * 4);
            var text = (await new RestorationCounterReader().ReadHeaderCropAsync(
                new PixelFrame(width, height, width * 4, pixels), token)).RawText;
            trace?.Invoke($"reconnect_character_text={text}");
            if (text.Contains("SELECIONAR", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("PERSONAGEM", StringComparison.OrdinalIgnoreCase))
                return ReconnectScreen.Character;
        }
        // Login may return directly to death/rest, where HP is zero and the
        // ordinary world HUD is hidden. This still needs the restoration flow.
        if ((await visual.FindAsync("rest_unlock_instruction", frame, token)).Found ||
            ((await visual.FindAsync("morte_titulo", frame, token)).Found &&
             (await visual.FindAsync("morte_ressuscitar", frame, token)).Found))
            return ReconnectScreen.World;
        var touch = (await visual.FindAsync("reconnect_touch", frame, token)).Found;
        var server = (await visual.FindAsync("reconnect_server", frame, token)).Found;
        // The message can change (inactivity, timeout, network loss). Only acknowledge
        // the fixed OK button when the login screen is independently visible.
        if ((await visual.FindAsync("reconnect_login_ok", frame, token)).Found &&
            (touch || server || (await visual.FindAsync("reconnect_login_dim_touch", frame, token)).Found))
            return ReconnectScreen.LoginNotice;
        if (!touch && !server && controls && (hp.Found ||
            ((await visual.FindAsync("reconnect_skill_off", frame, token)).Found ||
             (await visual.FindAsync("reconnect_skill_on", frame, token)).Found)))
            return ReconnectScreen.World;
        if (!touch && !server) return ReconnectScreen.Unknown;
        if ((await visual.FindAsync("reconnect_promo_close", frame, token)).Found) return ReconnectScreen.Promotion;
        return server ? ReconnectScreen.ServerReady : ReconnectScreen.Touch;
    }

    private async Task ObserveReconnectAsync(ClientSession session, PixelFrame frame, CancellationToken token)
    {
        if (session.ReconnectPending || DateTime.UtcNow < session.NextReconnectObservation) return;
        session.NextReconnectObservation = DateTime.UtcNow.AddSeconds(2);
        if (HpBarAnalyzer.Measure(frame).Found &&
            ((await recognition.FindAsync("game_hud_menu", frame, token)).Found ||
             (await recognition.FindAsync("hud_auto_label", frame, token)).Found))
        { session.ReconnectHits = 0; return; }
        var screen = await ReadReconnectScreenAsync(recognition, frame, token);
        var disconnected = screen is ReconnectScreen.LoginNotice or ReconnectScreen.Touch or
            ReconnectScreen.Promotion or ReconnectScreen.ServerReady or ReconnectScreen.Character;
        session.ReconnectHits = disconnected && screen == session.LastReconnectScreen ? session.ReconnectHits + 1 : disconnected ? 1 : 0;
        session.LastReconnectScreen = screen;
        if (session.ReconnectHits < 2) return;
        session.ReconnectPending = true;
        session.ReconnectAfterLogin = false;
        session.ReconnectWorldInitialized = false;
        session.DisconnectedAt = DateTime.UtcNow;
        session.ReconnectSkillSent = false;
        session.ReconnectSkillAttempts = 0;
        session.ReconnectWorldHits = 0;
        session.ReconnectStepAttempts = 0;
        session.LastReconnectActionScreen = ReconnectScreen.Unknown;
        session.NextReconnectAction = DateTime.UtcNow;
        session.ScheduleHuntConfirmed = false;
        session.SchedulePreviousObservationActive = false;
        session.Audio.Armed = false;
        _ = session.Audio.TryConsumeAlert(out _);
        Interlocked.Exchange(ref session.PendingVisualLowHp, 0);
        Interlocked.Exchange(ref session.PendingVisualDeath, 0);
        WriteLog(session, $"Desconexão confirmada ({screen}); reconexão deste cliente iniciada. O outro cliente continua ativo.");
    }

    private async Task ServiceReconnectsAsync(IReadOnlyList<ClientSession> sessions, SapherasOptions sapheras,
        AntiOverkillOptions antiOverkill, PauseController pause, CancellationToken token)
    {
        foreach (var session in sessions.Where(s => s.ReconnectPending && DateTime.UtcNow >= s.NextReconnectAction))
        {
            if (HumanOwnsInterface || HasPendingProtection) return;
            session.NextReconnectAction = DateTime.UtcNow.AddSeconds(3);
            session.HandlingReconnect = true;
            try
            {
                BindWorkflowClient(session);
                var frame = await CaptureClientFrameAsync(session, token);
                var screen = await ReadReconnectScreenAsync(recognition, frame, token,
                    evidence => WritePersistentOnly(session, evidence));
                if (screen == ReconnectScreen.Unknown && session.ReconnectWorldInitialized &&
                    RestorationCounterReader.HasRestorationHeading((await _restorationCounterReader.ReadClientFrameAsync(frame, token)).RawText))
                    screen = ReconnectScreen.World;
                WritePersistentOnly(session, $"reconnect_step screen={screen}; attempts={session.ReconnectStepAttempts}; restored={session.ReconnectAfterLogin}; skillSent={session.ReconnectSkillSent}; clientScoped=true");
                if (screen == ReconnectScreen.World)
                {
                    if (++session.ReconnectWorldHits < 2) continue;
                    if (!session.ReconnectAfterLogin)
                    {
                        if (!session.ReconnectWorldInitialized)
                        {
                            session.ReconnectWorldInitialized = true;
                            session.IsFarmingTa = false;
                            session.SafeInRest = false;
                            session.AwaitingHuntActivationAtSpot = false;
                            session.AbbeyInside = false;
                            session.AnonymousDungeonInside = false;
                            session.InDailyCampaign = false;
                            session.LoveBossInside = false;
                            session.RestorationResourcesCleared = false;
                            session.StartupRestorationChecked = false;
                        }
                        // Reuse the actual loss workflow; login is not proof of death.
                        if (await RunStartupRestorationSafelyAsync(session, sapheras, antiOverkill, pause, token))
                            continue;
                        session.ReconnectAfterLogin = true;
                    }
                    await ExitRestIfNeededAsync(session, pause, token);
                    frame = await CaptureClientFrameAsync(session, token);
                    var skill = await SkillSixReader.ReadAsync(recognition, frame, token, evidence => WritePersistentOnly(session, evidence));
                    if (skill == SkillSixState.Inactive && !session.ReconnectSkillSent)
                    {
                        await Task.Delay(250, token);
                        if (await SkillSixReader.ReadAsync(recognition, await CaptureClientFrameAsync(session, token), token) != SkillSixState.Inactive)
                            continue;
                        await input.PressKeyAsync(0x36, cancellationToken: token);
                        session.ReconnectSkillSent = true;
                        session.StartupSkillChecked = true;
                        session.ReconnectSkillAttempts = 0;
                        WriteLog(session, "Reconexão: skill 6 desligada; tecla 6 enviada uma vez.");
                        continue;
                    }
                    if (skill != SkillSixState.Active && ++session.ReconnectSkillAttempts < 3) continue;
                    WriteLog(session, skill == SkillSixState.Active ? "Skill 6 ativa confirmada após login." :
                        "Skill 6 sem confirmação visual após login; não vou alternar a tecla às cegas. Confira a habilidade; retomando o fluxo.");
                    session.ReconnectPending = false;
                    if (skill == SkillSixState.Active) session.StartupSkillChecked = true;
                    if (session.InAgenda) session.AgendaUntil += DateTime.UtcNow - session.DisconnectedAt;
                    session.ReconnectHits = 0;
                    session.ConsecutiveRecoveryFailures = 0;
                    session.RequiresHardFlowReset = false;
                    session.NextRecoveryAttemptAt = DateTime.UtcNow;
                    session.Audio.Armed = true;
                    session.VisualEmergencyIssued = false;
                    WriteLog(session, "Login concluído; retomando o fluxo configurado com o tempo restante preservado.");
                    continue;
                }
                session.ReconnectWorldHits = 0;
                if (screen == ReconnectScreen.Unknown) continue; // loading / network wait, no blind input
                // A second disconnect during restoration starts a new login, including
                // the one-shot skill activation. Loading alone does not reset it.
                if (session.ReconnectWorldInitialized)
                {
                    session.ReconnectWorldInitialized = false;
                    session.ReconnectAfterLogin = false;
                    session.ReconnectSkillSent = false;
                    session.ReconnectSkillAttempts = 0;
                }
                if (session.LastReconnectActionScreen == screen) session.ReconnectStepAttempts++;
                else { session.LastReconnectActionScreen = screen; session.ReconnectStepAttempts = 1; }
                if (session.ReconnectStepAttempts > 3)
                {
                    session.NextReconnectAction = DateTime.UtcNow.AddSeconds(3);
                    session.ReconnectStepAttempts = 0;
                    WriteLog(session, $"Reconexão sem avanço em {screen}; reavaliando a tela em 3s para repetir apenas a ação correspondente. Outro cliente continua ativo.");
                    continue;
                }
                switch (screen)
                {
                    case ReconnectScreen.LoginNotice:
                        await input.PressKeyAsync(KeyY, cancellationToken: token);
                        break;
                    case ReconnectScreen.Promotion:
                        await ClickReconnectPointAsync(session, 1533, 802, token);
                        break;
                    case ReconnectScreen.Touch:
                    case ReconnectScreen.ServerReady:
                        await ClickReconnectPointAsync(session, 960, 540, token);
                        break;
                    case ReconnectScreen.Character:
                        await ClickReconnectPointAsync(session, 1813, 1012, token);
                        session.NextReconnectAction = DateTime.UtcNow.AddSeconds(10);
                        break;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                session.NextReconnectAction = DateTime.UtcNow.AddSeconds(5);
                WritePersistentOnly(session, $"reconnect_retry error={error.GetType().Name}; detail={error.Message}; otherClientUnaffected=true");
            }
            finally { session.HandlingReconnect = false; }
        }
    }

    private async Task ClickReconnectPointAsync(ClientSession session, int x, int y, CancellationToken token)
    {
        var point = gameWindows.MapReferencePoint(session.Options.Target, x, y);
        await input.MoveAndClickAsync(point.X, point.Y, TimeSpan.FromMilliseconds(250), token);
    }

    internal static async Task VerifyReconnectFixturesAsync(VisualRecognitionService visual,
        Func<string, PixelFrame> load)
    {
        foreach (var (file, expected) in new[] {
            ("reconnect_inactivity.png", ReconnectScreen.LoginNotice),
            ("reconnect_touch.png", ReconnectScreen.Touch),
            ("reconnect_promo.png", ReconnectScreen.Promotion),
            ("reconnect_promo_second.png", ReconnectScreen.Promotion),
            ("reconnect_character.png", ReconnectScreen.Character),
            ("reconnect_world.png", ReconnectScreen.World) })
        {
            var actual = await ReadReconnectScreenAsync(visual, load(file), CancellationToken.None);
            if (actual != expected) throw new InvalidOperationException($"Reconnect {file}: {actual}, esperado {expected}.");
        }
        if (await ReadReconnectScreenAsync(visual, load("regression_death_rest_20260927.png"), CancellationToken.None) != ReconnectScreen.World)
            throw new InvalidOperationException("Login em descanso com morte deve encaminhar à restauração, não aguardar HUD vivo.");
        var frame = VisualRecognitionService.NormalizeForReferenceMatching(load("reconnect_world.png"));
        // Reproduce the failure mode from the friend's log: apparent HP on a
        // character screen must never divert login into restoration.
        var selection = VisualRecognitionService.NormalizeForReferenceMatching(load("reconnect_character.png"));
        var selectionPixels = (byte[])selection.Pixels.Clone();
        for (var y = 945; y < 1040; y++)
            Array.Clear(selectionPixels, y * selection.Stride + 1640 * 4, 280 * 4);
        static void PaintFalseHp(byte[] pixels, int stride, int height)
        {
            for (var y = height - 53; y <= height - 47; y++)
            for (var x = 100; x < 161; x++)
            {
                var offset = y * stride + x * 4;
                pixels[offset] = 0; pixels[offset + 1] = 0;
                pixels[offset + 2] = 200; pixels[offset + 3] = 255;
            }
        }
        PaintFalseHp(selectionPixels, selection.Stride, selection.Height);
        var selectionWithFalseHp = new PixelFrame(selection.Width, selection.Height, selection.Stride, selectionPixels);
        if (!HpBarAnalyzer.Measure(selectionWithFalseHp).Found ||
            await ReadReconnectScreenAsync(visual, selectionWithFalseHp, CancellationToken.None) != ReconnectScreen.Character)
            throw new InvalidOperationException("Seleção com HP falso e botão sem correspondência deve continuar em Iniciar.");
        var hpOnlyPixels = new byte[frame.Pixels.Length];
        PaintFalseHp(hpOnlyPixels, frame.Stride, frame.Height);
        if (await ReadReconnectScreenAsync(visual,
                new PixelFrame(frame.Width, frame.Height, frame.Stride, hpOnlyPixels), CancellationToken.None) != ReconnectScreen.Unknown)
            throw new InvalidOperationException("HP isolado não pode confirmar login concluído.");
        if (!(await visual.FindAsync("reconnect_skill_off", frame, CancellationToken.None)).Found ||
            (await visual.FindAsync("reconnect_skill_on", frame, CancellationToken.None)).Found)
            throw new InvalidOperationException("Skill 6 desligada não foi distinguida da borda ativa.");
        var activeIcon = load("reconnect_skill_on.png");
        if (await SkillSixReader.ReadAsync(visual, frame, CancellationToken.None) != SkillSixState.Inactive)
            throw new InvalidOperationException("Skill 6 desligada: leitura da borda não confirmou Inactive.");
        var activePixels = (byte[])frame.Pixels.Clone();
        for (var y = 0; y < 78; y++)
            Buffer.BlockCopy(activeIcon.Pixels, (y + 20) * activeIcon.Stride + 16 * 4,
                activePixels, (938 + y) * frame.Stride + 1047 * 4, 76 * 4);
        if (!(await visual.FindAsync("reconnect_skill_on",
                new PixelFrame(frame.Width, frame.Height, frame.Stride, activePixels), CancellationToken.None)).Found)
            throw new InvalidOperationException("Borda ativa da skill 6 não reconhecida.");
        if (await SkillSixReader.ReadAsync(visual, new PixelFrame(frame.Width, frame.Height, frame.Stride, activePixels), CancellationToken.None) != SkillSixState.Active)
            throw new InvalidOperationException("Skill 6 ativa não pode receber outra tecla 6.");
        // The dialog wording must be irrelevant; OK alone must never authorize
        // reconnect on a gameplay screen. Modify only in-memory test frames.
        var notice = load("reconnect_inactivity.png");
        var changedPixels = (byte[])notice.Pixels.Clone();
        for (var y = 450; y < 565; y++)
            Array.Clear(changedPixels, y * notice.Stride + 750 * 4, 440 * 4);
        if (await ReadReconnectScreenAsync(visual,
                new PixelFrame(notice.Width, notice.Height, notice.Stride, changedPixels), CancellationToken.None) != ReconnectScreen.LoginNotice)
            throw new InvalidOperationException("Reconexão depende do texto do aviso.");
        var worldPixels = (byte[])frame.Pixels.Clone();
        for (var y = 611; y < 661; y++)
            Buffer.BlockCopy(notice.Pixels, y * notice.Stride + 864 * 4,
                worldPixels, y * frame.Stride + 864 * 4, 192 * 4);
        if (await ReadReconnectScreenAsync(visual,
                new PixelFrame(frame.Width, frame.Height, frame.Stride, worldPixels), CancellationToken.None) == ReconnectScreen.LoginNotice)
            throw new InvalidOperationException("OK fora do login foi tratado como desconexão.");
        var reading = await new RestorationCounterReader().ReadClientFrameAsync(
            load("restoration_equipment_pending_regression.png"));
        if (reading.Tab != RestorationTab.Equipment || reading.Count != 1)
            throw new InvalidOperationException($"Equipamento pendente da log não reconhecido: {reading.RawText}");
        var first = new ClientSession(new AutomationClientOptions("Cliente 1", new GameWindowTarget(0, "Teste 1", 1, false, true), TaDestination.Ta2, false, 1, null));
        var second = new ClientSession(first.Options with { Label = "Cliente 2", Priority = 2 });
        first.ReconnectPending = true;
        var blocked = false;
        try { ThrowIfDeathPending(first); } catch (ReconnectTransitionException) { blocked = true; }
        if (!blocked) throw new InvalidOperationException("Reconexão não interrompeu comandos do fluxo anterior.");
        ThrowIfDeathPending(second);
        first.HandlingReconnect = true;
        ThrowIfDeathPending(first);
    }
}
