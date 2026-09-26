namespace BotNC.App.Services;

public sealed partial class BotAutomationEngine
{
    private sealed class HuntActivationUncertainException(string message) : InvalidOperationException(message);

    internal static bool ShouldResetRouteAfterFailure(int failures, bool arrived, bool huntUncertain) =>
        failures >= 2 && !(arrived && huntUncertain);

    internal static bool MayEnableHuntFromIdleRest(bool arrived, bool idleConfirmed,
        bool restorationPending, bool liveHp, OpenHudHuntState auto) =>
        arrived && idleConfirmed && !restorationPending && liveHp && auto == OpenHudHuntState.Unknown;

    internal static bool IsIdleFarmRest(bool waiting, bool fixedPoint, bool hunting, bool moving) =>
        waiting && !fixedPoint && !hunting && !moving;

    private async Task<bool> ConfirmIdleFarmSpotAsync(ClientSession session,
        PauseController pause, CancellationToken token)
    {
        for (var sample = 0; sample < 2; sample++)
        {
            await CheckpointAsync(pause, token);
            var frame = await CaptureClientFrameAsync(session, token);
            var waiting = await recognition.FindAsync("descanso_aguardando_spot", frame, token);
            var fixedPoint = await recognition.FindAsync("descanso_ponto_fixo", frame, token);
            var hunting = await recognition.FindAsync("caca_automatica", frame, token);
            var moving = await recognition.FindAsync("descanso_movendo", frame, token);
            WritePersistentOnly(session, $"hunt_idle_evidence sample={sample + 1}; waiting={waiting.Found}/{waiting.Confidence:F3}; fixedPoint={fixedPoint.Found}; hunting={hunting.Found}; moving={moving.Found}");
            if (!IsIdleFarmRest(waiting.Found, fixedPoint.Found, hunting.Found, moving.Found)) return false;
            if (sample == 0) await Task.Delay(300, token);
        }
        return true;
    }

    internal static void VerifyHuntActivationPolicy()
    {
        for (var flags = 0; flags < 16; flags++)
            if (IsIdleFarmRest((flags & 1) != 0, (flags & 2) != 0,
                    (flags & 4) != 0, (flags & 8) != 0) != (flags == 1))
                throw new InvalidOperationException("Cidade, movimento ou caça não podem confirmar espera no spot.");
        if (ShouldResetRouteAfterFailure(3, true, true) ||
            !ShouldResetRouteAfterFailure(3, true, false) ||
            !ShouldResetRouteAfterFailure(3, false, true) ||
            ShouldResetRouteAfterFailure(1, true, false))
            throw new InvalidOperationException("Somente leitura inconclusiva do Auto no spot deve preservar a rota após falhas.");
        foreach (var auto in Enum.GetValues<OpenHudHuntState>())
        {
            if (MayEnableHuntFromIdleRest(true, true, false, true, auto) != (auto == OpenHudHuntState.Unknown) ||
                MayEnableHuntFromIdleRest(false, true, false, true, auto) ||
                MayEnableHuntFromIdleRest(true, false, false, true, auto) ||
                MayEnableHuntFromIdleRest(true, true, true, true, auto) ||
                MayEnableHuntFromIdleRest(true, true, false, false, auto))
                throw new InvalidOperationException("Ativação contextual exige chegada, descanso parado válido, HP e ausência de perdas; Auto ativo nunca alterna.");
        }
    }
}
