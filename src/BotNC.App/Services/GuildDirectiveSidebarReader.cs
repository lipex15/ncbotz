using System.Text;
using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace BotNC.App.Services;

public enum GuildDirectiveSidebarState
{
    Unknown,
    Available,
    Active,
    NoGreenDirective
}

public sealed record GuildDirectiveSidebarResult(
    GuildDirectiveSidebarState State,
    string Evidence,
    int GreenPixels,
    int GenericCounters);

// A linha lateral é usada como sinal rápido, sem depender do nome sorteado
// para a Diretiva. O contador da linha ativa é invariável o bastante para
// distinguir a mecânica normal (500) das variações de evento (50/55).
public sealed class GuildDirectiveSidebarReader
{
    private static readonly Regex ActiveCounter = new(
        @"(?<!\d)\d{1,3}\s*[/\\]\s*(?:500|50|55)(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GenericCounter = new(
        @"(?<!\d)\d{1,3}\s*[/\\]\s*\d{1,3}(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<GuildDirectiveSidebarResult> ReadAsync(
        PixelFrame frame,
        CancellationToken cancellationToken)
    {
        var croppedInput = frame.Width < 1500;
        var x0 = croppedInput ? 0 : Scale(1280, frame.Width, 1920);
        var y0 = croppedInput ? 0 : Scale(95, frame.Height, 1040);
        var width = croppedInput ? frame.Width : Math.Min(frame.Width - x0, Scale(620, frame.Width, 1920));
        var height = croppedInput ? frame.Height : Math.Min(frame.Height - y0, Scale(610, frame.Height, 1040));
        if (width <= 0 || height <= 0)
            return new GuildDirectiveSidebarResult(GuildDirectiveSidebarState.Unknown, "região indisponível", 0, 0);

        var greenPixels = 0;
        var greenByRow = new int[height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var source = (y0 + y) * frame.Stride + (x0 + x) * 4;
                var blue = frame.Pixels[source];
                var green = frame.Pixels[source + 1];
                var red = frame.Pixels[source + 2];
                if (green >= 78 && green >= red + 17 && green + 10 >= blue)
                {
                    greenPixels++;
                    greenByRow[y]++;
                }
            }
        }

        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ??
                     OcrEngine.TryCreateFromLanguage(new Language("pt-BR")) ??
                     OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        if (engine is null)
            return new GuildDirectiveSidebarResult(GuildDirectiveSidebarState.Unknown, "OCR indisponível", greenPixels, 0);

        var observations = new List<string>();
        var activeCounterSeen = false;
        var genericCounters = 0;
        foreach (var threshold in new int?[] { null, 105, 145 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bitmap = CreateOcrBitmap(frame, x0, y0, width, height, threshold);
            using (bitmap)
            {
                var text = (await engine.RecognizeAsync(bitmap).AsTask(cancellationToken)).Text;
                observations.Add(text);
                var normalized = Normalize(text);
                activeCounterSeen |= ActiveCounter.IsMatch(normalized);
                genericCounters = Math.Max(genericCounters, GenericCounter.Matches(normalized).Count);
                if (normalized.Contains("DIRETIVA", StringComparison.Ordinal) &&
                    (normalized.Contains("DISPON", StringComparison.Ordinal) ||
                     normalized.Contains("DISP0N", StringComparison.Ordinal) ||
                     normalized.Contains("GUILDA", StringComparison.Ordinal)))
                {
                    return new GuildDirectiveSidebarResult(
                        GuildDirectiveSidebarState.Available,
                        string.Join(" | ", observations),
                        greenPixels,
                        genericCounters);
                }
            }
        }

        // Texto verde junto do contador especial significa que a Diretiva já
        // ocupa uma linha normal da lista. O nome da missão é deliberadamente
        // ignorado.
        if (activeCounterSeen && greenPixels >= Math.Max(35, width / 8))
        {
            return new GuildDirectiveSidebarResult(
                GuildDirectiveSidebarState.Active,
                string.Join(" | ", observations),
                greenPixels,
                genericCounters);
        }

        // A faixa selecionada de "disponível" possui muito mais área verde
        // que um texto comum. Isso cobre uma falha eventual do OCR da frase.
        var strongRows = greenByRow.Count(value => value >= Math.Max(35, width / 10));
        if (greenPixels >= Math.Max(1200, width * 3) && strongRows >= 14)
        {
            return new GuildDirectiveSidebarResult(
                GuildDirectiveSidebarState.Available,
                string.Join(" | ", observations),
                greenPixels,
                genericCounters);
        }

        // Só devolvemos ausência quando a lista lateral foi realmente
        // observável (outros contadores existem). O painel da Guilda ainda será
        // a confirmação final antes de considerar as Diretivas concluídas.
        var state = genericCounters >= 1 && greenPixels < Math.Max(35, width / 8)
            ? GuildDirectiveSidebarState.NoGreenDirective
            : GuildDirectiveSidebarState.Unknown;
        return new GuildDirectiveSidebarResult(
            state,
            string.Join(" | ", observations),
            greenPixels,
            genericCounters);
    }

    private static SoftwareBitmap CreateOcrBitmap(
        PixelFrame frame,
        int x0,
        int y0,
        int sourceWidth,
        int sourceHeight,
        int? threshold)
    {
        const int scale = 2;
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

        var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray(pixels));
        return bitmap;
    }

    private static string Normalize(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) ==
                System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            result.Append(char.ToUpperInvariant(character switch
            {
                'O' or 'o' => '0',
                _ => character
            }));
        }
        return result.ToString();
    }

    private static int Scale(int value, int actual, int reference) =>
        (int)Math.Round(value * actual / (double)reference);
}
