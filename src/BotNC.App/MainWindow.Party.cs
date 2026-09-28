using BotNC.App.Models;
using BotNC.App.Services;

namespace BotNC.App;

public partial class MainWindow
{
    internal void ShowPartyForScreenshot()
    {
        Client1PartyRole.SelectedIndex = 1;
        Client2PartyRole.SelectedIndex = 2;
        Client1PartyNames.Text = "weed · lover\n대박Brabo";
        Client2PartyInviter.Text = "流氓LIPEX";
        Client1KeepRestCheckBox.IsChecked = Client2KeepRestCheckBox.IsChecked = false;
        PartyExpander.IsExpanded = true;
        UpdateLayout();
        PartyExpander.BringIntoView();
    }

    private PartyOptions ReadPartyOptions(int client)
    {
        var role = client == 1 ? Client1PartyRole : Client2PartyRole;
        var names = client == 1 ? Client1PartyNames : Client2PartyNames;
        var inviter = client == 1 ? Client1PartyInviter : Client2PartyInviter;
        if (role.SelectedIndex <= 0) return new(PartyRole.Disabled, []);
        if ((client == 1 ? Client1KeepRestCheckBox : Client2KeepRestCheckBox).IsChecked == true)
            throw new ArgumentException($"Cliente {client}: desative ‘Manter descanso durante o farm’ para usar o grupo automático. Os convites ficam ocultos no descanso.");
        if (inviter.Text.Any(char.IsControl)) throw new ArgumentException("Nome do líder contém caracteres de controle.");
        var inviteNames = role.SelectedIndex == 1 ? PartyPolicy.ParseNames(names.Text) : [];
        if (role.SelectedIndex == 1 && inviteNames.Length == 0)
            throw new ArgumentException($"Grupo do Cliente {client}: informe pelo menos um nick para convidar.");
        return new((PartyRole)Math.Max(0, role.SelectedIndex),
            inviteNames, inviter.Text);
    }

    private async Task LoadPartySettingsAsync()
    {
        await LoadSpecialRoutineSettingsAsync();
        foreach (var client in new[] { 1, 2 })
        {
            var prefix = $"client{client}.party";
            (client == 1 ? Client1KeepRestCheckBox : Client2KeepRestCheckBox).IsChecked =
                await _database.GetSettingAsync($"client{client}.keepRestMode") != "false";
            var role = client == 1 ? Client1PartyRole : Client2PartyRole;
            role.SelectedIndex = int.TryParse(await _database.GetSettingAsync(prefix + ".role"), out var value) ? Math.Clamp(value, 0, 2) : 0;
            (client == 1 ? Client1PartyNames : Client2PartyNames).Text = await _database.GetSettingAsync(prefix + ".names") ?? "";
            (client == 1 ? Client1PartyInviter : Client2PartyInviter).Text = await _database.GetSettingAsync(prefix + ".inviter") ?? "";
        }
    }

    private async Task SavePartySettingsAsync()
    {
        await SaveSpecialRoutineSettingsAsync();
        foreach (var client in new[] { 1, 2 })
        {
            var prefix = $"client{client}.party";
            await _database.SaveSettingAsync($"client{client}.keepRestMode",
                (client == 1 ? Client1KeepRestCheckBox : Client2KeepRestCheckBox).IsChecked == true ? "true" : "false");
            await _database.SaveSettingAsync(prefix + ".role", (client == 1 ? Client1PartyRole : Client2PartyRole).SelectedIndex.ToString());
            await _database.SaveSettingAsync(prefix + ".names", (client == 1 ? Client1PartyNames : Client2PartyNames).Text);
            await _database.SaveSettingAsync(prefix + ".inviter", (client == 1 ? Client1PartyInviter : Client2PartyInviter).Text);
        }
    }
}
