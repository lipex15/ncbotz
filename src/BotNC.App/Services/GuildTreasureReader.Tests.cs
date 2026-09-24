namespace BotNC.App.Services;

internal static class GuildTreasureRegression
{
    internal static async Task<IReadOnlyList<string>> VerifyAsync(VisualRecognitionService recognition,
        AppDatabase database, Func<string, PixelFrame> load)
    {
        var report = new List<string>();
        foreach (var (file, expected, count) in new[]
        {
            ("guild_treasure_available.png", GuildTreasureState.Available, (int?)1),
            ("guild_treasure_empty.png", GuildTreasureState.Empty, (int?)0),
            ("guild_treasure_reward.png", GuildTreasureState.Reward, (int?)null),
            ("ta_selector_user_active.png", GuildTreasureState.Unknown, (int?)null)
        })
        {
            var reading = await GuildTreasureReader.ReadAsync(recognition, load(file), CancellationToken.None);
            report.Add($"treasure fixture={file}; state={reading.State}; {reading.Evidence}");
            if (reading.State != expected || reading.Count != count)
                throw new InvalidOperationException(report[^1]);
        }
        // The names and graphics of rewards are deliberately irrelevant.
        foreach (var (file, state) in new[] { ("guild_treasure_available.png", GuildTreasureState.Available),
                     ("guild_treasure_empty.png", GuildTreasureState.Empty), ("guild_treasure_reward.png", GuildTreasureState.Reward) })
        {
            var source = load(file);
            var modified = new PixelFrame(source.Width, source.Height, source.Stride, (byte[])source.Pixels.Clone());
            for (var y = 470; y < 695; y++)
                Array.Clear(modified.Pixels, y * modified.Stride + 1460 * 4, 300 * 4);
            if (state == GuildTreasureState.Available)
                for (var y = 700; y < 775; y++) Array.Clear(modified.Pixels, y * modified.Stride + 1460 * 4, 300 * 4);
            if (state == GuildTreasureState.Reward)
                for (var y = 475; y < 580; y++) Array.Clear(modified.Pixels, y * modified.Stride + 850 * 4, 240 * 4);
            var reading = await GuildTreasureReader.ReadAsync(recognition, modified, CancellationToken.None);
            if (reading.State != state) throw new InvalidOperationException($"Detector depende do desenho/nome do item: {file}; {reading}");
        }
        foreach (var count in new[] { 0, 1, 2, 7, 30 })
            if (GuildTreasureReader.ParseCount($"QNTD. DE BAU: {count}") != count)
                throw new InvalidOperationException("Quantidade de baús não reconhecida.");
        if (GuildTreasureReader.ParseCount("QNTD. DE BAU: 1 | QNTD. DE BAU: 0") is not null ||
            GuildTreasureReader.ParseCount("Item Obtido") is not null)
            throw new InvalidOperationException("Leitura incerta virou quantidade.");
        for (var count = 3; count > 0; count--)
            if (!BotAutomationEngine.GuildTreasureCollectionProgressed(count, count - 1))
                throw new InvalidOperationException("Sequência de múltiplos baús inválida.");
        if (BotAutomationEngine.GuildTreasureCollectionProgressed(2, 2) ||
            BotAutomationEngine.GuildTreasureCollectionProgressed(2, null) ||
            BotAutomationEngine.GuildTreasureCollectionProgressed(2, 3))
            throw new InvalidOperationException("Clique repetido sem progresso permitido.");
        var now = DateTimeOffset.Parse("2026-09-24T01:00:00Z");
        if (!BotAutomationEngine.GuildTreasureDue(now, null) ||
            BotAutomationEngine.GuildTreasureDue(now.AddHours(17).AddTicks(-1), now) ||
            !BotAutomationEngine.GuildTreasureDue(now.AddHours(17), now))
            throw new InvalidOperationException("Intervalo de 17 horas incorreto.");
        const string key = "users.treasure-test.client1.routines.guildTreasure.lastVisit";
        await database.SaveSettingAsync(key, now.ToString("O"));
        var reopened = new AppDatabase(System.IO.Path.GetDirectoryName(database.DatabasePath));
        var restored = DateTimeOffset.Parse((await reopened.GetSettingAsync(key))!);
        if (restored != now || BotAutomationEngine.GuildTreasureDue(now.AddHours(1), restored) ||
            await reopened.GetSettingAsync("users.treasure-test.client2.routines.guildTreasure.lastVisit") is not null)
            throw new InvalidOperationException("Agenda do baú não persistiu ou vazou entre clientes.");
        report.Add("treasure itemIndependent=true; multiple=3,2,1,0; unchangedRejected=true; interval=17h; restartPersisted=true; clientsIsolated=true");
        return report;
    }
}
