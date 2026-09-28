namespace BotNC.App.Services;

internal static class PartyRegression
{
    internal static async Task VerifyAsync(VisualRecognitionService visual, Func<string, PixelFrame> load)
    {
        static void Check(bool ok, string error) { if (!ok) throw new InvalidOperationException("Party: " + error); }
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
        Check(quiet.CanRun("directive", "today", true, true), "retomar com jogo já visível");
        Check(quiet.CanRun("directive", "today", false, false), "retomar fora do farm");
        quiet.NewWorkObserved("directive");
        Check(quiet.CanRun("directive", "today", true, false), "nova tarefa comprovada permite ação");
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
        foreach (var (reference, file) in new[] {
            ("party_panel", "party_panel.png"), ("party_empty", "party_create.png"),
            ("party_create_confirm", "party_create.png"), ("party_invite_prompt", "party_invite.png"),
            ("party_dissolve", "party_panel.png"), ("party_dissolve_confirm", "party_dissolve.png"),
            ("party_member_card", "party_member.png") })
            Check((await visual.FindAsync(reference, load(file))).Found, reference);
        Check(await PartyReader.HasInvitationAsync(visual, load("party_received.png"), CancellationToken.None), "convite real");
        Check(await PartyReader.HasInvitationAsync(visual, load("party_received_scaled.png"), CancellationToken.None), "convite com outro nick/resolução");
        foreach (var file in new[] { "party_create.png", "party_invite.png", "party_dissolve.png", "party_panel.png",
            "reconnect_inactivity.png", "reconnect_world.png", "reconnect_character.png", "party_member.png" })
            Check(!await PartyReader.HasInvitationAsync(visual, load(file), CancellationToken.None), "falso convite " + file);
        Check(PartyReader.MemberRows(load("party_panel.png")).Length == 1, "contagem do líder");
        Check(PartyReader.HudCards(load("party_member.png")) == 1 &&
            await PartyReader.HasMemberCardAsync(visual, load("party_member.png"), CancellationToken.None), "card no HUD");
        foreach (var file in new[] { "party_panel.png", "reconnect_world.png", "reconnect_character.png" })
            Check(!await PartyReader.HasMemberCardAsync(visual, load(file), CancellationToken.None), "falso card " + file);
        var panel = load("party_panel.png");
        var five = (byte[])panel.Pixels.Clone();
        for (var member = 1; member < 5; member++)
        for (var row = 195; row < 235; row++)
            Buffer.BlockCopy(panel.Pixels, row * panel.Stride + 27 * 4,
                five, (row + member * 85) * panel.Stride + 27 * 4, 40 * 4);
        Check(PartyReader.MemberRows(new(panel.Width, panel.Height, panel.Stride, five)).Length == 5, "cinco membros");
        foreach (var shift in new[] { 0, 170, 340 })
        {
            var moved = new byte[panel.Pixels.Length];
            for (var row = 252; row < 278; row++)
                Buffer.BlockCopy(panel.Pixels, row * panel.Stride + 308 * 4,
                    moved, (row + shift) * panel.Stride + 308 * 4, 71 * 4);
            var found = await visual.FindAsync("party_dissolve", new PixelFrame(panel.Width, panel.Height, panel.Stride, moved));
            Check(found.Found && Math.Abs(found.Y - (265 + shift)) <= 2, "Dissolver deslocado " + shift);
        }
        var names = PartyPolicy.ParseNames("流氓LIPEX\r\n대박Brabo\nweed · lover\n  Nick  ");
        Check(names.Length == 4 && names[0] == "流氓LIPEX" && names[3] == "  Nick  ", "Unicode/espaços alterados");
        Check(!PartyPolicy.ContainsName("AnotherNick", "Nick") && PartyPolicy.ContainsName("\nweed · lover\n", "weed · lover"), "identidade parcial");
        Check(PartyPolicy.ClearlyDifferentInviter("Convite para a equipe de OutroNick. Deseja aceitar?", "MeuNick") &&
            !PartyPolicy.ClearlyDifferentInviter("Convite para a equipe de 流氓LIPEX. Deseja aceitar?", "MeuNick"), "remetente ambíguo");
        var runtime = new PartyRuntime();
        Check(runtime.NeedsObservation, "nova sessão deve verificar a PT existente");
        runtime.RosterEstablished = true;
        Check(!runtime.NeedsObservation, "PT confirmada não deve continuar verificando");
        runtime.Reconnected(default);
        Check(!runtime.NeedsObservation, "sem reconexão não deve reabrir a verificação");
        var epoch = DateTime.UtcNow;
        runtime.Reconnected(epoch);
        Check(runtime.NeedsObservation && !runtime.RosterEstablished, "reconexão deve conferir PT novamente");
        runtime.RosterEstablished = true;
        runtime.RecheckAfterReconnect = false;
        runtime.Reconnected(epoch);
        Check(!runtime.NeedsObservation, "mesma reconexão não deve reiniciar checagem concluída");
        for (var i = 0; i < 5; i++) { Check(runtime.MayInvite(names[0]), "limite antecipado"); runtime.RecordInvite(names[0]); }
        Check(!runtime.MayInvite(names[0]) && runtime.MayInvite(names[1]), "limite por pessoa");
        runtime.Reconnected(DateTime.UtcNow);
        Check(!runtime.MayInvite(names[0]), "reconnect zerou limite");
        runtime.Confirmed.Add(names[1]);
        Check(!runtime.MayInvite(names[1]) && new PartyRuntime().MayInvite(names[0]), "reinício/confirmação");
        try { PartyPolicy.ParseNames("a\nb\nc\nd\ne"); throw new InvalidOperationException("cinco convidados aceitos"); }
        catch (ArgumentException) { }
    }
}
