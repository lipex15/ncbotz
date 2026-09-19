namespace BotNC.App.Models;

public sealed record GameWindowTarget(
    nint Handle,
    string Title,
    int ProcessId,
    bool IsMinimized,
    bool IsVisible)
{
    public string DisplayName => IsMinimized
        ? $"{Title} · minimizado · PID {ProcessId}"
        : $"{Title} · PID {ProcessId}";

    public override string ToString() => DisplayName;
}
