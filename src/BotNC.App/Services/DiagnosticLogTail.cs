using System.IO;
using System.Text;

namespace BotNC.App.Services;

internal static class DiagnosticLogTail
{
    internal const int MaxBytes = 256 * 1024;
    internal static string Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var offset = Math.Max(0, stream.Length - MaxBytes);
        stream.Seek(offset, SeekOrigin.Begin);
        var bytes = new byte[(int)Math.Min(stream.Length, MaxBytes)];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = stream.Read(bytes, count, bytes.Length - count);
            if (read == 0) break;
            count += read;
        }
        var text = Encoding.UTF8.GetString(bytes, 0, count);
        if (offset > 0)
        {
            var newline = text.IndexOf('\n');
            text = newline >= 0 ? text[(newline + 1)..] : string.Empty;
        }
        return string.Join(Environment.NewLine, text.Split('\n').TakeLast(1500));
    }
}
