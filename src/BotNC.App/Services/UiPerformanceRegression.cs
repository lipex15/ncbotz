using System.Diagnostics;
using System.IO;
using System.Text;

namespace BotNC.App.Services;

internal static class UiPerformanceRegression
{
    internal static async Task VerifyAsync(string output)
    {
        var updates = new LatestUiUpdates();
        var latest = -1;
        Parallel.For(0, 100000, index => updates.Set("audio:1", () => latest = index));
        updates.Set("audio:1", () => latest = 100000);
        updates.Set("audio:2", () => { });
        var batch = updates.Drain();
        if (batch.Length != 2) throw new InvalidOperationException("UI status backlog was not bounded per client.");
        foreach (var update in batch) update();
        if (latest != 100000 || updates.Drain().Length != 0)
            throw new InvalidOperationException("Latest UI status lost or repeated.");

        var file = output + ".large-log-fixture";
        try
        {
            await using (var writer = new StreamWriter(file, false, Encoding.UTF8))
            {
                var line = new string('x', 1024);
                for (var index = 0; index < 8192; index++) await writer.WriteLineAsync(line);
                await writer.WriteLineAsync("última linha: proteção e reconexão");
            }
            var timer = Stopwatch.StartNew();
            var tail = await Task.Run(() => DiagnosticLogTail.Read(file));
            if (!tail.Contains("última linha: proteção e reconexão") ||
                tail.Length > DiagnosticLogTail.MaxBytes * 2 || tail.Split('\n').Length > 1500)
                throw new InvalidOperationException("Diagnostic tail was unbounded or lost its final Unicode line.");
            await File.WriteAllTextAsync(output + ".performance.txt",
                $"100000 status events -> {batch.Length} UI updates; 8 MiB log -> {tail.Length} characters; tailReadMs={timer.ElapsedMilliseconds}");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }
}
