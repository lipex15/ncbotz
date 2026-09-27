using BotNC.App.Models;

namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    internal enum ReconnectScreen { Unknown, LoginNotice, Touch, Promotion, ServerReady, Character, World }
    private sealed class ReconnectTransitionException : InvalidOperationException;

    internal static async Task<ReconnectScreen> ReadReconnectScreenAsync(VisualRecognitionService visual,
        PixelFrame frame, CancellationToken token)
    {
        frame = VisualRecognitionService.NormalizeForReferenceMatching(frame);
        if ((await visual.FindAsync("reconnect_character", frame, token)).Found &&
            (await visual.FindAsync("reconnect_start", frame, token)).Found) return ReconnectScreen.Character;
        if (HpBarAnalyzer.Measure(frame).Found) return ReconnectScreen.World;
        var touch = (await visual.FindAsync("reconnect_touch", frame, token)).Found;
        var server = (await visual.FindAsync("reconnect_server", frame, token)).Found;
        // The message can change (inactivity, timeout, network loss). Only acknowledge
        // the fixed OK button when the login screen is independently visible.
        if ((await visual.FindAsync("reconnect_login_ok", frame, token)).Found &&
            (touch || server || (await visual.FindAsync("reconnect_login_dim_touch", frame, token)).Found))
            return ReconnectScreen.LoginNotice;
        if (!touch && !server &&
            ((await visual.FindAsync("reconnect_skill_off", frame, token)).Found ||
             (await visual.FindAsync("reconnect_skill_on", frame, token)).Found))
            return ReconnectScreen.World;
        if (!touch && !server) return ReconnectScreen.Unknown;
        if ((await visual.FindAsync("reconnect_promo_close", frame, token)).Found) return ReconnectScreen.Promotion;
        return server ? ReconnectScreen.ServerReady : ReconnectScreen.Touch;
    }

    private async Task ObserveReconnectAsync(ClientSession session, PixelFrame frame, CancellationToken token)
    {
        if (session.ReconnectPending || DateTime.UtcNow < session.NextReconnectObservation) return;
        session.NextReconnectObservation = DateTime.UtcNow.AddSeconds(2);
        if (HpBarAnalyzer.Measure(frame).Found) { session.ReconnectHits = 0; return; }
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
                var screen = await ReadReconnectScreenAsync(recognition, frame, token);
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
                    var active = await recognition.FindAsync("reconnect_skill_on", frame, token);
                    var inactive = await recognition.FindAsync("reconnect_skill_off", frame, token);
                    WritePersistentOnly(session, $"reconnect_skill on={active.Found}/{active.Confidence:F3}; off={inactive.Found}/{inactive.Confidence:F3}; sent={session.ReconnectSkillSent}");
                    if (!active.Found && inactive.Found && !session.ReconnectSkillSent)
                    {
                        await input.PressKeyAsync(0x36, cancellationToken: token);
                        session.ReconnectSkillSent = true;
                        session.StartupSkillChecked = true;
                        session.ReconnectSkillAttempts = 0;
                        WriteLog(session, "Reconexão: skill 6 desligada; tecla 6 enviada uma vez.");
                        continue;
                    }
                    if (!active.Found && ++session.ReconnectSkillAttempts < 3) continue;
                    WriteLog(session, active.Found ? "Skill 6 ativa confirmada após login." :
                        "Skill 6 sem confirmação visual após login; não vou alternar a tecla às cegas. Confira a habilidade; retomando o fluxo.");
                    session.ReconnectPending = false;
                    if (active.Found) session.StartupSkillChecked = true;
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
                    session.NextReconnectAction = DateTime.UtcNow.AddSeconds(30);
                    session.ReconnectStepAttempts = 0;
                    WriteLog(session, $"Reconexão aguardando mudança em {screen}; sem repetir cliques durante 30s. Outro cliente continua ativo.");
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
        var frame = VisualRecognitionService.NormalizeForReferenceMatching(load("reconnect_world.png"));
        if (!(await visual.FindAsync("reconnect_skill_off", frame, CancellationToken.None)).Found ||
            (await visual.FindAsync("reconnect_skill_on", frame, CancellationToken.None)).Found)
            throw new InvalidOperationException("Skill 6 desligada não foi distinguida da borda ativa.");
        var activeIcon = load("reconnect_skill_on.png");
        var activePixels = (byte[])frame.Pixels.Clone();
        for (var y = 0; y < 78; y++)
            Buffer.BlockCopy(activeIcon.Pixels, (y + 20) * activeIcon.Stride + 16 * 4,
                activePixels, (938 + y) * frame.Stride + 1047 * 4, 76 * 4);
        if (!(await visual.FindAsync("reconnect_skill_on",
                new PixelFrame(frame.Width, frame.Height, frame.Stride, activePixels), CancellationToken.None)).Found)
            throw new InvalidOperationException("Borda ativa da skill 6 não reconhecida.");
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
