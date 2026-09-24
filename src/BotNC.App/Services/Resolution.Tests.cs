using System.IO;

namespace BotNC.App.Services;

internal static class ResolutionRegression
{
    internal static async Task VerifyAsync(string output, VisualRecognitionService recognition,
        AppDatabase database, Func<string, PixelFrame> load)
    {
        if (!AppIdentity.IsTesting) return;
        var report = new List<string>();
        async Task Log(string line) { report.Add(line); await File.WriteAllLinesAsync(output, report); }
        foreach (var (width, desktopHeight) in new[] { (1920, 1080), (1366, 768), (1440, 900) })
        {
            var viewport = new ReferenceViewport(17, 46, width, desktopHeight - 63);
            foreach (var (x, y) in new[] { (0, 23), (1918, 1038), (956, 443), (1627, 942), (1535, 63), (540, 732) })
            {
                var mapped = viewport.ToScreen(x, y);
                var back = viewport.ToReference(mapped.X, mapped.Y);
                if (Math.Abs(back.X - x) > 1 || Math.Abs(back.Y - y) > 1)
                    throw new Exception($"Coordinate round-trip failed: {viewport}; {x},{y} => {back}");
            }
            if (!ReferenceViewport.IsSupportedDisplay(width, desktopHeight)) throw new Exception("Supported resolution rejected.");
            await Log($"geometry {width}x{desktopHeight}; roundTrip=true; movedWindow=true; customPoints=true");

            foreach (var (file, id) in new[]
            {
                ("morte_confirmada.png", "morte_ressuscitar"),
                ("morte_confirmada.png", "morte_titulo"),
                ("perda_exp.png", "lapide_nucleo"),
                ("daily_automatic.png", "daily_automatic"),
                ("daily_page.png", "daily_page"),
                ("guild_directive_page.png", "guild_directive_accept_button"),
                ("guild_directive_in_progress.png", "guild_directive_decline_button"),
                ("guild_treasure_available.png", "guild_treasure_open"),
                ("guild_treasure_empty.png", "guild_treasure_empty"),
                ("guild_treasure_reward.png", "guild_treasure_reward"),
                ("statistics_article_purchase.png", "statistics_article_title"),
                ("boss_room.png", "game_hud_menu")
            })
            {
                var source = load(file);
                if (source.Width < 1918 || source.Height < 1038)
                {
                    var reference = await database.GetReferenceAsync(id);
                    source = PlaceReference(reference);
                }
                var normalized = Simulate(source, viewport);
                var match = await recognition.FindAsync(id, normalized);
                await Log($"resolution={width}x{desktopHeight}; ref={id}; found={match.Found}; score={match.Confidence:F4}; xy={match.X},{match.Y}");
                if (!match.Found) throw new Exception(report[^1]);
                var actual = viewport.ToScreen(match.X, match.Y);
                if (actual.X < viewport.Left || actual.Y < viewport.Top) throw new Exception("Recognition click left content.");
            }
            foreach (var file in new[] { "ta_selector_user_active.png", "guild_treasure_empty.png", "daily_page.png" })
            {
                var frame = Simulate(load(file), viewport);
                var tombstone = await TombstoneIconReader.ReadAsync(recognition, frame, CancellationToken.None);
                var death = await recognition.FindAsync("morte_ressuscitar", frame);
                await Log($"negative {width}x{desktopHeight}; file={file}; tombstone={tombstone.Found}; death={death.Found}");
                if (tombstone.Found || death.Found) throw new Exception(report[^1]);
            }
            var realTombstone = await TombstoneIconReader.ReadAsync(recognition, Simulate(load("perda_exp.png"), viewport), CancellationToken.None);
            if (!realTombstone.Found) throw new Exception("Scaled tombstone shape/color failed.");
            var daily = Simulate(load("daily_automatic.png"), viewport);
            BotAutomationEngine.VerifyDailyListFixture(daily);
            foreach (var (file, layout, expected) in new[] { ("statistics_article_purchase.png", "articles", 244100L),
                ("anonymous_entry_confirmation.png", "dungeon", 350000L), ("ta_selector_user_active.png", "ta3", 45000L) })
            {
                var price = await GoldPriceReader.ReadAsync(recognition, Simulate(load(file), viewport), layout, CancellationToken.None);
                await Log($"price {width}x{desktopHeight}; layout={layout}; value={price.Value}; {price.Evidence}");
                if (price.Value != expected) throw new Exception(report[^1]);
            }
        }

        // Every registered image, including cropped references, is exercised at
        // both smaller resolutions without asking the user to recreate assets.
        foreach (var (id, _) in AppDatabase.ResolutionFixtures)
        {
            var reference = await database.GetReferenceAsync(id);
            var source = VisualRecognitionService.Decode(reference.Image);
            var full = new PixelFrame(1920, 1040, 7680, new byte[1920 * 1040 * 4]);
            var x = Math.Clamp(reference.SearchX + (reference.SearchWidth - reference.SourceWidth) / 2, 0, 1920 - reference.SourceWidth);
            var y = Math.Clamp(reference.SearchY + (reference.SearchHeight - reference.SourceHeight) / 2, 23, 1040 - reference.SourceHeight);
            for (var row = 0; row < reference.SourceHeight; row++)
                Buffer.BlockCopy(source.Pixels, (row + reference.SourceY) * source.Stride + reference.SourceX * 4,
                    full.Pixels, (row + y) * full.Stride + x * 4, reference.SourceWidth * 4);
            foreach (var (width, height) in new[] { (1366, 705), (1440, 837) })
            {
                var viewport = new ReferenceViewport(0, 23, width, height);
                var result = await recognition.FindAsync(id, Simulate(full, viewport));
                await Log($"all-refs native={width}x{height}; id={id}; found={result.Found}; score={result.Confidence:F4}");
                if (!result.Found) throw new Exception(report[^1]);
                if (Math.Abs(result.X - (x + reference.SourceWidth / 2)) > 4 || Math.Abs(result.Y - (y + reference.SourceHeight / 2)) > 4)
                    throw new Exception($"Located wrong target: {id}; {result.X},{result.Y}; expected={x},{y}");
            }
        }
        foreach (var (width, height) in new[] { (1366, 705), (1440, 837) })
        foreach (var id in new[] { "game_hud_menu", "guild_treasure_open", "statistics_article_title" })
        {
            var reference = await database.GetReferenceAsync(id);
            var image = VisualRecognitionService.Decode(reference.Image);
            var viewport = new ReferenceViewport(0, 23, width, height);
            var uniform = Math.Min(viewport.ScaleX, viewport.ScaleY);
            foreach (var scale in new[] { uniform, 1d })
            {
                var cropped = new PixelFrame(reference.SourceWidth, reference.SourceHeight, reference.SourceWidth * 4,
                    new byte[reference.SourceWidth * reference.SourceHeight * 4]);
                for (var row = 0; row < cropped.Height; row++)
                    Buffer.BlockCopy(image.Pixels, (row + reference.SourceY) * image.Stride + reference.SourceX * 4,
                        cropped.Pixels, row * cropped.Stride, cropped.Stride);
                cropped = ReferenceViewport.Resize(cropped, (int)Math.Round(cropped.Width * scale), (int)Math.Round(cropped.Height * scale));
                var centerX = reference.SearchX + reference.SearchWidth / 2d;
                var centerY = reference.SearchY - 23 + reference.SearchHeight / 2d;
                var ax = centerX < 640 ? 0 : centerX > 1280 ? 1920 : 960;
                var ay = centerY < 339 ? 0 : centerY > 678 ? 1017 : 508.5;
                var x = (int)Math.Round(ax * viewport.ScaleX + (reference.SourceX - ax) * scale);
                var y = (int)Math.Round(ay * viewport.ScaleY + (reference.SourceY - 23 - ay) * scale);
                var native = new PixelFrame(width, height, width * 4, new byte[width * height * 4]);
                for (var row = 0; row < cropped.Height; row++)
                    Buffer.BlockCopy(cropped.Pixels, row * cropped.Stride, native.Pixels, (row + y) * native.Stride + x * 4, cropped.Stride);
                var normalized = viewport.Normalize(native, 0, 0);
                var found = await recognition.FindAsync(id, normalized);
                var actual = viewport.ToScreen(found.X, found.Y);
                await Log($"anchor {width}x{height}; id={id}; scale={scale:F3}; found={found.Found}; score={found.Confidence:F4}; xy={actual}");
                if (!found.Found || Math.Abs(actual.X - (x + cropped.Width / 2)) > 3 || Math.Abs(actual.Y - viewport.Top - (y + cropped.Height / 2)) > 3)
                    throw new Exception(report[^1]);
                var adjusted = normalized.AdjustReferencePoint(reference.SourceX + reference.SourceWidth / 2,
                    reference.SourceY + reference.SourceHeight / 2);
                var clicked = viewport.ToScreen(adjusted.X, adjusted.Y);
                if (clicked.X < x || clicked.X >= x + cropped.Width || clicked.Y < y + viewport.Top || clicked.Y >= y + viewport.Top + cropped.Height)
                    throw new Exception($"Anchor click outside detected button: {id}; click={clicked}");
                if (normalized.AdjustReferencePoint(found.X, found.Y) != (found.X, found.Y))
                    throw new Exception("Detected click transformed twice.");
            }
        }
        var one = new ReferenceViewport(0, 23, 1366, 705);
        var two = new ReferenceViewport(0, 23, 1440, 837);
        if (one.ToScreen(957, 440) == two.ToScreen(957, 440) || ReferenceViewport.IsSupportedDisplay(1280, 720))
            throw new Exception("Client isolation or supported-resolution gate failed.");
        await Log("clientsIndependent=true; noDesktopGeometryGuess=true; allReferencesCovered=true");
    }

