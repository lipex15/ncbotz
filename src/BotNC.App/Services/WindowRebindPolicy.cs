using BotNC.App.Models;

namespace BotNC.App.Services;

internal static class WindowRebindPolicy
{
    internal static GameWindowTarget? Resolve(GameWindowTarget previous,
        IReadOnlyList<GameWindowTarget> available, IReadOnlyList<GameWindowTarget> otherClients)
    {
        static bool SameProcess(GameWindowTarget a, GameWindowTarget b) =>
            a.ProcessId == b.ProcessId && a.Title == b.Title &&
            (a.ProcessStartedAt is null || a.ProcessStartedAt == b.ProcessStartedAt);
        var candidates = available.Where(w => w.Title == previous.Title &&
            !otherClients.Any(other => other.Handle == w.Handle || other.ProcessId == w.ProcessId)).ToArray();
        if (candidates.Length != 1) return null;
        var candidate = candidates[0];
        if (SameProcess(previous, candidate)) return candidate;
        // A closed pair of games cannot be identified by discovery order.
        // Only replace one missing slot when every other slot is still accounted for.
        if (otherClients.Any(other => !available.Any(w => w.Handle == other.Handle && SameProcess(other, w))))
            return null;
        if (available.Any(w => SameProcess(previous, w))) return null;
        return !string.IsNullOrEmpty(previous.ExecutablePath) &&
            string.Equals(previous.ExecutablePath, candidate.ExecutablePath, StringComparison.OrdinalIgnoreCase)
            ? candidate : null;
    }

    internal static void Verify()
    {
        var first = new GameWindowTarget(1, "NIGHT CROWS(1)", 11, false, true, "game.exe", DateTime.UnixEpoch);
        var second = new GameWindowTarget(2, "NIGHT CROWS(2)", 22, false, true, "game.exe", DateTime.UnixEpoch);
        var replacement = first with { Handle = 3, ProcessId = 33, ProcessStartedAt = DateTime.UnixEpoch.AddMinutes(1) };
        void Check(bool ok) { if (!ok) throw new InvalidOperationException("Window rebind isolation failed."); }
        Check(Resolve(first, [replacement, second], [second]) == replacement);
        Check(Resolve(first, [replacement], [second]) is null);
        Check(Resolve(first, [replacement, replacement with { Handle = 4 }, second], [second]) is null);
        Check(Resolve(first, [replacement with { ExecutablePath = "other.exe" }, second], [second]) is null);
        Check(Resolve(first, [replacement with { ProcessId = second.ProcessId }, second], [second]) is null);
        Check(Resolve(first, [replacement with { Title = second.Title }], []) is null);
        Check(Resolve(first, [replacement], []) == replacement);
        Check(Resolve(first with { ExecutablePath = null }, [replacement], []) is null);
    }
}
