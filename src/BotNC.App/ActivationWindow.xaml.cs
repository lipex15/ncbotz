using System.Net.Http;
using System.Windows;
using BotNC.App.Services;

namespace BotNC.App;

public partial class ActivationWindow : Window
{
    private readonly ActivationService _activation;

    internal ActivationWindow(ActivationService activation)
    {
        _activation = activation;
        InitializeComponent();
        Loaded += (_, _) => UsernameBox.Focus();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private async void OnActivate(object sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        if (username.Length == 0 || password.Length == 0)
        {
            StatusText.Text = "Informe usuário e senha.";
            return;
        }

        ActivateButton.IsEnabled = false;
        StatusText.Text = "Conferindo seu acesso…";
        try
        {
            await _activation.ActivateAsync(username, password, CancellationToken.None);
            PasswordBox.Clear();
            DialogResult = true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
               InvalidOperationException or System.Security.Cryptography.CryptographicException or
               System.IO.IOException or FormatException)
        {
            StatusText.Text = exception is HttpRequestException or TaskCanceledException
                ? "Servidor de login indisponível. Tente novamente quando a conexão estiver ativa."
                : exception.Message;
        }
        finally { ActivateButton.IsEnabled = true; }
    }
}