    internal static PixelFrame Simulate(PixelFrame original, ReferenceViewport viewport)
    {
        var content = new PixelFrame(1920, 1017, 7680, new byte[1920 * 1017 * 4]);
        for (var row = 0; row < 1017; row++)
            Buffer.BlockCopy(original.Pixels, Math.Min(original.Height - 1, row + 23) * original.Stride,
                content.Pixels, row * content.Stride, Math.Min(original.Width, 1920) * 4);
        var native = ReferenceViewport.Resize(content, viewport.Width, viewport.Height);
        // Synthetic WGC frame includes independent fixed-size OS decorations.
        var decorated = new PixelFrame(native.Width + 16, native.Height + 39, (native.Width + 16) * 4,
            new byte[(native.Width + 16) * (native.Height + 39) * 4]);
        for (var row = 0; row < native.Height; row++)
            Buffer.BlockCopy(native.Pixels, row * native.Stride, decorated.Pixels, (row + 31) * decorated.Stride + 8 * 4, native.Stride);
        return viewport.Normalize(decorated, 8, 31);
    }

    private static PixelFrame PlaceReference(BotNC.App.Models.VisualReference reference)
    {
        var source = VisualRecognitionService.Decode(reference.Image);
        var full = new PixelFrame(1920, 1040, 7680, new byte[1920 * 1040 * 4]);
        var x = Math.Clamp(reference.SearchX + (reference.SearchWidth - reference.SourceWidth) / 2, 0, 1920 - reference.SourceWidth);
        var y = Math.Clamp(reference.SearchY + (reference.SearchHeight - reference.SourceHeight) / 2, 23, 1040 - reference.SourceHeight);
        for (var row = 0; row < reference.SourceHeight; row++)
            Buffer.BlockCopy(source.Pixels, (row + reference.SourceY) * source.Stride + reference.SourceX * 4,
                full.Pixels, (row + y) * full.Stride + x * 4, reference.SourceWidth * 4);
        return full;
    }
}
