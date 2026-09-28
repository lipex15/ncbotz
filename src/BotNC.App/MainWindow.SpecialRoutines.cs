using System.Windows;
using BotNC.App.Models;

namespace BotNC.App;

public partial class MainWindow
{
    internal void ShowSpecialRoutinesForScreenshot()
    {
        ShowRoutinesForScreenshot();
        UpdateLayout();
        GlobalRoutineCard.BringIntoView();
    }
    private FarmCoordinate? _global1Point, _global2Point;
    private async void OnCaptureGlobal1(object sender, RoutedEventArgs e) => await CaptureCustomCoordinateAsync(1, global: true);
    private async void OnCaptureGlobal2(object sender, RoutedEventArgs e) => await CaptureCustomCoordinateAsync(2, global: true);

    private GlobalDungeonOptions ReadGlobalOptions(int client)
    {
        var steps = client == 1 ? Client1ScheduleSteps : Client2ScheduleSteps;
        var points = client == 1 ? _client1TaCoordinates : _client2TaCoordinates;
        if (ScopeIncludesClient(FarmScheduleScopeComboBox.SelectedItem, client) &&
            steps.Any(s => s.Destination == FarmScheduleDestination.Ta1) && !points.ContainsKey(TaDestination.Ta1Codex))
            throw new ArgumentException($"Agenda do Cliente {client}: capture o ponto da T.A 1 (Codex) no mapa com zoom mínimo.");
        var enabled = ScopeIncludesClient(GlobalScopeComboBox.SelectedItem, client);
        var point = client == 1 ? _global1Point : _global2Point;
        if (!enabled) return new(false, 120, point);
        if (!int.TryParse(GlobalDurationTextBox.Text, out var minutes) || minutes is < 1 or > 120)
            throw new ArgumentException("Global: informe duração entre 1 e 120 minutos.");
        if (point is null) throw new ArgumentException($"Global do Cliente {client}: capture a coordenada de farm no mapa M.");
        return new(true, minutes, point);
    }
    private async Task LoadSpecialRoutineSettingsAsync()
    {
        string[] scopes = ["Nenhum", "Cliente 1", "Cliente 2", "Ambos"];
        BoostBuffScopeComboBox.ItemsSource = scopes;
        GlobalScopeComboBox.ItemsSource = scopes;
        BoostBuffScopeComboBox.SelectedItem = await _database.GetSettingAsync("routines.boost.scope") ?? "Nenhum";
        GlobalScopeComboBox.SelectedItem = await _database.GetSettingAsync("routines.global.scope") ?? "Nenhum";
        GlobalDurationTextBox.Text = await _database.GetSettingAsync("routines.global.minutes") ?? "120";
        foreach (var client in new[] { 1, 2 })
        {
            if (int.TryParse(await _database.GetSettingAsync($"client{client}.global.x"), out var x) &&
                int.TryParse(await _database.GetSettingAsync($"client{client}.global.y"), out var y))
                SetGlobalPoint(client, new(x, y));
        }
    }
    private void SetGlobalPoint(int client, FarmCoordinate point)
    {
        if (client == 1) { _global1Point = point; Global1PointText.Text = $"({point.X}, {point.Y})"; }
        else { _global2Point = point; Global2PointText.Text = $"({point.X}, {point.Y})"; }
    }
    private async Task SaveSpecialRoutineSettingsAsync()
    {
        await _database.SaveSettingAsync("routines.boost.scope", BoostBuffScopeComboBox.SelectedItem?.ToString() ?? "Nenhum");
        await _database.SaveSettingAsync("routines.global.scope", GlobalScopeComboBox.SelectedItem?.ToString() ?? "Nenhum");
        await _database.SaveSettingAsync("routines.global.minutes", GlobalDurationTextBox.Text);
    }
}
