namespace BotNC.App.Services;

// Status is a snapshot, not a command. Keep only the newest snapshot per slot.
// Never use this buffer for protection decisions, game input or persistence.
internal sealed class LatestUiUpdates
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Action> _pending = new();
    internal void Set(string key, Action update)
    {
        lock (_sync) _pending[key] = update;
    }
    internal Action[] Drain()
    {
        lock (_sync)
        {
            var result = _pending.Values.ToArray();
            _pending.Clear();
            return result;
        }
    }
}
