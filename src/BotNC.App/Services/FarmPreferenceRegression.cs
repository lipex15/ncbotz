namespace BotNC.App.Services;

internal static class FarmPreferenceRegression
{
    internal static void Verify()
    {
        static void Check(bool ok, string error) { if (!ok) throw new InvalidOperationException("Farm preference: " + error); }
        Check(RestPreferencePolicy.ShouldClose(false, "caca_automatica", false), "farm deve sair do descanso desativado");
        foreach (var hud in new[] { OpenHudHuntState.Active, OpenHudHuntState.Inactive })
            Check(!RestPreferencePolicy.ShouldClose(false, "caca_automatica", false, hud), "HUD aberto não pode provocar L/foco por falso descanso");
        var preference = new RestPreferenceRuntime();
        var quiet = new QuietFarmRoutineGate();
        Check(quiet.CanRun("directive", "today", true, false), "horário agendado permite primeira execução");
        quiet.Started("directive", "today");
        for (var tick = 0; tick < 600; tick++)
            Check(!quiet.CanRun("directive", "today", true, false), "retentativa não rouba foco durante farm");
        Check(quiet.CanRun("directive", "tomorrow", true, false), "novo ciclo mantém horário");
        Check(quiet.CanRun("daily", "today", true, false), "rotinas independentes");
        Check(!quiet.CanRun("directive", "today", true, true), "foco deixado pelo bot não libera retentativa no farm");
        Check(quiet.CanRun("directive", "today", false, false), "retomar fora do farm");
        quiet.Started("mail", "today:01");
        Check(!quiet.CanRun("mail", "today:01", true, true), "correio não repete no mesmo horário");
        Check(quiet.CanRun("mail", "today:07", true, false), "próximo horário de correio permanece disponível");
        Check(quiet.CanRun("mail", "tomorrow:01", true, false), "correio volta no próximo dia");
        Check(AppUpdateService.DisplayVersion(new Version(11, 6, 0, 0)) == "11.6", "versão simplificada");
        Check(AppUpdateService.DisplayVersion(new Version(11, 6, 1)) == "11.6.1", "correção da versão deve permanecer visível");
        Check(new Version(11, 6, 0) > new Version(0, 11, 5, 0), "versão nova compatível com atualização anterior");
        Check(preference.Pending, "preferência deve ser aplicada no início");
        preference.Complete();
        for (var idleTick = 0; idleTick < 600; idleTick++)
            Check(!preference.Pending, "farm estabilizado não deve reaplicar descanso nem solicitar foco");
        preference.Request();
        Check(preference.Pending, "ação com descanso/reconexão deve reaplicar preferência");
        preference.Complete();
        Check(!preference.Pending, "retorno ao farm deve encerrar aplicação da preferência");
        Check(!RestPreferencePolicy.ShouldClose(true, "caca_automatica", false), "descanso ativado deve ser preservado");
        foreach (var state in new string?[] { null, "descanso_movendo", "descanso_morte", "tela_descanso", "rest_unlock_instruction" })
            Check(!RestPreferencePolicy.ShouldClose(false, state, false), "não interromper deslocamento/morte/tela desconhecida: " + state);
        Check(RestPreferencePolicy.ShouldClose(false, "tela_descanso", true), "diária confirmada deve voltar à tela aberta");
        Check(RestPreferencePolicy.ShouldOpen(true, null, OpenHudHuntState.Active), "preferência ligada deve descansar farm ativo");
        Check(!RestPreferencePolicy.ShouldOpen(false, null, OpenHudHuntState.Active) &&
            !RestPreferencePolicy.ShouldOpen(true, null, OpenHudHuntState.Unknown) &&
            !RestPreferencePolicy.ShouldOpen(true, null, OpenHudHuntState.Inactive), "não esconder tela ambígua ou desrespeitar preferência");
    }
}
