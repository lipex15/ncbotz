using BotNC.App.Models;

namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private static void ThrowIfDeathPending(ClientSession session)
    {
        if (!session.HandlingDeath && Volatile.Read(ref session.PendingVisualDeath) != 0)
            throw new InvalidOperationException($"{session.Options.Label}: morte confirmada; comando normal bloqueado para priorizar Ressuscitar.");
    }

    private static bool TryClaimEmergency(ClientSession session)
    {
        var now = DateTime.UtcNow.Ticks;
        var previous = Interlocked.Read(ref session.EmergencyClaimUntilTicks);
        return previous <= now && Interlocked.CompareExchange(ref session.EmergencyClaimUntilTicks,
            now + TimeSpan.FromSeconds(30).Ticks, previous) == previous;
    }

    private async Task<bool> ReadAnonymousLocationAsync(ClientSession session, CancellationToken token)
    {
        var frame = VisualRecognitionService.NormalizeForReferenceMatching(await CaptureClientFrameAsync(session, token));
        return await ReadAnonymousFrameAsync(recognition, frame, token,
            evidence => WritePersistentOnly(session, evidence));
    }

    internal static async Task<bool> ReadAnonymousFrameAsync(VisualRecognitionService recognition,
        PixelFrame frame, CancellationToken token, Action<string>? log = null)
    {
        if ((await recognition.FindAsync("anonymous_map", frame, token)).Found) return true;
        var reference = await recognition.FindAsync("anonymous_arrival", frame, token);
        if (reference.Found) return true;
        var text = (await new RestorationCounterReader().ReadClientFrameAsync(frame, token)).RawText;
        var time = await new AbbeyTimeReader().ReadAsync(frame, token);
        var hud = await recognition.FindAsync("game_hud_menu", frame, token);
        var found = text.Contains("POSTO", StringComparison.Ordinal) && text.Contains("ILUSAO", StringComparison.Ordinal) &&
            time.Remaining is not null && hud.Found;
        log?.Invoke($"anonymous_location reference={reference.Confidence:F3}; hud={hud.Found}; time={time.Remaining}; text={text}; confirmed={found}");
        return found;
    }

    internal static void VerifyDeathPriorityPolicy()
    {
        var first = new ClientSession(new AutomationClientOptions("Cliente 1", new GameWindowTarget(0, "Teste 1", 1, false, true), TaDestination.Ta2, false, 1, null));
        var second = new ClientSession(first.Options with { Label = "Cliente 2", Priority = 2 });
        first.PendingVisualDeath = 1;
        var blocked = false;
        try { ThrowIfDeathPending(first); } catch (InvalidOperationException) { blocked = true; }
        if (!blocked) throw new InvalidOperationException("Morte não bloqueou o fluxo normal.");
        ThrowIfDeathPending(second);
        first.HandlingDeath = true;
        ThrowIfDeathPending(first);
        var claims = 0;
        Parallel.For(0, 32, _ => { if (TryClaimEmergency(first)) Interlocked.Increment(ref claims); });
        if (claims != 1 || !TryClaimEmergency(second))
            throw new InvalidOperationException("Teleporte duplicado ou bloqueio cruzado entre clientes.");
    }

    private async Task WaitForAnonymousArrivalAsync(ClientSession session, PauseController pause, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(70);
        var hits = 0;
        while (DateTime.UtcNow < deadline)
        {
            await CheckpointAsync(pause, token);
            await AbortWorkflowIfDeathDetectedAsync(session, "durante chegada à Anônima", token);
            hits = await ReadAnonymousLocationAsync(session, token) ? hits + 1 : 0;
            if (hits >= 2) return;
            await Task.Delay(250, token);
        }
        var diagnostic = await recognition.SaveDiagnosticAsync("anonymous_arrival",
            await CaptureClientFrameAsync(session, token));
        throw new TimeoutException($"Chegada à Anônima não confirmada por imagem ou localização/tempo/HUD; sem repetir pagamento. Diagnóstico: {diagnostic}");
    }
}
