using System.Globalization;
using System.Text.RegularExpressions;
using BotNC.App.Models;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace BotNC.App.Services;

public sealed record AbbeyTimeReading(TimeSpan? Remaining, string Text);

public sealed class AbbeyTimeReader
{
    // Região do contador abaixo do minimapa, relativa à área de jogo 1920×1040.
    private const int ReferenceWidth = 1920;
    private const int ReferenceHeight = 1040;
    private static readonly Regex TimePattern = new(
        @"(?<hours>\d{1,2})\s*[hH]\s*(?<minutes>\d{1,2})\s*(?:min|m)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public Task<AbbeyTimeReading> ReadAsync(PixelFrame frame, CancellationToken cancellationToken) =>
        ReadRegionAsync(frame, 75, 264, 180, 75, cancellationToken);

    public Task<AbbeyTimeReading> ReadMenuAsync(PixelFrame frame, CancellationToken cancellationToken) =>
        ReadRegionAsync(frame, 75, 535, 210, 85, cancellationToken);

    private async Task<AbbeyTimeReading> ReadRegionAsync(
        PixelFrame frame,
        int referenceX,
        int referenceY,
        int referenceWidth,
        int referenceHeight,
        CancellationToken cancellationToken)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ??
                     OcrEngine.TryCreateFromLanguage(new Language("pt-BR")) ??
                     OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        if (engine is null)
        {
            return new AbbeyTimeReading(null, "OCR indisponível");
        }

        var sourceX = (int)Math.Round((double)referenceX * frame.Width / ReferenceWidth);
        var sourceY = (int)Math.Round((double)referenceY * frame.Height / ReferenceHeight);
        var sourceWidth = Math.Min(frame.Width - sourceX, (int)Math.Round((double)referenceWidth * frame.Width / ReferenceWidth));
        var sourceHeight = Math.Min(frame.Height - sourceY, (int)Math.Round((double)referenceHeight * frame.Height / ReferenceHeight));
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return new AbbeyTimeReading(null, "região indisponível");
        }

        var observations = new List<string>();
        foreach (var highContrast in new[] { false, true })
        {
            cancellationToken.ThrowIfCancellationRequested();
            const int scale = 3;
            var width = sourceWidth * scale;
            var height = sourceHeight * scale;
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                var sourceRow = (sourceY + y / scale) * frame.Stride;
                for (var x = 0; x < width; x++)
                {
                    var sourceIndex = sourceRow + (sourceX + x / scale) * 4;
                    var destinationIndex = (y * width + x) * 4;
                    var blue = frame.Pixels[sourceIndex];
                    var green = frame.Pixels[sourceIndex + 1];
                    var red = frame.Pixels[sourceIndex + 2];
                    if (highContrast)
                    {
                        var brightness = (red * 30 + green * 59 + blue * 11) / 100;
                        var value = brightness >= 120 ? (byte)255 : (byte)0;
                        blue = green = red = value;
                    }

                    pixels[destinationIndex] = blue;
                    pixels[destinationIndex + 1] = green;
                    pixels[destinationIndex + 2] = red;
                    pixels[destinationIndex + 3] = 255;
                }
            }

            using var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
            bitmap.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray(pixels));
            var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
            var text = result.Text.Replace('O', '0').Replace('o', '0');
            observations.Add(text);
            var match = TimePattern.Match(text);
            if (match.Success &&
                int.TryParse(match.Groups["hours"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hours) &&
                int.TryParse(match.Groups["minutes"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) &&
                hours <= 10 && minutes < 60)
            {
                return new AbbeyTimeReading(TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes),
                    string.Join(" | ", observations));
            }
        }

        return new AbbeyTimeReading(null, string.Join(" | ", observations));
    }
}
