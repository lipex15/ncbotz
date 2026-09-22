using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace BotNC.App.Services;

public sealed record DailyShopStatus(bool Exhausted, string Evidence);

// Lê os contadores diários dos cartões. A decisão não depende do nome do item,
// que pode variar por idioma, evento ou conta.
public sealed class DailyShopStatusReader
{
    private static readonly Regex LimitCounter = new(
        @"(?<!\d)(?<used>\d{1,2})\s*[/\\]\s*(?<limit>\d{1,2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Task<DailyShopStatus> ReadCommonAsync(PixelFrame frame, CancellationToken cancellationToken) =>
        ReadAsync(frame, 245, 175, 400, 420, minimumCompletedCounters: 1, cancellationToken);

    public Task<DailyShopStatus> ReadSummonAsync(PixelFrame frame, CancellationToken cancellationToken) =>
        ReadAsync(frame, 240, 175, 1050, 690, minimumCompletedCounters: 3, cancellationToken);

    private static async Task<DailyShopStatus> ReadAsync(
        PixelFrame frame,
        int referenceX,
        int referenceY,
        int referenceWidth,
        int referenceHeight,
        int minimumCompletedCounters,
        CancellationToken cancellationToken)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ??
                     OcrEngine.TryCreateFromLanguage(new Language("pt-BR")) ??
                     OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        if (engine is null)
            return new DailyShopStatus(false, "OCR indisponível");

        var croppedReference = frame.Width < 1500;
        var x0 = croppedReference ? 0 : (int)Math.Round(referenceX * frame.Width / 1920d);
        var y0 = croppedReference ? 0 : (int)Math.Round(referenceY * frame.Height / 1040d);
        var sourceWidth = croppedReference
            ? frame.Width
            : Math.Min(frame.Width - x0, (int)Math.Round(referenceWidth * frame.Width / 1920d));
        var sourceHeight = croppedReference
            ? frame.Height
            : Math.Min(frame.Height - y0, (int)Math.Round(referenceHeight * frame.Height / 1040d));
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return new DailyShopStatus(false, "região indisponível");

        var observations = new List<string>();
        foreach (var threshold in new int?[] { null, 105, 145 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            const int scale = 3;
            var width = sourceWidth * scale;
            var height = sourceHeight * scale;
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
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
            }

            using var bitmap = new SoftwareBitmap(
                BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
            bitmap.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray(pixels));
            var text = (await engine.RecognizeAsync(bitmap).AsTask(cancellationToken)).Text
                .Replace('O', '0').Replace('o', '0');
            observations.Add(text);
            var completed = LimitCounter.Matches(text)
                .Select(match => (
                    Used: int.TryParse(match.Groups["used"].Value, out var used) ? used : -1,
                    Limit: int.TryParse(match.Groups["limit"].Value, out var limit) ? limit : -1))
                .Count(counter => counter.Limit > 0 && counter.Used >= counter.Limit);
            if (completed >= minimumCompletedCounters)
                return new DailyShopStatus(true, string.Join(" | ", observations));
        }

        return new DailyShopStatus(false, string.Join(" | ", observations));
    }
}
