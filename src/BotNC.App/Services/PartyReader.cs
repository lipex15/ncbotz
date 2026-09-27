using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace BotNC.App.Services;

internal static class PartyReader
{
    internal static int HudCards(PixelFrame source)
    {
        var frame = VisualRecognitionService.NormalizeForReferenceMatching(source);
        var count = 0;
        var start = -1;
        var last = -10;
        for (var x = 520; x < Math.Min(1370, frame.Width); x++)
        {
            var blue = 0;
            for (var y = 850; y < Math.Min(915, frame.Height); y++)
            {
                var i = y * frame.Stride + x * 4;
                var b = frame.Pixels[i]; var g = frame.Pixels[i + 1]; var r = frame.Pixels[i + 2];
                if (b > 30 && g > 30 && b > r * 1.35 && g > r * 1.25) blue++;
            }
            if (blue < 6) continue;
            if (x - last > 4)
            {
                if (start >= 0 && last - start is >= 5 and <= 28) count++;
                start = x;
            }
            last = x;
        }
        if (start >= 0 && last - start is >= 5 and <= 28) count++;
        return Math.Min(4, count);
    }

    internal static async Task<bool> HasMemberCardAsync(VisualRecognitionService visual, PixelFrame frame, CancellationToken token) =>
        HudCards(frame) > 0 && (await visual.FindAsync("party_member_card", frame, token)).Found;

    internal static async Task<bool> HasInvitationAsync(VisualRecognitionService visual, PixelFrame frame, CancellationToken token) =>
        (await visual.FindAsync("party_received_title", frame, token)).Found &&
        (await visual.FindAsync("party_received_question", frame, token)).Found &&
        (await visual.FindAsync("party_received_ok", frame, token)).Found;

    internal static async Task<string> TextAsync(PixelFrame source, int x, int y, int width, int height, CancellationToken token)
    {
        var frame = VisualRecognitionService.NormalizeForReferenceMatching(source);
        if (x + width > frame.Width || y + height > frame.Height) return "";
        // Preserve Unicode and accents: the restoration reader intentionally
        // normalizes its output, which is not suitable for player identity.
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
            Buffer.BlockCopy(frame.Pixels, (y + row) * frame.Stride + x * 4, pixels, row * width * 4, width * 4);
        using var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Ignore);
        bitmap.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray(pixels));
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        if (engine is null) return "";
        var result = await engine.RecognizeAsync(bitmap).AsTask(token);
        return string.Join("\n", result.Lines.Select(line => line.Text));
    }

    internal static int[] MemberRows(PixelFrame source)
    {
        var frame = VisualRecognitionService.NormalizeForReferenceMatching(source);
        var rows = new List<int>();
        var start = -1;
        var last = -10;
        for (var y = 180; y < Math.Min(660, frame.Height); y++)
        {
            var blue = 0;
            for (var x = 27; x < Math.Min(63, frame.Width); x++)
            {
                var i = y * frame.Stride + x * 4;
                var b = frame.Pixels[i]; var g = frame.Pixels[i + 1]; var r = frame.Pixels[i + 2];
                if (b > 65 && g > 65 && b > r * 1.35 && g > r * 1.25) blue++;
            }
            if (blue < 5) continue;
            if (y - last > 6)
            {
                if (start >= 0 && last - start >= 5) rows.Add((start + last) / 2);
                start = y;
            }
            last = y;
        }
        if (start >= 0 && last - start >= 5) rows.Add((start + last) / 2);
        return rows.Take(5).ToArray();
    }
}
