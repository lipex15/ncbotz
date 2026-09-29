namespace BotNC.App.Services;

internal static class RestPreferencePolicy
{
    internal static bool ShouldClose(bool keepRest, string? restState, bool dailyConfirmed,
        OpenHudHuntState openHud = OpenHudHuntState.Unknown) =>
        !keepRest && openHud == OpenHudHuntState.Unknown && (restState is "caca_automatica" or "daily_automatic" or
            "descanso_ponto_fixo" or "descanso_aguardando_spot" ||
            (dailyConfirmed && restState is "tela_descanso" or "rest_unlock_instruction"));

    internal static bool ShouldOpen(bool keepRest, string? restState, OpenHudHuntState auto) =>
        keepRest && restState is null && auto == OpenHudHuntState.Active;
}

internal sealed class RestPreferenceRuntime
{
    private long? _openSince;
    private long _lastIdleAttempt;
    internal long RequestedAt { get; private set; } = Environment.TickCount64;
    internal bool Pending { get; private set; } = true;
    internal void Request() { Pending = true; RequestedAt = Environment.TickCount64; }
    internal void Complete() => Pending = false;
    internal bool ObserveIdle(long now, long lastPhysicalAt, bool enabled, bool farming, bool openAuto, bool resting)
    {
        if (!enabled || !farming || !openAuto || resting) { _openSince = null; return false; }
        _openSince ??= now;
        const long delay = 180000;
        if (now - Math.Max(_openSince.Value, Math.Max(lastPhysicalAt, _lastIdleAttempt)) < delay) return false;
        _lastIdleAttempt = now;
        return true;
    }
}
