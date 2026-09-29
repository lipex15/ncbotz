namespace BotNC.App.Services;

internal enum SkillSixState { Unknown, Active, Inactive }

internal static class SkillSixReader
{
    internal static async Task<SkillSixState> ReadAsync(VisualRecognitionService visual, PixelFrame source,
        CancellationToken token, Action<string>? trace = null)
    {
        var frame = VisualRecognitionService.NormalizeForReferenceMatching(source);
        var on = await visual.FindAsync("reconnect_skill_on", frame, token);
        var off = await visual.FindAsync("reconnect_skill_off", frame, token);
        var compact = false;
        if (!on.Found && !off.Found)
        {
            on = await visual.FindAsync("skill6_compact_on", frame, token);
            off = await visual.FindAsync("skill6_compact_off", frame, token);
            compact = true;
        }
        if (!on.Found && !off.Found)
        {
            trace?.Invoke($"skill6_evidence state=Unknown; reason=icon_not_located; on={on.Confidence:F3}; off={off.Confidence:F3}");
            return SkillSixState.Unknown;
        }
        var cx = off.Found ? off.X : on.X;
        var cy = off.Found ? off.Y : on.Y;
        var sides = new double[4];
        bool White(int x, int y)
        {
            if (x < 0 || y < 0 || x >= frame.Width || y >= frame.Height) return false;
            var i = y * frame.Stride + x * 4;
            var b = frame.Pixels[i]; var g = frame.Pixels[i + 1]; var r = frame.Pixels[i + 2];
            return Math.Min(r, Math.Min(g, b)) >= 175 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) < 65;
        }
        // Evaluate the border separately from the purple drawing shared by both states.
        for (var distance = compact ? 29 : 31; distance <= (compact ? 35 : 43); distance++)
        {
            var counts = new int[4];
            for (var along = -24; along <= 24; along++)
            {
                if (White(cx + along, cy - distance)) counts[0]++;
                if (White(cx + along, cy + distance)) counts[1]++;
                if (White(cx - distance, cy + along)) counts[2]++;
                if (White(cx + distance, cy + along)) counts[3]++;
            }
            for (var side = 0; side < 4; side++) sides[side] = Math.Max(sides[side], counts[side] / 49d);
        }
        var activeBorder = sides.Count(s => s >= .45) >= 2 && sides.Count(s => s >= .25) >= 3;
        var clearlyUnlit = sides.All(s => s < .25);
        var state = (!compact && on.Found) || activeBorder ? SkillSixState.Active :
            off.Found && clearlyUnlit ? SkillSixState.Inactive : SkillSixState.Unknown;
        trace?.Invoke($"skill6_evidence state={state}; on={on.Confidence:F3}; off={off.Confidence:F3}; border={string.Join(",", sides.Select(s => s.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))}; xy={cx},{cy}");
        return state;
    }
}
