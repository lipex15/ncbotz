using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace BotNC.App.Services;

public sealed class GuildDonationCounterReader
{
    private static readonly Regex Counter = new(@"Doa[cç][oõ]es\s*:\s*([0-3])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public async Task<int?> ReadAsync(PixelFrame frame, CancellationToken cancellationToken)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ??
                     OcrEngine.TryCreateFromLanguage(new Language("pt-BR"));
        if (engine is null) return null;

        var x0 = (int)Math.Round(565d * frame.Width / 1920);
        var y0 = (int)Math.Round(358d * frame.Height / 1040);
        var sourceWidth = Math.Min(frame.Width - x0, (int)Math.Round(170d * frame.Width / 1920));
        var sourceHeight = Math.Min(frame.Height - y0, (int)Math.Round(78d * frame.Height / 1040));
        if (sourceWidth <= 0 || sourceHeight <= 0) return null;

        const int scale = 4;
        var width = sourceWidth * scale;
        var height = sourceHeight * scale;
        foreach (var threshold in new int?[] { null, 115, 150 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var source = (y0 + y / scale) * frame.Stride + (x0 + x / scale) * 4;
                var destination = (y * width + x) * 4;
                var blue = frame.Pixels[source];
                var green = frame.Pixels[source + 1];
                var red = frame.Pixels[source + 2];
                var luma = (red * 77 + green * 150 + blue * 29) >> 8;
                var value = threshold is null ? (byte)luma : luma >= threshold ? (byte)255 : (byte)0;
                pixels[destination] = value;
                pixels[destination + 1] = value;
                pixels[destination + 2] = value;
                pixels[destination + 3] = 255;
            }

            using var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
            bitmap.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray(pixels));
            var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
            var match = Counter.Match(result.Text.Replace('\n', ' '));
            if (match.Success && int.TryParse(match.Groups[1].Value, out var count)) return count;
        }

        return null;
    }
}
