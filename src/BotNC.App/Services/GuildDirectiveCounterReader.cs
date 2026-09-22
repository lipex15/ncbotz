using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace BotNC.App.Services;

// O contador fica no rodapé da aba Diretiva. Ele não depende do nome da missão
// nem de existir um consumível de recarga no inventário.
public sealed class GuildDirectiveCounterReader
{
    private static readonly Regex Completed = new(@"(?<!\d)5\s*[/\\]\s*5(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<bool> IsCompleteAsync(PixelFrame frame, CancellationToken cancellationToken)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ??
                     OcrEngine.TryCreateFromLanguage(new Language("pt-BR")) ??
                     OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        if (engine is null)
        {
            return false;
        }

        // Coordenadas da área cliente 1920×1040; o contador 5/5 está em
        // aproximadamente (690, 957), abaixo do conteúdo da Diretiva.
        var x0 = (int)Math.Round(635d * frame.Width / 1920);
        var y0 = (int)Math.Round(934d * frame.Height / 1040);
        var sourceWidth = Math.Min(frame.Width - x0, (int)Math.Round(120d * frame.Width / 1920));
        var sourceHeight = Math.Min(frame.Height - y0, (int)Math.Round(50d * frame.Height / 1040));
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return false;
        }

        const int scale = 4;
        var width = sourceWidth * scale;
        var height = sourceHeight * scale;
        foreach (var threshold in new int?[] { null, 105, 145 })
        {
            cancellationToken.ThrowIfCancellationRequested();
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

            using var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
            bitmap.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray(pixels));
            var text = (await engine.RecognizeAsync(bitmap).AsTask(cancellationToken)).Text;
            if (Completed.IsMatch(text))
            {
                return true;
            }
        }

        return false;
    }
}
