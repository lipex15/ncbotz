using BotNC.App.Models;

namespace BotNC.App;

public partial class MainWindow
{
    private static GameWindowTarget? UniqueTitle(IReadOnlyList<GameWindowTarget> windows, string title)
    {
        var matches = windows.Where(window => window.Title == title).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private void RefreshStoppedClientWindows()
    {
        if (!_environmentReady || _runCancellation is not null || _capturingCustomCoordinate ||
            Client1ComboBox.IsDropDownOpen || Client2ComboBox.IsDropDownOpen) return;
        var discovered = _gameWindows.Discover();
        var listed = Client1ComboBox.Items.Cast<GameWindowTarget>().ToArray();
        // No activation, maximize, screenshots or list churn on this timer.
        if (discovered.Count == listed.Length && discovered.Zip(listed).All(pair =>
            pair.First.Handle == pair.Second.Handle && pair.First.ProcessId == pair.Second.ProcessId &&
            pair.First.ProcessStartedAt == pair.Second.ProcessStartedAt && pair.First.Title == pair.Second.Title))
            return;
        RefreshClients();
    }
}
