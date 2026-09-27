using BotNC.App.Models;

namespace BotNC.App.Services;

internal static class PartyPolicy
{
    internal static bool ClearlyDifferentInviter(string text, string preferred)
    {
        if (string.IsNullOrWhiteSpace(preferred) || ContainsName(text, preferred)) return false;
        // Only treat a complete, plain-text name as a confident mismatch.
        // Unreadable/non-Latin names retain the user-authorized acceptance fallback.
        var match = System.Text.RegularExpressions.Regex.Match(text,
            @"equipe\s+de\s+([^\r\n]+?)\.\s*(?:\r?\n|\s)*Deseja", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success && match.Groups[1].Value.Length >= 3 &&
            match.Groups[1].Value.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or ' ' or '_' or '-') &&
            preferred.All(c => c <= 127) && !ContainsName(match.Groups[1].Value, preferred);
    }
    internal static string[] ParseNames(string text)
    {
        var names = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToArray();
        if (names.Length > 4) throw new ArgumentException("Grupo: informe no máximo quatro convidados, um nick por linha.");
        if (names.Any(n => n.Length > 64 || n.Any(char.IsControl)))
            throw new ArgumentException("Grupo: nick inválido (máximo 64 caracteres, sem tabulações).");
        return names; // Never trim, transliterate or normalize the actual nickname.
    }

    internal static bool ContainsName(string text, string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        // OCR line breaks are tolerated; symbols are never stripped to turn a
        // different player's name into a match.
        static string Compact(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));
        var haystack = Compact(text);
        var needle = Compact(name);
        var index = haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var end = index + needle.Length;
            if ((index == 0 || !char.IsLetterOrDigit(haystack[index - 1])) &&
                (end == haystack.Length || !char.IsLetterOrDigit(haystack[end]))) return true;
            index = haystack.IndexOf(needle, index + 1, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}

internal sealed class PartyRuntime
{
    internal DateTime NextScan;
    internal DateTime NextLeaderCheck;
    internal DateTime ReconnectEpoch;
    internal bool RecheckAfterReconnect;
    internal bool RebuiltAfterReconnect;
    internal int UncertainReads;
    internal DateTime LastRebuild;
    internal bool RosterEstablished;
    internal int MissingCardReads;
    internal int NextInviteIndex;
    internal bool AcceptanceAwaitingCard;
    internal string? AwaitingName;
    internal int MembersBeforeInvite;
    internal readonly Dictionary<string, int> Attempts = new(StringComparer.Ordinal);
    internal readonly HashSet<string> Confirmed = new(StringComparer.Ordinal);
    internal bool MayInvite(string name) => !Confirmed.Contains(name) && Attempts.GetValueOrDefault(name) < 5;
    internal int RecordInvite(string name) => Attempts[name] = Attempts.GetValueOrDefault(name) + 1;
    internal void Reconnected(DateTime epoch)
    {
        if (epoch == default || epoch == ReconnectEpoch) return;
        ReconnectEpoch = epoch;
        RecheckAfterReconnect = true;
        RebuiltAfterReconnect = false;
        UncertainReads = 0;
        NextLeaderCheck = default;
        Confirmed.Clear();
        RosterEstablished = false;
        AwaitingName = null;
        // Attempts intentionally survive reconnect/rebuild; only a new session resets them.
    }
}
