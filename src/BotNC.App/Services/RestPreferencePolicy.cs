namespace BotNC.App.Services;

internal static class RestPreferencePolicy
{
    internal static bool ShouldClose(bool keepRest, string? restState, bool dailyConfirmed) =>
        !keepRest && (restState is "caca_automatica" or "daily_automatic" or
            "descanso_ponto_fixo" or "descanso_aguardando_spot" ||
            (dailyConfirmed && restState is "tela_descanso" or "rest_unlock_instruction"));

    internal static bool ShouldOpen(bool keepRest, string? restState, OpenHudHuntState auto) =>
        keepRest && restState is null && auto == OpenHudHuntState.Active;
}
