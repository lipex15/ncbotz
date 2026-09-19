using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BotNC.App.Models;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace BotNC.App.Services;

public sealed record SpotLevelRecognitionResult(int? Level, string Text);

public sealed partial class SpotLevelRecognitionService
{
    private const int ReferenceWidth = 1920;
    private const int ReferenceHeight = 1040;
    private static readonly int[] KnownLevels = [68, 72, 76, 80, 84, 88, 90, 92, 94, 96, 98, 100];

    public async Task<SpotLevelRecognitionResult> RecognizeFirstFavoriteAsync(
        PixelFrame frame,
        IReadOnlySet<int> allowedLevels,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var crop = CropFirstFavorite(frame);
        using var bitmap = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            crop.Width,
            crop.Height,
            BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray(crop.Pixels));

        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ??
                     OcrEngine.TryCreateFromLanguage(new Language("en-US")) ??
                     throw new InvalidOperationException("O reconhecimento de texto do Windows não está disponível.");
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
        var normalized = NormalizeOcrText(result.Text);
        var level = ExtractLevel(normalized, allowedLevels);
        return new SpotLevelRecognitionResult(level, normalized);
    }

    public async Task<SpotLevelRecognitionResult> RecognizeFirstFavoriteInImageAsync(
        string imagePath,
        IReadOnlySet<int> allowedLevels,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(imagePath);
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(
            decoder.Frames[0],
            PixelFormats.Bgra32,
            null,
            0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return await RecognizeFirstFavoriteAsync(
            new PixelFrame(converted.PixelWidth, converted.PixelHeight, stride, pixels),
            allowedLevels,
            cancellationToken);
    }

    private static PixelFrame CropFirstFavorite(PixelFrame frame)
    {
        var scaleX = frame.Width / (double)ReferenceWidth;
        var scaleY = frame.Height / (double)ReferenceHeight;
        var sourceX = Math.Clamp((int)Math.Round(1535 * scaleX), 0, frame.Width - 1);
        var sourceY = Math.Clamp((int)Math.Round(195 * scaleY), 0, frame.Height - 1);
        var sourceWidth = Math.Clamp((int)Math.Round(330 * scaleX), 1, frame.Width - sourceX);
        var sourceHeight = Math.Clamp((int)Math.Round(145 * scaleY), 1, frame.Height - sourceY);
        const int enlargement = 3;
        var width = sourceWidth * enlargement;
        var height = sourceHeight * enlargement;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            var originalY = sourceY + (y / enlargement);
            for (var x = 0; x < width; x++)
            {
                var originalX = sourceX + (x / enlargement);
                var sourceOffset = (originalY * frame.Stride) + (originalX * 4);
                var destinationOffset = (y * stride) + (x * 4);
                var blue = frame.Pixels[sourceOffset];
                var green = frame.Pixels[sourceOffset + 1];
                var red = frame.Pixels[sourceOffset + 2];
                var luma = ((red * 77) + (green * 150) + (blue * 29)) >> 8;
                var value = luma >= 108 ? (byte)255 : (byte)0;
                pixels[destinationOffset] = value;
                pixels[destinationOffset + 1] = value;
                pixels[destinationOffset + 2] = value;
                pixels[destinationOffset + 3] = 255;
            }
        }

        return new PixelFrame(width, height, stride, pixels);
    }

    private static int? ExtractLevel(string text, IReadOnlySet<int> allowedLevels)
    {
        foreach (Match match in NumberPattern().Matches(text))
        {
            if (int.TryParse(match.Value, out var value) &&
                KnownLevels.Contains(value) &&
                allowedLevels.Contains(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string NormalizeOcrText(string text) =>
        text.Replace('\r', ' ').Replace('\n', ' ').Trim();

    [GeneratedRegex(@"\b(?:68|72|76|80|84|88|90|92|94|96|98|100)\b", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();
}
