namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private sealed class RecoveryObservationPendingException(string reason) : InvalidOperationException(reason);

    private static bool IsSapherasSessionActive(ClientSession session) =>
        session.Options.UseSapheras && !session.SapherasExitedEarly && !session.GlobalInside && !GlobalHasPriority(session);

    internal static bool IsConfirmedCityExit(bool fixedPoint, bool liveHp, string heading) =>
        fixedPoint && liveHp && heading.Contains("CASTELO DE ABILIUS", StringComparison.Ordinal);

    private async Task<bool> ObserveSapherasExitAsync(ClientSession session, CancellationToken token)
    {
        if (DateTime.UtcNow - session.LastSapherasExitCheck < TimeSpan.FromSeconds(5)) return false;
        session.LastSapherasExitCheck = DateTime.UtcNow;
        var frame = await CaptureClientFrameAsync(session, token);
        var fixedPoint = await recognition.FindAsync("descanso_ponto_fixo", frame, token);
        if (!fixedPoint.Found) { session.SapherasExitHits = 0; return false; }
        var heading = await _restorationCounterReader.ReadClientFrameAsync(frame, token);
        var city = IsConfirmedCityExit(fixedPoint.Found, HpBarAnalyzer.Measure(frame).Found, heading.RawText);
        session.SapherasExitHits = city ? session.SapherasExitHits + 1 : 0;
        WritePersistentOnly(session, $"sapheras_exit city={city}; stableSamples={session.SapherasExitHits}; heading={heading.RawText}; noInput=true");
        // Positive evidence only. A hidden Auto or an unrecognized location never ends the session.
        return session.SapherasExitHits >= 3;
    }

    private void BeginResidualHpRecovery(ClientSession session, string reason)
    {
        session.ResidualHp.Begin(DateTime.UtcNow);
        _ = session.Audio.TryConsumeAlert(out _);
        WriteLog(session, $"{reason}: seguindo o fluxo sem esperar HP; pulsação residual não provoca novo TP.");
    }

    // This state only filters duplicate protection events. It is never awaited
    // by travel, restoration, hunt activation, or the other client's workflow.
    private sealed class ResidualHpGuard
    {
        private readonly object gate = new();
        private bool active;
        private DateTime started, lastSample;
        private double peak;
        private int recoveredHits, fallingHits;
        public bool IsActive { get { lock (gate) return active; } }
        public void Begin(DateTime now)
        {
            lock (gate)
            {
                active = true;
                started = now;
                lastSample = default;
                peak = 0;
                recoveredHits = fallingHits = 0;
            }
        }
        public bool Observe(bool found, double percent, DateTime now)
        {
            lock (gate)
            {
                if (!active || now - lastSample < TimeSpan.FromMilliseconds(500)) return false;
                lastSample = now;
                if (!found || percent < 0 || percent > 1)
                {
                    recoveredHits = fallingHits = 0;
                    return false;
                }
                // Ignore transient pre-teleport frames; no delay is imposed on gameplay.
                if (now - started < TimeSpan.FromSeconds(5))
                {
                    peak = percent;
                    return false;
                }
                peak = Math.Max(peak, percent);
                recoveredHits = percent >= .60 ? recoveredHits + 1 : 0;
                fallingHits = peak - percent >= .12 ? fallingHits + 1 : 0;
                if (recoveredHits < 2 && fallingHits < 2) return false;
                active = false;
                return true;
            }
        }
    }

    internal static void VerifyRecoveryObservationPolicy()
    {
        if (!IsConfirmedCityExit(true, true, "CASTELO DE ABILIUS") ||
            IsConfirmedCityExit(false, true, "CASTELO DE ABILIUS") ||
            IsConfirmedCityExit(true, false, "CASTELO DE ABILIUS") ||
            IsConfirmedCityExit(true, true, "ATALAIA ERODIDA"))
            throw new InvalidOperationException("Saída de Sapheras exige cidade reconhecida, HP e ponto fixo.");
        var now = DateTime.UtcNow;
        var guard = new ResidualHpGuard();
        guard.Begin(now);
        for (var i = 1; i <= 120; i++)
            if (guard.Observe(true, .25, now.AddSeconds(i)))
                throw new InvalidOperationException("HP baixo residual não pode gerar nova emergência por tempo decorrido.");
        if (!guard.IsActive || guard.Observe(false, 1, now.AddSeconds(121)))
            throw new InvalidOperationException("Leitura ausente não prova recuperação.");
        if (guard.Observe(true, .65, now.AddSeconds(122)) ||
            !guard.Observe(true, .65, now.AddSeconds(123)) || guard.IsActive)
            throw new InvalidOperationException("HP recuperado deve rearmar proteção em duas amostras.");
        guard.Begin(now);
        guard.Observe(true, .40, now.AddSeconds(6));
        if (guard.Observe(true, .20, now.AddSeconds(7)) ||
            !guard.Observe(true, .20, now.AddSeconds(8)))
            throw new InvalidOperationException("Nova queda relevante de HP deve rearmar proteção sem esperar cura.");
        var other = new ResidualHpGuard();
        if (other.IsActive) throw new InvalidOperationException("Recuperação residual não deve afetar outro cliente.");
    }
}
