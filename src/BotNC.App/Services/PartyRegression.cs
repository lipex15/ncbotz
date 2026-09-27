namespace BotNC.App.Services;

internal static class PartyRegression
{
    internal static async Task VerifyAsync(VisualRecognitionService visual, Func<string, PixelFrame> load)
    {
        static void Check(bool ok, string error) { if (!ok) throw new InvalidOperationException("Party: " + error); }
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
