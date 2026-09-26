namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private sealed class RecoveryObservationPendingException(string reason) : InvalidOperationException(reason);

    internal static bool IsRecoveredHp(bool found, double percent) => found && percent >= 0.70 && percent <= 1;

    private static bool IsSapherasSessionActive(ClientSession session) =>
        session.Options.UseSapheras && !session.SapherasExitedEarly;

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

    private async Task EnsureRecoveredHpAsync(ClientSession session, CancellationToken token)
    {
        if (!session.AwaitingHpRecovery) return;
        // Short, passive samples: leave the action queue available to the other client.
        if (DateTime.UtcNow - session.LastHpRecoverySample < TimeSpan.FromSeconds(2))
            throw new RecoveryObservationPendingException("aguardando próxima amostra de HP");
        session.LastHpRecoverySample = DateTime.UtcNow;
        var frame = await CaptureClientFrameAsync(session, token);
        var hp = HpBarAnalyzer.Measure(frame);
        session.HpRecoveryHits = IsRecoveredHp(hp.Found, hp.Percent) ? session.HpRecoveryHits + 1 : 0;
        if (session.HpRecoveryHits >= 2)
        {
            session.AwaitingHpRecovery = false;
            session.HpRecoveryHits = 0;
            WriteLog(session, $"HP recuperado e estável ({hp.Percent:P0}); retorno ao farm liberado.");
            return;
        }
        if (DateTime.UtcNow - session.LastHpRecoveryLog >= TimeSpan.FromSeconds(30))
        {
            session.LastHpRecoveryLog = DateTime.UtcNow;
            WriteLog(session, hp.Found
                ? $"Aguardando recuperação de HP ({hp.Percent:P0}) antes de retornar; proteção continua ativa."
                : "HP encoberto ou não reconhecido; aguardando leitura em segundo plano antes de retornar.");
        }
        WritePersistentOnly(session, $"hp_recovery found={hp.Found}; percent={hp.Percent:F3}; stableSamples={session.HpRecoveryHits}; required=2; threshold=0.70; input=false");
        throw new RecoveryObservationPendingException("HP ainda sem recuperação estável");
    }

    internal static void VerifyRecoveryObservationPolicy()
    {
        if (!IsConfirmedCityExit(true, true, "CASTELO DE ABILIUS") ||
            IsConfirmedCityExit(false, true, "CASTELO DE ABILIUS") ||
            IsConfirmedCityExit(true, false, "CASTELO DE ABILIUS") ||
            IsConfirmedCityExit(true, true, "ATALAIA ERODIDA"))
            throw new InvalidOperationException("Saída de Sapheras exige cidade reconhecida, HP e ponto fixo.");
        if (IsRecoveredHp(false, 1) || IsRecoveredHp(true, .35) || IsRecoveredHp(true, .69) ||
            !IsRecoveredHp(true, .70) || !IsRecoveredHp(true, 1) || IsRecoveredHp(true, 1.1))
            throw new InvalidOperationException("Política de recuperação de HP inválida.");
    }
}
