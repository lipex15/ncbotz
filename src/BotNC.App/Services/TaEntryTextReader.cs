using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace BotNC.App.Services;

// Lê somente o comando estável do primeiro cartão, não o nome do mapa.
public sealed class TaEntryTextReader
{
    private static readonly Regex EntryWord = new(@"\bEntrar\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public async Task<bool> HasFirstEntryAsync(PixelFrame frame, CancellationToken cancellationToken)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ??
                     OcrEngine.TryCreateFromLanguage(new Language("pt-BR")) ??
                     OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        if (engine is null)
        {
            return false;
        }

        // Capturas de diagnóstico 1920×1080 incluem 23 px de barra de título;
        // a captura da janela em execução entrega apenas a área cliente.
        var desktopScreenshot = frame.Width is >= 1918 and <= 1922 &&
                                frame.Height is >= 1078 and <= 1082;
        var contentTop = desktopScreenshot ? 23 : 0;
        var contentHeight = desktopScreenshot ? frame.Height - 63 : frame.Height;
        var sourceX = (int)Math.Round(448d * frame.Width / 1920);
        var sourceY = contentTop + (int)Math.Round(720d * contentHeight / 1040);
        var sourceWidth = Math.Min(frame.Width - sourceX, (int)Math.Round(225d * frame.Width / 1920));
        var sourceHeight = Math.Min(frame.Height - sourceY, (int)Math.Round(62d * contentHeight / 1040));
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return false;
        }

        const int scale = 3;
        var width = sourceWidth * scale;
        var height = sourceHeight * scale;
        foreach (var threshold in new int?[] { null, 110, 145 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var source = (sourceY + y / scale) * frame.Stride + (sourceX + x / scale) * 4;
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

            using var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
            bitmap.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray(pixels));
            var text = (await engine.RecognizeAsync(bitmap).AsTask(cancellationToken)).Text;
            if (EntryWord.IsMatch(text))
            {
                return true;
            }
        }

        return false;
    }
}
