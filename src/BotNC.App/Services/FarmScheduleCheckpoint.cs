using System.Text.Json;

namespace BotNC.App.Services;

internal sealed record FarmScheduleCheckpoint(string Signature, int Index, double RemainingSeconds, bool Completed, bool Waiting)
{
    internal static FarmScheduleCheckpoint? TryRead(string? text)
    {
        try
        {
            var value = string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<FarmScheduleCheckpoint>(text);
            return value is { Index: >= 0, RemainingSeconds: >= 0 } && double.IsFinite(value.RemainingSeconds) &&
                !string.IsNullOrWhiteSpace(value.Signature) ? value : null;
        }
        catch (JsonException) { return null; }
    }
}
