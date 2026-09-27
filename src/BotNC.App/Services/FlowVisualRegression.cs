namespace BotNC.App.Services;

internal static class FlowVisualRegression
{
    internal static async Task VerifyAsync(VisualRecognitionService recognition, Func<string, PixelFrame> load)
    {
        var deathRest = load("regression_death_rest_20260927.png");
        if (!(await recognition.FindAsync("rest_unlock_instruction", deathRest)).Found)
            throw new InvalidOperationException("Descanso real de morte não foi reconhecido; perdas ficariam ocultas.");
        var restControls = (await recognition.FindAsync("game_hud_menu", deathRest)).Found ||
            (await recognition.FindAsync("hud_auto_label", deathRest)).Found;
        // Deliberately simulate the misleading HP=true from the incident log.
        if (BotAutomationEngine.HasSufficientRestorationHud(restControls, true, true))
            throw new InvalidOperationException("Captura real de descanso foi aceita como HUD completo com HP isolado.");
        if ((await recognition.FindAsync("rest_unlock_instruction", load("hud_auto_active.png"))).Found ||
            (await recognition.FindAsync("rest_unlock_instruction", load("reconnect_character.png"))).Found)
            throw new InvalidOperationException("Arraste de descanso liberado fora da tela de descanso.");
        var balance = FarmScheduleCheckpoint.TryRead(System.Text.Json.JsonSerializer.Serialize(
            new FarmScheduleCheckpoint("Abbey:300", 0, TimeSpan.FromMinutes(300 - 100).TotalSeconds, false, false)));
        if (balance?.RemainingSeconds != 12000 || FarmScheduleCheckpoint.TryRead("corrompido") is not null)
            throw new InvalidOperationException("Saldo persistente da Agenda inválido.");
        static PixelFrame Canvas(PixelFrame crop, int x, int y)
        {
            const int stride = 1920 * 4;
            var pixels = new byte[stride * 1040];
            for (var row = 0; row < crop.Height; row++)
                Array.Copy(crop.Pixels, row * crop.Stride, pixels, (row + y) * stride + x * 4, crop.Width * 4);
            return new PixelFrame(1920, 1040, stride, pixels);
        }
        foreach (var (file, expected) in new[] {
            ("regression_auto_range_on.png", OpenHudHuntState.Active),
            ("regression_auto_range_off.png", OpenHudHuntState.Inactive) })
        {
            var frame = Canvas(load(file), 1815, 615);
            var autoControl = (await recognition.FindAsync("hud_auto_label", frame)).Found;
            if (!BotAutomationEngine.HasSufficientRestorationHud(autoControl, true, true))
                throw new InvalidOperationException($"HUD aberto não pode depender só do menu decorativo: {file}");
            if (await OpenHudHuntReader.ReadAsync(recognition, frame, CancellationToken.None) != expected)
                throw new InvalidOperationException($"Auto com distância variável: {file}");
        }
        var red = await TombstoneIconReader.ReadAsync(recognition, Canvas(load("lapide_vermelha.png"), 1518, 38), CancellationToken.None);
        if (!red.Found || red.X != 1535 || red.Y != 63)
            throw new InvalidOperationException("Lápide vermelha não reconhecida na partida sem morte previamente salva.");
        var exit = Canvas(load("regression_dungeon_exit.png"), 328, 100);
        if ((await recognition.FindAsync("boss_entry_icon", exit)).Found)
            throw new InvalidOperationException("Saída da masmorra confundida com entrada do Boss.");
        if (BotAutomationEngine.HasPurpleDailyMission(Canvas(load("regression_white_quest.png"), 1500, 110)))
            throw new InvalidOperationException("Missão branca confundida com diária.");
        // Move the real raid glyph to the adjacent slot, independently of minimap layout.
        var source = load("boss_entry_panel.png");
        var glyph = new byte[37 * 34 * 4];
        for (var row = 0; row < 34; row++)
            Array.Copy(source.Pixels, (102 + row) * source.Stride + 334 * 4, glyph, row * 37 * 4, 37 * 4);
        foreach (var x in new[] { 334, 394, 454 })
            if (!(await recognition.FindAsync("boss_entry_icon", Canvas(new PixelFrame(37, 34, 37 * 4, glyph), x, 102))).Found)
                throw new InvalidOperationException("Ícone do Boss deslocado não reconhecido.");
    }
}
