namespace BotNC.App.Services;

internal sealed record TombstoneIconReading(
    bool Found, bool HasRedSignal, double Confidence, int X, int Y, int FrameHeight,
    bool AreaInspection = false)
{
    public int ClickReferenceX => X;
    public int ClickReferenceY => (int)Math.Round((Y + 7d) * 1040 / FrameHeight);

    public bool AgreesWith(TombstoneIconReading? previous) =>
        Found && previous is { Found: true } && FrameHeight == previous.FrameHeight &&
        Math.Abs(X - previous.X) <= 4 && Math.Abs(Y - previous.Y) <= 4;
}

internal static class TombstoneIconReader
{
    internal static bool HasRestorationAreaSignal(PixelFrame frame)
    {
        if (frame.Width < 1596 || frame.Height < 127) return false;
        var hits = 0;
        var bands = new int[3];
        var columns = new HashSet<int>();
        // Restrict the full silhouette to its known slot, excluding the nearby
        // currency, shop and guild notification badges. Require vertical extent.
        for (var y = 28; y <= 99; y += 2)
        for (var x = 1510; x <= 1566; x += 2)
        {
            var i = y * frame.Stride + x * 4;
            var b = frame.Pixels[i]; var g = frame.Pixels[i + 1]; var r = frame.Pixels[i + 2];
            if (r >= 110 && r >= g * 1.35 && r >= b * 1.25)
            { hits++; bands[Math.Min(2, (y - 28) / 24)]++; columns.Add(x); }
        }
        return hits >= 24 && columns.Count >= 5 && bands.Count(count => count >= 5) >= 2;
    }
    internal static bool MayInspectAfterDeath(bool pending, bool red, double confidence, int x, int y) =>
        pending && red && confidence >= 0.60 && x >= 1477 && x <= 1578 && y >= 35 && y <= 95;

    public static async Task<TombstoneIconReading> ReadAsync(
        VisualRecognitionService recognition, PixelFrame source, CancellationToken token, bool restorationPending = false)
    {
        var frame = VisualRecognitionService.NormalizeForReferenceMatching(source);
        // Keep the search around the restoration shortcut, not Guild/shop badges.
        var icon = await recognition.FindAsync("lapide_nucleo", frame, token);
        var redVariant = await recognition.FindAsync("lapide_vermelha", frame, token);
        if ((redVariant.Found && !icon.Found) ||
            (!icon.Found && redVariant.Confidence > icon.Confidence) ||
            (redVariant.Found && redVariant.Confidence > icon.Confidence))
            icon = redVariant;
        var candidateRed = TombstoneIconAnalyzer.HasRedAt(frame, icon.X, icon.Y);
        var areaSignal = HasRestorationAreaSignal(frame);
        var contextual = MayInspectAfterDeath(restorationPending, candidateRed, icon.Confidence, icon.X, icon.Y);
        var areaInspection = restorationPending && areaSignal && !(icon.Found && candidateRed);
        var found = (icon.Found || contextual || areaInspection) &&
            (candidateRed || restorationPending && areaSignal);
        // Area evidence authorizes opening the panel, never fabricates a matching
        // percentage. The actual panel/counters establish whether losses exist.
        return new(found, candidateRed || areaSignal, icon.Confidence,
            areaInspection ? 1538 : icon.X, areaInspection ? 63 : icon.Y, frame.Height, areaInspection);
    }
}
