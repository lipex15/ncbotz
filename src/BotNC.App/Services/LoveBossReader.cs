using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace BotNC.App.Services;

public sealed record LoveBossMissionStatus(int? DailyCompleted, int? WeeklyCompleted, string Evidence);

public enum LoveBossPhase
{
    Unknown,
    AwaitingSpawn,
    Fighting,
    Leaving
}

// OCR sempre limitado ao HUD e aos contadores da missão. Nomes de jogadores
// no centro da arena nunca entram na decisão de estado.
public sealed class LoveBossReader
{
    private static readonly Regex Counter = new(
        @"(?<!\d)(?<done>[0-5])\s*[/\\]\s*(?<total>[15])(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Time = new(
        @"(?<!\d)(?<minutes>\d{1,2})\s*min",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public async Task<LoveBossMissionStatus> ReadMissionAsync(
        PixelFrame frame, CancellationToken cancellationToken)
    {
        var daily = await ReadRegionAsync(frame, 1380, 398, 75, 61, cancellationToken);
        var weekly = await ReadRegionAsync(frame, 1380, 623, 75, 62, cancellationToken);
        return new LoveBossMissionStatus(
            ParseCounter(daily, 1), ParseCounter(weekly, 5),
            $"diária='{daily}' semanal='{weekly}'");
    }

    public async Task<(LoveBossPhase Phase, int? Minutes, string Evidence)> ReadPhaseAsync(
        PixelFrame frame, CancellationToken cancellationToken)
    {
        var text = await ReadRegionAsync(frame, 12, 288, 313, 53, cancellationToken);
        var normalized = text.ToLowerInvariant();
        var minutes = Time.Match(normalized) is { Success: true } match &&
                      int.TryParse(match.Groups["minutes"].Value, out var value)
            ? value : (int?)null;
        if (normalized.Contains("saída automática") || normalized.Contains("saida automatica") ||
            normalized.Contains("até a saída") || normalized.Contains("ate a saida"))
            return (LoveBossPhase.Leaving, minutes, text);
        if (normalized.Contains("manifestação") || normalized.Contains("manifestacao"))
            return (LoveBossPhase.AwaitingSpawn, minutes, text);
        if (minutes is >= 0 and <= 30)
            return (LoveBossPhase.Fighting, minutes, text);
        return (LoveBossPhase.Unknown, minutes, text);
    }

    private static int? ParseCounter(string text, int total)
    {
        foreach (Match match in Counter.Matches(text.Replace('O', '0').Replace('o', '0')))
        {
            if (int.TryParse(match.Groups["total"].Value, out var readTotal) &&
                readTotal == total &&
                int.TryParse(match.Groups["done"].Value, out var done))
                return done;
        }
        return null;
    }

    private static async Task<string> ReadRegionAsync(
        PixelFrame frame, int referenceX, int referenceY, int referenceWidth,
        int referenceHeight, CancellationToken cancellationToken)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ??
                     OcrEngine.TryCreateFromLanguage(new Language("pt-BR")) ??
                     OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        if (engine is null) return "OCR indisponível";

        var x0 = (int)Math.Round(referenceX * frame.Width / 1920d);
        var y0 = (int)Math.Round(referenceY * frame.Height / 1080d);
        var sourceWidth = Math.Min(frame.Width - x0,
            (int)Math.Round(referenceWidth * frame.Width / 1920d));
        var sourceHeight = Math.Min(frame.Height - y0,
            (int)Math.Round(referenceHeight * frame.Height / 1080d));
        if (sourceWidth <= 0 || sourceHeight <= 0) return "região indisponível";

        var observations = new List<string>();
        foreach (var threshold in new int?[] { null, 105, 145 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            const int scale = 4;
            var width = sourceWidth * scale;
            var height = sourceHeight * scale;
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
                var value = threshold is null ? (byte)luma :
                    luma >= threshold ? (byte)255 : (byte)0;
                pixels[destination] = value;
                pixels[destination + 1] = value;
                pixels[destination + 2] = value;
                pixels[destination + 3] = 255;
            }
            using var bitmap = new SoftwareBitmap(
                BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
            bitmap.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray(pixels));
            var text = (await engine.RecognizeAsync(bitmap).AsTask(cancellationToken)).Text;
            observations.Add(text);
        }
        // Uma das versões pode ler melhor texto brilhante; preservar todas para
        // não descartar o único contador reconhecido.
        return string.Join(" | ", observations);
    }
}
