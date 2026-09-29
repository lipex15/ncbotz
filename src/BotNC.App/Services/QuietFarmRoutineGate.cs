namespace BotNC.App.Services;

// One scheduled interruption per routine/cycle. A failed optional routine must
// not repeatedly steal focus from another application while farming continues.
internal sealed class QuietFarmRoutineGate
{
    private readonly Dictionary<string, string> attempts = new();
    internal bool CanRun(string routine, string cycle, bool farming, bool foreground) =>
        !farming || !attempts.TryGetValue(routine, out var previous) || previous != cycle;
    internal void Started(string routine, string cycle) => attempts[routine] = cycle;
}
