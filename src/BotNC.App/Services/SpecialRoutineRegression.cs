namespace BotNC.App.Services;

internal static class SpecialRoutineRegression
{
    internal static async Task VerifyAsync(VisualRecognitionService visual, Func<string, PixelFrame> load)
    {
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("Global/Boost: " + message); }
        static PixelFrame Place(PixelFrame crop, int x, int y)
        {
            var pixels = new byte[1920 * 1080 * 4];
            for (var row = 0; row < crop.Height; row++)
                Buffer.BlockCopy(crop.Pixels, row * crop.Stride, pixels, ((y + row) * 1920 + x) * 4, crop.Width * 4);
            return new(1920, 1080, 1920 * 4, pixels);
        }
        SpecialRoutinePolicy.Verify();
        foreach (var (id, file) in new[] { ("global_page", "global_page.png"), ("global_confirm", "global_confirm.png"), ("global_map", "global_map.png") })
            Check((await visual.FindAsync(id, load(file))).Found, "referência real " + id);
        foreach (var id in new[] { "global_spawn_north", "global_spawn_south" })
            Check((await visual.FindAsync(id, Place(load(id + ".png"), 0, 72))).Found, "entrada por " + id);
        Check((await visual.FindAsync("boost_npc", Place(load("boost_npc.png"), 10, 352))).Found, "NPC no fim da lista");
        foreach (var file in new[] { "boost_list_end.png", "boost_npc.png", "boost_npc_scrolled.png" })
        {
            var frame = Place(load(file), 10, file == "boost_list_end.png" ? 90 : 352);
            Check((await visual.FindAsync("boost_npc_icon", frame)).Found, "ícone fixo independente do texto: " + file);
        }
        foreach (var x in new[] { 108, 137, 200, 330 })
            Check((await visual.FindAsync("boost_buff", Place(load("boost_icon.png"), x, 922))).Found, "buff deslocado " + x);
        var active = Place(load("global_enter.png"), 1688, 953);
        Check(await GlobalEntryReader.ReadyAsync(visual, active, CancellationToken.None), "botão Entrar ativo");
        Check(!await GlobalEntryReader.ReadyAsync(visual, load("global_page.png"), CancellationToken.None), "botão apagado antes do horário");
        foreach (var file in new[] { "reconnect_world.png", "global_map.png", "global_confirm.png" })
        {
            Check(!(await visual.FindAsync("boost_npc_icon", load(file))).Found, "falso Patrocinador em " + file);
            Check(!(await visual.FindAsync("boost_buff", load(file))).Found, "falso buff em " + file);
            Check(!await GlobalEntryReader.ReadyAsync(visual, load(file), CancellationToken.None), "falsa entrada em " + file);
        }
    }
}

internal static class GlobalEntryReader
{
    internal static async Task<bool> ReadyAsync(VisualRecognitionService visual, PixelFrame source, CancellationToken token)
    {
        var frame = VisualRecognitionService.NormalizeForReferenceMatching(source);
        var match = await visual.FindAsync("global_enter", frame, token);
        if (!match.Found) return false;
        // The disabled button shares the same lettering: require its lit fill too.
        double luma = 0;
        var count = 0;
        for (var y = Math.Max(0, match.Y - 12); y < Math.Min(frame.Height, match.Y + 12); y++)
        for (var x = Math.Max(0, match.X - 80); x < Math.Min(frame.Width, match.X + 80); x++)
        {
            var i = y * frame.Stride + x * 4;
            luma += .114 * frame.Pixels[i] + .587 * frame.Pixels[i + 1] + .299 * frame.Pixels[i + 2];
            count++;
        }
        return count > 0 && luma / count >= 70;
    }
}
