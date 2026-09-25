namespace BotNC.App.Services;

internal sealed record TombstoneIconReading(
    bool Found, bool HasRedSignal, double Confidence, int X, int Y, int FrameHeight)
{
    public int ClickReferenceX => X;
    public int ClickReferenceY => (int)Math.Round((Y + 7d) * 1040 / FrameHeight);

    public bool AgreesWith(TombstoneIconReading? previous) =>
        Found && previous is { Found: true } && FrameHeight == previous.FrameHeight &&
        Math.Abs(X - previous.X) <= 4 && Math.Abs(Y - previous.Y) <= 4;
}

internal static class TombstoneIconReader
{
    internal static bool MayInspectAfterDeath(bool pending, bool red, double confidence, int x, int y) =>
        pending && red && confidence >= 0.60 && x >= 1477 && x <= 1578 && y >= 35 && y <= 95;

    public static async Task<TombstoneIconReading> ReadAsync(
        VisualRecognitionService recognition, PixelFrame source, CancellationToken token, bool restorationPending = false)
    {
        var frame = VisualRecognitionService.NormalizeForReferenceMatching(source);
        // Keep the search around the restoration shortcut, not Guild/shop badges.
        var icon = await recognition.FindAsync("lapide_nucleo", frame, token);
        var redVariant = await recognition.FindAsync("lapide_vermelha", frame, token);
        if (redVariant.Found && (!icon.Found || redVariant.Confidence > icon.Confidence))
            icon = redVariant;
        var candidateRed = TombstoneIconAnalyzer.HasRedAt(frame, icon.X, icon.Y);
        var contextual = MayInspectAfterDeath(restorationPending, candidateRed, icon.Confidence, icon.X, icon.Y);
        return new((icon.Found || contextual) && candidateRed, candidateRed, icon.Confidence,
            icon.X, icon.Y, frame.Height);
    }
}
