namespace BotNC.App.Services;

internal static class TombstoneIconRegression
{
    internal static async Task<IReadOnlyList<string>> VerifyAsync(VisualRecognitionService recognition,
        Func<string, PixelFrame> load)
    {
        var report = new List<string>();
        var original = load("perda_exp.png");
        foreach (var scale in new[] { 2d / 3, 0.75, 1d, 1.25, 1.5 })
        foreach (var brightness in new[] { 0.7, 1d, 1.2 })
        {
            var sample = Resize(original, (int)(original.Width * scale), (int)(original.Height * scale), brightness);
            var reading = await TombstoneIconReader.ReadAsync(recognition, sample, CancellationToken.None);
            report.Add($"tombstone scale={scale:F2}; brightness={brightness:F2}; found={reading.Found}; score={reading.Confidence:F3}; xy={reading.X},{reading.Y}");
            if (!reading.Found || Math.Abs(reading.X - 1535) > 4 || Math.Abs(reading.Y - 63) > 4)
                throw new InvalidOperationException(report[^1]);
        }
        foreach (var file in new[] { "agenda_tela.png", "ta_selector_user_active.png", "daily_page.png",
                     "anonymous_arrival_regression.png", "anonymous_arrival_inventory.png" })
        {
            var reading = await TombstoneIconReader.ReadAsync(recognition, load(file), CancellationToken.None);
            report.Add($"tombstone negative={file}; found={reading.Found}; score={reading.Confidence:F3}; red={reading.HasRedSignal}");
            if (reading.Found) throw new InvalidOperationException(report[^1]);
        }
        var moved = new PixelFrame(original.Width, original.Height, original.Stride, (byte[])original.Pixels.Clone());
        for (var y = 18; y < 100; y++)
            Array.Clear(moved.Pixels, y * moved.Stride + 1502 * 4, 70 * 4);
        for (var y = 0; y < 82; y++)
            Buffer.BlockCopy(original.Pixels, (18 + y) * original.Stride + 1502 * 4,
                moved.Pixels, (26 + y) * moved.Stride + 1520 * 4, 70 * 4);
        var displaced = await TombstoneIconReader.ReadAsync(recognition, moved, CancellationToken.None);
        if (!displaced.Found || Math.Abs(displaced.X - 1553) > 3 || Math.Abs(displaced.Y - 71) > 3)
            throw new InvalidOperationException($"Lápide deslocada não localizada: {displaced}");
        var initial = await TombstoneIconReader.ReadAsync(recognition, original, CancellationToken.None);
        if (displaced.AgreesWith(initial) || !initial.AgreesWith(initial) ||
            (initial with { Found = false }).AgreesWith(initial))
            throw new InvalidOperationException("Estabilidade da posição da lápide incorreta.");
        report.Add("tombstone displaced=true; unstablePositionRejected=true");

        // A red notification alone must neither cause a click nor block known absence.
        var badge = new PixelFrame(1920, 1040, 1920 * 4, new byte[1920 * 1040 * 4]);
        for (var y = 37; y < 79; y++)
        for (var x = 1517; x < 1553; x++)
            badge.Pixels[y * badge.Stride + x * 4 + 2] = 200;
        var badgeReading = await TombstoneIconReader.ReadAsync(recognition, badge, CancellationToken.None);
        if (badgeReading.Found || BotAutomationEngine.ClassifyStartupRestoration(true, true, false, false, false)
                != BotAutomationEngine.StartupRestorationObservation.Absent)
            throw new InvalidOperationException("Uma marca vermelha sem forma virou lápide.");
        report.Add("tombstone redBadgeRejected=true; validHudWithoutIcon=Absent");
        var pendingArea = await TombstoneIconReader.ReadAsync(recognition, badge, CancellationToken.None, true);
        if (!pendingArea.Found || !pendingArea.AreaInspection || pendingArea.X != 1538 || pendingArea.Y != 63)
            throw new InvalidOperationException("Morte pendente com área vermelha deve permitir inspeção do painel, sem depender da forma.");
        var smallBadge = new PixelFrame(1920, 1040, 1920 * 4, new byte[1920 * 1040 * 4]);
        for (var y = 30; y < 38; y++)
        for (var x = 1530; x < 1538; x++) smallBadge.Pixels[y * smallBadge.Stride + x * 4 + 2] = 200;
        if (TombstoneIconReader.HasRestorationAreaSignal(smallBadge))
            throw new InvalidOperationException("Ponto vermelho isolado não pode confirmar a área da lápide.");
        report.Add("tombstone pendingAreaInspection=true; tinyBadgeRejected=true; confidenceNotFabricated=true");
        return report;
    }

    private static PixelFrame Resize(PixelFrame source, int width, int height, double brightness)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sx = Math.Min(source.Width - 1, (int)((x + 0.5) * source.Width / width));
            var sy = Math.Min(source.Height - 1, (int)((y + 0.5) * source.Height / height));
            var sourceOffset = sy * source.Stride + sx * 4;
            var targetOffset = (y * width + x) * 4;
            for (var channel = 0; channel < 3; channel++)
                pixels[targetOffset + channel] = (byte)Math.Clamp(source.Pixels[sourceOffset + channel] * brightness, 0, 255);
            pixels[targetOffset + 3] = 255;
        }
        return new(width, height, width * 4, pixels);
    }
}
