namespace BotNC.App.Services;

internal static class LoadingScreenReader
{
    internal static async Task<bool> IsLoadingAsync(PixelFrame source, CancellationToken token)
    {
        var frame = VisualRecognitionService.NormalizeForReferenceMatching(source);
        const int x = 710, y = 935, width = 500;
        var height = Math.Min(95, frame.Height - y);
        if (frame.Width < x + width || height <= 0) return false;
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
            Buffer.BlockCopy(frame.Pixels, (y + row) * frame.Stride + x * 4,
                pixels, row * width * 4, width * 4);
        var text = (await new RestorationCounterReader().ReadHeaderCropAsync(
            new PixelFrame(width, height, width * 4, pixels), token)).RawText;
        return text.Contains("CARREGANDO", StringComparison.OrdinalIgnoreCase);
    }
}
