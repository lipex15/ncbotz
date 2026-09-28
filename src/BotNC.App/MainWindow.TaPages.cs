using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BotNC.App.Models;
namespace BotNC.App;
public partial class MainWindow
{
    private TaDestination _taPageDestination;
    private bool _refreshingTaPage;
    private void OnShowTaPage(object sender, RoutedEventArgs e)
    {
        OnShowOverview(sender, e);
        OverviewPanel.Visibility = Visibility.Collapsed;
        OverviewNavigationButton.Background = Brushes.Transparent;
        var index = int.Parse(((Button)sender).CommandParameter.ToString()!);
        _taPageDestination = index == 0 ? TaDestination.Ta1Codex : index == 1 ? TaDestination.Ta2 : TaDestination.Ta3;
        TaDetailsTitle.Text = $"T.A {index + 1}";
        ((Button)sender).Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#153D50"));
        TaDestinationPanel.Visibility = Visibility.Visible;
        RefreshTaPage();
    }
    private void RefreshTaPage()
    {
        _refreshingTaPage = true;
        try
        {
            TaPageClient1Enabled.IsChecked = _client1TaCoordinatesEnabled.Contains(_taPageDestination);
            TaPageClient2Enabled.IsChecked = _client2TaCoordinatesEnabled.Contains(_taPageDestination);
            var a = _client1TaCoordinates.GetValueOrDefault(_taPageDestination);
            var b = _client2TaCoordinates.GetValueOrDefault(_taPageDestination);
            TaPageClient1Point.Text = a is null ? "Não definida" : $"({a.X}, {a.Y})";
            TaPageClient2Point.Text = b is null ? "Não definida" : $"({b.X}, {b.Y})";
            TaPageHelp.Text = _taPageDestination == TaDestination.Ta1Codex
                ? "T.A 1 (Codex): abra M no zoom mínimo e capture o ponto. O ponto é obrigatório, inclusive na Agenda."
                : "Abra o mapa e capture o ponto. Sem ponto personalizado, o bot usa o farm na primeira posição dos favoritos e o teleporte opcional na segunda.";
            TaDestinationPanel.IsEnabled = _runCancellation is null && !_capturingCustomCoordinate;
        }
        finally { _refreshingTaPage = false; }
    }
    private async void OnTaPageCapture(object sender, RoutedEventArgs e)
    {
        await CaptureCustomCoordinateAsync(int.Parse(((Button)sender).Tag.ToString()!), destinationOverride: _taPageDestination);
        RefreshTaPage();
    }
    private async void OnTaPageEnabled(object sender, RoutedEventArgs e)
    {
        if (_refreshingTaPage || !_databaseReady || _runCancellation is not null) return;
        var control = (CheckBox)sender;
        var client = int.Parse(control.Tag.ToString()!);
        var enabled = client == 1 ? _client1TaCoordinatesEnabled : _client2TaCoordinatesEnabled;
        if (control.IsChecked == true) enabled.Add(_taPageDestination); else enabled.Remove(_taPageDestination);
        try
        {
            await _database.SaveSettingAsync($"client{client}.customFarm.{_taPageDestination}.enabled", control.IsChecked == true ? "true" : "false");
            OnTaDestinationChanged(client == 1 ? Ta1ComboBox : Ta2ComboBox, new SelectionChangedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
        }
        catch (Exception error) { ShowValidation(error.Message); }
    }
}
