namespace BotNC.App;
public partial class MainWindow
{
    private async Task LoadFarmDisplaySettingsAsync()
    {
        await LoadSpecialRoutineSettingsAsync();
        foreach (var client in new[] { 1, 2 })
            (client == 1 ? Client1KeepRestCheckBox : Client2KeepRestCheckBox).IsChecked =
                await _database.GetSettingAsync($"client{client}.keepRestMode") != "false";
    }
    private async Task SaveFarmDisplaySettingsAsync()
    {
        await SaveSpecialRoutineSettingsAsync();
        foreach (var client in new[] { 1, 2 })
            await _database.SaveSettingAsync($"client{client}.keepRestMode",
                (client == 1 ? Client1KeepRestCheckBox : Client2KeepRestCheckBox).IsChecked == true ? "true" : "false");
    }
}
