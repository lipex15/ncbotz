using System.Text.RegularExpressions;

namespace BotNC.App.Services;

internal sealed record GoldPriceReading(long? Value, string Evidence);

internal static class GoldPriceReader
{
    internal static async Task<GoldPriceReading> ReadAsync(VisualRecognitionService recognition,
        PixelFrame source, string layout, CancellationToken token)
    {
        var frame = VisualRecognitionService.NormalizeForReferenceMatching(source);
        var region = layout switch
        {
            "ta1" => (540, 720, 60, 80), "ta2" => (805, 720, 65, 80),
            "ta3" => (1070, 720, 65, 80), "dungeon" => (975, 490, 65, 65),
            "daily-shop" => (1075, 735, 65, 80), "articles" => (980, 651, 65, 65), _ => (0, 0, 0, 0)
        };
        if (layout is "dungeon" or "daily-teleport")
        {
            var label = await recognition.FindAsync("statistics_resource_label", frame, token);
            if (!label.Found) return new(null, "linha Recurso necessário não confirmada");
            region = (label.X + 145, label.Y - 25, 85, 50);
        }
        if (region.Item3 == 0) return new(null, "layout sem referência");
        var coinReference = layout.StartsWith("ta", StringComparison.Ordinal) ? "statistics_ta_gold_coin" :
            layout == "daily-shop" ? "statistics_shop_gold_coin" : "statistics_gold_coin";
        var coin = await recognition.FindAsync(coinReference, frame,
            region.Item1, region.Item2, region.Item3, region.Item4, token);
        if (!coin.Found) return new(null, $"moeda de ouro não confirmada: {coin.Confidence:F3}");
        return await ReadCropAsync(frame, coin.X + 18, coin.Y - 17, 132, 34, token);
    }

    // Only tightly cropped numeric prices from a known, gold-only screen.
    // Never parse balances, item quantities or arbitrary full-screen numbers.
    internal static long? ParseConsensus(string text)
    {
        var values = new List<long>();
        foreach (var variant in text.Split('|'))
        {
            var value = variant.Trim();
            if (value.Length == 0) continue;
            if (!Regex.IsMatch(value, @"^(?:[1-9]\d{0,2}(?:[.,]\d{3})+|[1-9]\d{0,8})$")) continue;
            if (!long.TryParse(value.Replace(".", "").Replace(",", ""), out var number) || number > 100_000_000)
                return null;
            values.Add(number);
        }
        return values.Count >= 2 && values.All(value => value == values[0]) ? values[0] : null;
    }

    internal static async Task<GoldPriceReading> ReadCropAsync(PixelFrame frame,
        int x, int y, int width, int height, CancellationToken token)
    {
        if (frame.NativeContent is { } native && frame.Viewport is { } viewport && viewport.Width < 1920)
        {
            // OCR directly from original pixels: normalizing and then enlarging
            // again blurs small digits. Keep the canonical crop for coordinates only.
            var nativeX = Math.Max(0, (int)Math.Floor(x * viewport.ScaleX));
            var nativeY = Math.Max(0, (int)Math.Floor((y - ReferenceViewport.TitleHeight) * viewport.ScaleY));
            var nativeRight = Math.Min(native.Width, (int)Math.Ceiling((x + width) * viewport.ScaleX));
            var nativeBottom = Math.Min(native.Height, (int)Math.Ceiling((y + height - ReferenceViewport.TitleHeight) * viewport.ScaleY));
            return await ReadCropAsync(native, nativeX, nativeY, nativeRight - nativeX, nativeBottom - nativeY, token);
        }
        if (x < 0 || y < 0 || x + width > frame.Width || y + height > frame.Height)
            return new(null, "região indisponível");
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
            Buffer.BlockCopy(frame.Pixels, (y + row) * frame.Stride + x * 4, pixels, row * width * 4, width * 4);
        var raw = (await new RestorationCounterReader().ReadHeaderCropAsync(
            new(width, height, width * 4, pixels), token)).RawText;
        return new(ParseConsensus(raw), $"price_crop={x},{y},{width},{height}; ocr={raw}");
    }
}
