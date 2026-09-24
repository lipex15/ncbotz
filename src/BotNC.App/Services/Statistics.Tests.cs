namespace BotNC.App.Services;

internal static class StatisticsRegression
{
    internal static async Task VerifyAsync(string output, VisualRecognitionService recognition, Func<string, PixelFrame> load)
    {
        var lines = new List<string>();
        foreach (var (name, layout, expected) in new[]
        {
            ("ta_selector_user_active.png", "ta1", 25000L),
            ("ta_selector_user_active.png", "ta2", 35000L),
            ("ta_selector_user_active.png", "ta3", 45000L),
            ("anonymous_entry_confirmation.png", "dungeon", 350000L),
            ("daily_shop_bulk_popup_full.png", "daily-shop", 300000L),
            ("statistics_article_purchase.png", "articles", 244100L)
        })
        {
            var reading = await GoldPriceReader.ReadAsync(recognition, load(name), layout, CancellationToken.None);
            lines.Add($"gold fixture={name}; layout={layout}; expected={expected}; actual={reading.Value}; {reading.Evidence}");
            await System.IO.File.WriteAllLinesAsync(output, lines);
            if (reading.Value != expected) throw new InvalidOperationException(lines[^1]);
        }
        foreach (var (name, x, y, width, height, expected) in new[]
        {
            ("statistics_daily_price.png", 337, 45, 150, 35, 8100L),
            ("daily_teleport.png", 411, 357, 150, 36, 15000L),
            ("abadia_confirmacao.png", 405, 217, 150, 36, 200000L)
        })
        {
            var reading = await GoldPriceReader.ReadCropAsync(load(name), x, y, width, height, CancellationToken.None);
            lines.Add($"gold crop={name}; expected={expected}; actual={reading.Value}; {reading.Evidence}");
            await System.IO.File.WriteAllLinesAsync(output, lines);
            if (reading.Value != expected) throw new InvalidOperationException(lines[^1]);
        }
        var articleSource = load("statistics_article_purchase.png");
        foreach (var offsetY in new[] { -100, 100 })
        {
            var shifted = new PixelFrame(1920, 1040, 7680, new byte[1920 * 1040 * 4]);
            for (var y = 0; y < Math.Min(1040, articleSource.Height); y++)
                if (y + offsetY >= 0 && y + offsetY < 1040)
                    Buffer.BlockCopy(articleSource.Pixels, y * articleSource.Stride, shifted.Pixels, (y + offsetY) * shifted.Stride, 7680);
            var price = await GoldPriceReader.ReadAsync(recognition, shifted, "articles", CancellationToken.None);
            if (price.Value != 244100) throw new Exception($"Preço do lote deslocado {offsetY}: {price.Evidence}");
        }
        foreach (var (name, x, y, expected) in new[]
        {
            ("daily_teleport.png", 630, 210, 15000L),
            ("statistics_daily_price.png", 700, 500, 8100L),
            ("abadia_confirmacao.png", 630, 300, 200000L)
        })
        {
            var crop = load(name);
            var full = new PixelFrame(1920, 1040, 7680, new byte[1920 * 1040 * 4]);
            for (var row = 0; row < crop.Height; row++)
                Buffer.BlockCopy(crop.Pixels, row * crop.Stride, full.Pixels, (y + row) * full.Stride + x * 4, crop.Width * 4);
            var reading = await GoldPriceReader.ReadAsync(recognition, full, "daily-teleport", CancellationToken.None);
            lines.Add($"gold anchored={name}; expected={expected}; actual={reading.Value}; {reading.Evidence}");
            await System.IO.File.WriteAllLinesAsync(output, lines);
            if (reading.Value != expected) throw new InvalidOperationException(lines[^1]);
        }
        foreach (var layout in new[] { "ta1", "ta2", "ta3", "dungeon", "daily-shop", "articles", "daily-teleport" })
        {
            var reading = await GoldPriceReader.ReadAsync(recognition, load("guild_treasure_empty.png"), layout, CancellationToken.None);
            if (reading.Value is not null) throw new Exception("Ouro reconhecido fora da tela: " + layout);
        }
        foreach (var text in new[] { "8.100 | 8.100", "8,100 | 8,100", "8100 | 8100", "8.100 | 8.100 | 8±100" })
            if (GoldPriceReader.ParseConsensus(text) != 8100) throw new Exception("Preço válido rejeitado.");
        foreach (var text in new[] { "8.100 | 81.000", "8.100 | 8.100 | 81.000", "8.100", "", "8.1 | 8.1", "0 | 0", "Preço 8100 | Preço 8100", "26.516.176 8100 | 8100", "100000001 | 100000001" })
            if (GoldPriceReader.ParseConsensus(text) is not null) throw new Exception("Preço ambíguo aceito: " + text);
        var store = new StatisticsStore(output + ".db");
        var at = new DateTimeOffset(2026, 9, 24, 2, 59, 59, TimeSpan.Zero);
        var first = new StatisticEvent("purchase", "profileA", 1, "sessionA", at, "expense", "Loja", 8100);
        await store.RecordAsync(first);
        await store.RecordAsync(first);
        await store.RecordAsync(first with { Client = 2, Gold = 244100 });
        await store.RecordAsync(first with { Profile = "profileB", Gold = 999 });
        await store.RecordAsync(first with { Id = "unknown", Gold = null, Session = "sessionB" });
        await store.RecordAsync(first with { Id = "pending", Kind = "pending", Gold = 500 });
        await store.RecordAsync(first with { Id = "pending", Kind = "expense", Gold = 500 });
        await store.RecordAsync(first with { Id = "tomorrow", At = at.AddSeconds(1) });
        var reopened = new StatisticsStore(output + ".db");
        var rows = await reopened.ReadAsync("profileA", at.AddSeconds(-1), at.AddSeconds(1), 1, "sessionA");
        if (rows.Count != 2 || rows.Sum(item => item.Gold ?? 0) != 8600 || rows.Any(item => item.Kind != "expense"))
            throw new Exception("Falha de isolamento, período, promoção ou deduplicação.");
        var all = await reopened.ReadAsync("profileA", at.AddSeconds(-1), at.AddSeconds(1));
        if (all.Count != 4 || all.Count(item => item.Gold is null) != 1 || all.Sum(item => item.Gold ?? 0) != 252700)
            throw new Exception("Total misturou clientes/usuários/valores desconhecidos.");
        lines.Add("statistics dedup=true; restart=true; profiles=true; clients=true; session=true; periodBoundary=true; unknownExcluded=true; pendingPromotion=true");
        var backup = await reopened.ResetAsync("profileA", 1, false);
        if (!System.IO.File.Exists(backup) || (await reopened.ReadAsync("profileA", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, 1)).Count != 0 ||
            (await reopened.ReadAsync("profileA", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, 2)).Count != 1 ||
            (await reopened.ReadAsync("profileB", DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).Count != 1)
            throw new Exception("Reset vazou para outro cliente/perfil ou não criou backup.");
        await reopened.RecordAsync(first);
        if ((await reopened.ReadAsync("profileA", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, 1)).Count != 0)
            throw new Exception("Reset permitiu reaparecer um evento antigo repetido.");
        lines.Add("statistics resetBackup=true; resetClientIsolation=true; resetDedup=true");
        await System.IO.File.WriteAllLinesAsync(output, lines);
    }
}
