namespace BotNC.App.Services;

internal enum OpenHudHuntState { Unknown, Active, Inactive }
internal static class OpenHudHuntReader
{
    internal static async Task<OpenHudHuntState> ReadAsync(VisualRecognitionService recognition, PixelFrame source, CancellationToken token)
    {
        var frame = VisualRecognitionService.NormalizeForReferenceMatching(source);
        var on = await recognition.FindAsync("hud_auto_active", frame, token);
        var off = await recognition.FindAsync("hud_auto_inactive", frame, token);
        if (!on.Found && !off.Found) return OpenHudHuntState.Unknown;
        // Both states share the same lettering. Compare the orange halo as well.
        var cx = on.Confidence >= off.Confidence ? on.X : off.X;
        var cy = on.Confidence >= off.Confidence ? on.Y : off.Y;
        var orange = 0;
        for (var y = Math.Max(0, cy - 24); y < Math.Min(frame.Height, cy + 24); y++)
        for (var x = Math.Max(0, cx - 25); x < Math.Min(frame.Width, cx + 25); x++)
        {
            var i = y * frame.Stride + x * 4;
            var b = frame.Pixels[i]; var g = frame.Pixels[i + 1]; var r = frame.Pixels[i + 2];
            if (r > 145 && g > 65 && r > g * 1.15 && g > b * 1.3) orange++;
        }
        return on.Found && orange >= 180 ? OpenHudHuntState.Active :
            off.Found && orange < 100 ? OpenHudHuntState.Inactive : OpenHudHuntState.Unknown;
    }
}
