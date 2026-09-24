using System.Text.RegularExpressions;

namespace BotNC.App.Services;

internal enum GuildTreasureState { Unknown, Empty, Available, Reward }
internal sealed record GuildTreasureReading(GuildTreasureState State, int? Count, string Evidence);

internal static class GuildTreasureReader
{
    internal static int? ParseCount(string text)
    {
        var values = Regex.Matches(text, @"(?:QNTD|QTD)\.?\s*(?:DE\s*)?BAU\s*:?\s*(\d{1,4})\b")
            .Select(match => int.Parse(match.Groups[1].Value)).Distinct().ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    internal static async Task<GuildTreasureReading> ReadAsync(VisualRecognitionService recognition,
        PixelFrame source, CancellationToken token)
    {
        var frame = VisualRecognitionService.NormalizeForReferenceMatching(source);
        if ((await recognition.FindAsync("guild_treasure_reward", frame, token)).Found)
            return new(GuildTreasureState.Reward, null, "Item Obtido");
        if (!(await recognition.FindAsync("guild_treasure_panel", frame, token)).Found)
            return new(GuildTreasureState.Unknown, null, "painel não visível");
        var crop = new byte[300 * 70 * 4];
        if (frame.Width < 1740 || frame.Height < 390) return new(GuildTreasureState.Unknown, null, "quadro inválido");
        for (var y = 0; y < 70; y++)
            Buffer.BlockCopy(frame.Pixels, (320 + y) * frame.Stride + 1440 * 4, crop, y * 300 * 4, 300 * 4);
        var text = (await new RestorationCounterReader().ReadHeaderCropAsync(new(300, 70, 1200, crop), token)).RawText;
        var count = ParseCount(text);
        var empty = await recognition.FindAsync("guild_treasure_empty", frame, token);
        var button = await recognition.FindAsync("guild_treasure_open", frame, token);
        // Do not inspect the chest graphic or item name: both can change.
        var red = 0;
        for (var y = 916; y < Math.Min(frame.Height, 935); y++)
        for (var x = 1698; x < Math.Min(frame.Width, 1723); x++)
        {
            var offset = y * frame.Stride + x * 4;
            if (frame.Pixels[offset + 2] > 120 && frame.Pixels[offset + 2] > frame.Pixels[offset + 1] * 1.4 &&
                frame.Pixels[offset + 2] > frame.Pixels[offset] * 1.4) red++;
        }
        var state = count == 0 && empty.Found ? GuildTreasureState.Empty :
            count > 0 && button.Found && red >= 12 && !empty.Found ? GuildTreasureState.Available : GuildTreasureState.Unknown;
        return new(state, count, $"count={count}; open={button.Confidence:F3}; empty={empty.Confidence:F3}; red={red}; ocr={text}");
    }
}
