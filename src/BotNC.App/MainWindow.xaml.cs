using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using BotNC.App.Models;
using BotNC.App.Services;

namespace BotNC.App;

public partial class MainWindow : Window
{
    private readonly GameWindowService _gameWindows = new();
    private readonly AppDatabase _database = new();
    private readonly PauseController _pause = new();
    private readonly BotAutomationEngine _engine;
    private readonly AppUpdateService _updates = new();
    private CancellationTokenSource? _runCancellation;
    private CancellationTokenSource? _updateDownloadCancellation;
    private TaskCompletionSource<bool>? _runFinished;
    private AvailableUpdate? _availableUpdate;
    private bool _updateBusy;
    private bool _databaseReady;
    private BotRunState _stateBeforePause = BotRunState.Waiting;
    private string? _savedClient1Title;
    private string? _savedClient2Title;

    public MainWindow()
    {
        InitializeComponent();
        var capture = new ScreenCaptureService();
        var recognition = new VisualRecognitionService(_database, capture);
        _engine = new BotAutomationEngine(
            _gameWindows,
            new WindowsInputService(),
            recognition,
            capture);
        _engine.Log += OnEngineLog;
        _engine.StatusChanged += OnEngineStatusChanged;
        _engine.AudioStatusChanged += OnEngineAudioStatusChanged;

        Ta1ComboBox.ItemsSource = new[] { "T.A 2", "T.A 3" };
        Ta2ComboBox.ItemsSource = new[] { "T.A 2", "T.A 3" };
        Ta1ComboBox.SelectedIndex = 1;
        Ta2ComboBox.SelectedIndex = 0;

        TeleportKeyComboBox.ItemsSource = BuildKeyChoices();
        TeleportKeyComboBox.Text = "E";
        EmergencyTeleportKeyComboBox.ItemsSource = BuildKeyChoices();
        EmergencyTeleportKeyComboBox.Text = "7";
        ScheduleTextBox.Text = "11:50";
        DurationTextBox.Text = "30";
        DeathThresholdTextBox.Text = "3";
        DeathWindowTextBox.Text = "30";
        AgendaDurationTextBox.Text = "60";
        InstalledVersionText.Text = $"v{AppUpdateService.CurrentVersion.ToString(3)}";
        ApplicationVersionText.Text = $"v{AppUpdateService.CurrentVersion.ToString(3)} · Proteção independente";
        DatabasePathText.Text = $"Dados: {_database.DatabasePath} · Log: {_engine.RuntimeLogPath}";
    }

    private void OnShowOverview(object sender, RoutedEventArgs e)
    {
        OverviewPanel.Visibility = Visibility.Visible;
        UpdatesPanel.Visibility = Visibility.Collapsed;
        OverviewNavigationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#266FEA"));
        UpdatesNavigationButton.Background = Brushes.Transparent;
    }

    private void OnShowUpdates(object sender, RoutedEventArgs e)
    {
        OverviewPanel.Visibility = Visibility.Collapsed;
        UpdatesPanel.Visibility = Visibility.Visible;
        OverviewNavigationButton.Background = Brushes.Transparent;
        UpdatesNavigationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#266FEA"));
    }

    internal void ShowUpdatesForScreenshot() => OnShowUpdates(this, new RoutedEventArgs());

    private async void OnCheckForUpdates(object sender, RoutedEventArgs e)
    {
        if (_updateBusy)
        {
            return;
        }

        _updateBusy = true;
        _availableUpdate = null;
        CheckUpdatesButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "Consultando versões publicadas…";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            _availableUpdate = await _updates.CheckAsync(timeout.Token);
            UpdateStatusText.Text = _availableUpdate is null
                ? "Nenhuma atualização disponível. Sua versão está em dia."
                : $"Versão v{_availableUpdate.Version.ToString(3)} disponível para instalar.";
            InstallUpdateButton.IsEnabled = _availableUpdate is not null;
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = $"Não foi possível verificar: {exception.GetBaseException().Message}";
        }
        finally
        {
            _updateBusy = false;
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private async void OnInstallUpdate(object sender, RoutedEventArgs e)
    {
        if (_updateBusy || _availableUpdate is null)
        {
            return;
        }

        _updateBusy = true;
        CheckUpdatesButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        UpdateProgressBar.Visibility = Visibility.Visible;
        UpdateProgressBar.IsIndeterminate = true;
        UpdateStatusText.Text = "Baixando a atualização…";
        _updateDownloadCancellation = new CancellationTokenSource();
        try
        {
            var progress = new Progress<int>(percent =>
            {
                UpdateProgressBar.IsIndeterminate = false;
                UpdateProgressBar.Value = percent;
                UpdateStatusText.Text = percent < 100
                    ? $"Baixando a atualização… {percent}%"
                    : "Conferindo a integridade do instalador…";
            });
            var installer = await _updates.DownloadAndVerifyAsync(
                _availableUpdate, progress, _updateDownloadCancellation.Token);
            UpdateStatusText.Text = "Instalador conferido. Encerrando o bot para atualizar…";
            if (_runFinished is { } activeRun)
            {
                _runCancellation?.Cancel();
                await activeRun.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }

            AppUpdateService.LaunchInstaller(installer);
            Close();
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = $"Atualização interrompida: {exception.GetBaseException().Message}";
            UpdateProgressBar.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _updateDownloadCancellation?.Dispose();
            _updateDownloadCancellation = null;
            _updateBusy = false;
            CheckUpdatesButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = _availableUpdate is not null;
        }
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        SetStatus(BotRunState.Waiting, "Preparando o aplicativo", "Cadastrando referências visuais…");
        try
        {
            await _database.InitializeAsync();
            _databaseReady = true;
            await LoadSettingsAsync();
            RefreshClients();
            SetStatus(BotRunState.Stopped, "Bot parado", "Configure o módulo e clique em Iniciar.");
            AddLog("PEXBOT iniciado.");
            AddLog("Banco de imagens carregado com sucesso.");
        }
        catch (Exception exception)
        {
            SetStatus(BotRunState.Failed, "Falha na inicialização", exception.Message);
            AddLog($"ERRO: {exception.Message}");
            MessageBox.Show(
                this,
                exception.Message,
                "PEXBOT — Falha na inicialização",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnRefreshClients(object sender, RoutedEventArgs e) => RefreshClients();

    private void RefreshClients()
    {
        var previousClient1 = (Client1ComboBox.SelectedItem as GameWindowTarget)?.Title ?? _savedClient1Title;
        var previousClient2 = (Client2ComboBox.SelectedItem as GameWindowTarget)?.Title ?? _savedClient2Title;
        var clients = _gameWindows.Discover();
        Client1ComboBox.ItemsSource = clients;
        Client2ComboBox.ItemsSource = clients;
        Client1ComboBox.SelectedItem = clients.FirstOrDefault(client => client.Title == previousClient1)
            ?? clients.FirstOrDefault(client => client.Title == "NIGHT CROWS(1)")
            ?? clients.FirstOrDefault();
        Client2ComboBox.SelectedItem = clients.FirstOrDefault(client => client.Title == previousClient2)
            ?? clients.FirstOrDefault(client => client.Title == "NIGHT CROWS(2)")
            ?? clients.Skip(1).FirstOrDefault()
            ?? clients.FirstOrDefault();
        AddLog(clients.Count switch
        {
            0 => "Nenhum cliente NIGHT CROWS encontrado.",
            1 => "1 cliente NIGHT CROWS encontrado.",
            _ => $"{clients.Count} clientes NIGHT CROWS encontrados."
        });
    }

    private void OnClient1EnabledChanged(object sender, RoutedEventArgs e)
    {
        if (Client1ComboBox is null || Ta1ComboBox is null)
        {
            return;
        }

        var enabled = EnableClient1CheckBox.IsChecked == true;
        Client1ComboBox.IsEnabled = enabled && _runCancellation is null;
        Ta1ComboBox.IsEnabled = enabled && _runCancellation is null;
        Client1SapherasCheckBox.IsEnabled = enabled && _runCancellation is null;
    }

    private void OnClient2EnabledChanged(object sender, RoutedEventArgs e)
    {
        if (Client2ComboBox is null || Ta2ComboBox is null)
        {
            return;
        }

        var enabled = EnableClient2CheckBox.IsChecked == true;
        Client2ComboBox.IsEnabled = enabled && _runCancellation is null;
        Ta2ComboBox.IsEnabled = enabled && _runCancellation is null;
        Client2SapherasCheckBox.IsEnabled = enabled && _runCancellation is null;
    }

    private void OnUseNow(object sender, RoutedEventArgs e)
    {
        ScheduleTextBox.Text = DateTime.Now.AddSeconds(5).ToString("HH:mm:ss");
    }

    private async void OnStartBot(object sender, RoutedEventArgs e)
    {
        if (!_databaseReady)
        {
            ShowValidation("O banco visual ainda não terminou de carregar.");
            return;
        }

        if (_runCancellation is not null)
        {
            return;
        }

        var enableClient1 = EnableClient1CheckBox.IsChecked == true;
        var enableClient2 = EnableClient2CheckBox.IsChecked == true;
        if (!enableClient1 && !enableClient2)
        {
            ShowValidation("Ative pelo menos um cliente.");
            return;
        }

        GameWindowTarget? client1 = null;
        if (enableClient1)
        {
            if (Client1ComboBox.SelectedItem is not GameWindowTarget selectedClient1)
            {
                ShowValidation("O Cliente 1 está ativo, mas nenhuma janela foi selecionada.");
                return;
            }

            client1 = selectedClient1;
        }

        GameWindowTarget? client2 = null;
        if (enableClient2)
        {
            if (Client2ComboBox.SelectedItem is not GameWindowTarget selectedClient2)
            {
                ShowValidation("O Cliente 2 está ativo, mas nenhuma janela foi selecionada.");
                return;
            }

            if (client1 is not null && selectedClient2.ProcessId == client1.ProcessId)
            {
                ShowValidation("Selecione janelas diferentes para o Cliente 1 e o Cliente 2.");
                return;
            }

            client2 = selectedClient2;
        }

        if (!TryParseSchedule(ScheduleTextBox.Text, out var scheduledAt))
        {
            ShowValidation("Informe o horário como HH:mm ou HH:mm:ss.");
            return;
        }

        if (!TryParseDuration(DurationTextBox.Text, out var duration))
        {
            ShowValidation("Informe uma permanência maior que zero, em minutos.");
            return;
        }

        if (!TryParseDuration(DirectSapherasWindowTextBox.Text, out var directSapherasWindow))
        {
            ShowValidation("Informe uma janela de entrada direta maior que zero, em minutos.");
            return;
        }

        if (!int.TryParse(DeathThresholdTextBox.Text.Trim(), out var deathThreshold) || deathThreshold is < 1 or > 20)
        {
            ShowValidation("Informe entre 1 e 20 mortes para ativar o Anti Over Kill.");
            return;
        }

        if (!TryParseDuration(DeathWindowTextBox.Text, out var deathWindow))
        {
            ShowValidation("Informe uma janela de mortes maior que zero, em minutos.");
            return;
        }

        if (!TryParseDuration(AgendaDurationTextBox.Text, out var agendaDuration))
        {
            ShowValidation("Informe um tempo de Agenda maior que zero, em minutos.");
            return;
        }

        var keyName = TeleportKeyComboBox.Text.Trim().ToUpperInvariant();
        if (!TryParseVirtualKey(keyName, out var teleportKey))
        {
            ShowValidation("A tecla do teleporte deve ser 0–9, A–Z ou F1–F12.");
            return;
        }

        var emergencyKeyName = EmergencyTeleportKeyComboBox.Text.Trim().ToUpperInvariant();
        if (!TryParseVirtualKey(emergencyKeyName, out var emergencyTeleportKey))
        {
            ShowValidation("A tecla do TP de emergência deve ser 0–9, A–Z ou F1–F12.");
            return;
        }

        var options = new SapherasOptions(
            scheduledAt,
            duration,
            directSapherasWindow,
            teleportKey,
            keyName,
            emergencyTeleportKey,
            emergencyKeyName);
        var clients = new List<AutomationClientOptions>();
        if (client1 is not null)
        {
            clients.Add(new(
                "Cliente 1",
                client1,
                ParseTaDestination(Ta1ComboBox.SelectedItem),
                Client1SapherasCheckBox.IsChecked == true,
                1));
        }

        if (client2 is not null)
        {
            clients.Add(
                new AutomationClientOptions(
                    "Cliente 2",
                    client2,
                    ParseTaDestination(Ta2ComboBox.SelectedItem),
                    Client2SapherasCheckBox.IsChecked == true,
                    client1 is null ? 1 : 2));
        }

        var antiOverkill = new AntiOverkillOptions(deathThreshold, deathWindow, agendaDuration);
        var runOptions = new BotRunOptions(options, antiOverkill, clients);
        await SaveSettingsAsync(runOptions);

        _runCancellation = new CancellationTokenSource();
        _runFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pause.Resume();
        SetRunControls(isRunning: true);
        AddLog(clients.Count == 2
            ? "Bot iniciado para 2 clientes. Cliente 1 tem prioridade."
            : $"Bot iniciado somente para {clients[0].Label}.");

        try
        {
            await _engine.RunAsync(runOptions, _pause, _runCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus(BotRunState.Stopped, "Bot parado", "Execução interrompida pelo usuário.");
            AddLog("Execução interrompida.");
        }
        catch (Exception exception)
        {
            SetStatus(BotRunState.Failed, "Fluxo interrompido", exception.Message);
            AddLog($"ERRO: {exception.Message}");
            MessageBox.Show(
                this,
                exception.Message,
                "PEXBOT — Fluxo interrompido",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _runCancellation.Dispose();
            _runCancellation = null;
            SetRunControls(isRunning: false);
            _runFinished?.TrySetResult(true);
            _runFinished = null;
        }
    }

    private void OnPauseResume(object sender, RoutedEventArgs e)
    {
        if (_runCancellation is null)
        {
            return;
        }

        if (_pause.IsPaused)
        {
            _pause.Resume();
            PauseButton.Content = "Pausar";
            AddLog("Bot retomado.");
            SetBadge(_stateBeforePause);
        }
        else
        {
            _stateBeforePause = BotRunState.Running;
            _pause.Pause();
            PauseButton.Content = "Continuar";
            AddLog("Bot pausado.");
            SetStatus(BotRunState.Paused, "Bot pausado", "Nenhuma nova ação será iniciada.");
        }
    }

    private void OnStopBot(object sender, RoutedEventArgs e)
    {
        _runCancellation?.Cancel();
    }

    private void OnClearLog(object sender, RoutedEventArgs e) => LogListBox.Items.Clear();

    private void OnEngineLog(string message) =>
        Dispatcher.BeginInvoke(() => AddLog(message));

    private void OnEngineStatusChanged(BotRunState state, string title, string detail) =>
        Dispatcher.BeginInvoke(
            () =>
            {
                if (!_pause.IsPaused)
                {
                    _stateBeforePause = state;
                    SetStatus(state, title, detail);
                }
            });

    private void OnEngineAudioStatusChanged(AudioClientStatus status) =>
        Dispatcher.BeginInvoke(() => UpdateAudioStatus(status));

    private void UpdateAudioStatus(AudioClientStatus status)
    {
        var stateText = status.Label == "Cliente 1" ? Client1AudioStateText : Client2AudioStateText;
        if (!status.IsHealthy)
        {
            stateText.Text = "Sem captura";
            stateText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6577"));
        }
        else if (status.IsArmed)
        {
            stateText.Text = "Protegido";
            stateText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38D59A"));
        }
        else
        {
            stateText.Text = "Em preparação";
            stateText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0B84B"));
        }
    }

    private void AddLog(string message)
    {
        LogListBox.Items.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (LogListBox.Items.Count > 400)
        {
            LogListBox.Items.RemoveAt(0);
        }

        if (LogListBox.Items.Count > 0)
        {
            LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
        }
    }

    private void SetStatus(BotRunState state, string title, string detail)
    {
        CurrentStateText.Text = title;
        CurrentDetailText.Text = detail;
        SetBadge(state);
    }

    private void SetBadge(BotRunState state)
    {
        var (text, color) = state switch
        {
            BotRunState.Stopped => ("PARADO", "#7F91A6"),
            BotRunState.Waiting => ("AGUARDANDO", "#F0B84B"),
            BotRunState.Running => ("EXECUTANDO", "#38D59A"),
            BotRunState.Paused => ("PAUSADO", "#F0B84B"),
            BotRunState.Completed => ("CONCLUÍDO", "#4E91FF"),
            BotRunState.Failed => ("ERRO", "#FF6577"),
            _ => ("PARADO", "#7F91A6")
        };
        StatusBadgeText.Text = text;
        StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private void SetRunControls(bool isRunning)
    {
        if (isRunning)
        {
            Client1AudioStateText.Text = EnableClient1CheckBox.IsChecked == true ? "Conectando…" : "Desativado";
            Client2AudioStateText.Text = EnableClient2CheckBox.IsChecked == true ? "Conectando…" : "Desativado";
        }

        StartButton.IsEnabled = !isRunning;
        PauseButton.IsHitTestVisible = isRunning;
        PauseButton.Opacity = isRunning ? 1 : 0.45;
        StopButton.IsHitTestVisible = isRunning;
        StopButton.Opacity = isRunning ? 1 : 0.45;
        RefreshButton.IsEnabled = !isRunning;
        EnableClient1CheckBox.IsEnabled = !isRunning;
        Client1ComboBox.IsEnabled = !isRunning && EnableClient1CheckBox.IsChecked == true;
        Ta1ComboBox.IsEnabled = !isRunning && EnableClient1CheckBox.IsChecked == true;
        Client1SapherasCheckBox.IsEnabled = !isRunning && EnableClient1CheckBox.IsChecked == true;
        EnableClient2CheckBox.IsEnabled = !isRunning;
        Client2ComboBox.IsEnabled = !isRunning && EnableClient2CheckBox.IsChecked == true;
        Ta2ComboBox.IsEnabled = !isRunning && EnableClient2CheckBox.IsChecked == true;
        Client2SapherasCheckBox.IsEnabled = !isRunning && EnableClient2CheckBox.IsChecked == true;
        ScheduleTextBox.IsEnabled = !isRunning;
        DurationTextBox.IsEnabled = !isRunning;
        DirectSapherasWindowTextBox.IsEnabled = !isRunning;
        TeleportKeyComboBox.IsEnabled = !isRunning;
        EmergencyTeleportKeyComboBox.IsEnabled = !isRunning;
        DeathThresholdTextBox.IsEnabled = !isRunning;
        DeathWindowTextBox.IsEnabled = !isRunning;
        AgendaDurationTextBox.IsEnabled = !isRunning;
        if (!isRunning)
        {
            PauseButton.Content = "Pausar";
        }
    }

    private async Task LoadSettingsAsync()
    {
        var defaultsApplied = await _database.GetSettingAsync("sapheras.defaultsV050Applied");
        if (string.IsNullOrWhiteSpace(defaultsApplied))
        {
            ScheduleTextBox.Text = "11:50";
            DurationTextBox.Text = "30";
            await _database.SaveSettingAsync("sapheras.schedule", "11:50:00");
            await _database.SaveSettingAsync("sapheras.durationMinutes", "30");
            await _database.SaveSettingAsync("sapheras.defaultsV050Applied", "true");
        }

        var schedule = await _database.GetSettingAsync("sapheras.schedule");
        var duration = await _database.GetSettingAsync("sapheras.durationMinutes");
        var directWindow = await _database.GetSettingAsync("sapheras.directWindowMinutes");
        var teleport = await _database.GetSettingAsync("sapheras.teleportKey");
        var emergencyTeleport = await _database.GetSettingAsync("ta3.emergencyTeleportKey");
        _savedClient1Title = await _database.GetSettingAsync("client1.title");
        _savedClient2Title = await _database.GetSettingAsync("client2.title");
        var client1Ta = await _database.GetSettingAsync("client1.ta");
        var client2Ta = await _database.GetSettingAsync("client2.ta");
        var client1Sapheras = await _database.GetSettingAsync("client1.sapheras");
        var client2Sapheras = await _database.GetSettingAsync("client2.sapheras");
        var client1Enabled = await _database.GetSettingAsync("client1.enabled");
        var client2Enabled = await _database.GetSettingAsync("client2.enabled");
        var deathThreshold = await _database.GetSettingAsync("antiOverkill.deathThreshold");
        var deathWindow = await _database.GetSettingAsync("antiOverkill.deathWindowMinutes");
        var agendaDuration = await _database.GetSettingAsync("antiOverkill.agendaDurationMinutes");
        if (!string.IsNullOrWhiteSpace(schedule))
        {
            ScheduleTextBox.Text = schedule;
        }

        if (!string.IsNullOrWhiteSpace(duration))
        {
            DurationTextBox.Text = duration;
        }

        if (!string.IsNullOrWhiteSpace(directWindow))
        {
            DirectSapherasWindowTextBox.Text = directWindow;
        }

        if (!string.IsNullOrWhiteSpace(teleport))
        {
            TeleportKeyComboBox.Text = teleport;
        }

        if (!string.IsNullOrWhiteSpace(emergencyTeleport))
        {
            EmergencyTeleportKeyComboBox.Text = emergencyTeleport;
        }

        Ta1ComboBox.SelectedItem = client1Ta == nameof(TaDestination.Ta2) ? "T.A 2" : "T.A 3";
        Ta2ComboBox.SelectedItem = client2Ta == nameof(TaDestination.Ta3) ? "T.A 3" : "T.A 2";
        Client1SapherasCheckBox.IsChecked = !string.Equals(client1Sapheras, "false", StringComparison.OrdinalIgnoreCase);
        Client2SapherasCheckBox.IsChecked = string.Equals(client2Sapheras, "true", StringComparison.OrdinalIgnoreCase);
        EnableClient1CheckBox.IsChecked = !string.Equals(client1Enabled, "false", StringComparison.OrdinalIgnoreCase);
        EnableClient2CheckBox.IsChecked = string.Equals(client2Enabled, "true", StringComparison.OrdinalIgnoreCase);
        DeathThresholdTextBox.Text = string.IsNullOrWhiteSpace(deathThreshold) ? "3" : deathThreshold;
        DeathWindowTextBox.Text = string.IsNullOrWhiteSpace(deathWindow) ? "30" : deathWindow;
        AgendaDurationTextBox.Text = string.IsNullOrWhiteSpace(agendaDuration) ? "60" : agendaDuration;
    }

    private async Task SaveSettingsAsync(BotRunOptions runOptions)
    {
        var options = runOptions.Sapheras;
        var client1 = runOptions.Clients.FirstOrDefault(client => client.Label == "Cliente 1");
        var client2 = runOptions.Clients.FirstOrDefault(client => client.Label == "Cliente 2");
        await _database.SaveSettingAsync("client1.enabled", (client1 is not null).ToString().ToLowerInvariant());
        await _database.SaveSettingAsync("client2.enabled", (client2 is not null).ToString().ToLowerInvariant());
        if (client1 is not null)
        {
            await _database.SaveSettingAsync("client1.title", client1.Target.Title);
            await _database.SaveSettingAsync("client1.ta", client1.Destination.ToString());
            await _database.SaveSettingAsync("client1.sapheras", client1.UseSapheras.ToString().ToLowerInvariant());
        }

        if (client2 is not null)
        {
            await _database.SaveSettingAsync("client2.title", client2.Target.Title);
            await _database.SaveSettingAsync("client2.ta", client2.Destination.ToString());
            await _database.SaveSettingAsync("client2.sapheras", client2.UseSapheras.ToString().ToLowerInvariant());
        }

        await _database.SaveSettingAsync("sapheras.schedule", options.ScheduledAt.ToString("HH:mm:ss"));
        await _database.SaveSettingAsync(
            "sapheras.durationMinutes",
            options.Duration.TotalMinutes.ToString(CultureInfo.InvariantCulture));
        await _database.SaveSettingAsync(
            "sapheras.directWindowMinutes",
            options.DirectSapherasWindow.TotalMinutes.ToString(CultureInfo.InvariantCulture));
        await _database.SaveSettingAsync("sapheras.teleportKey", options.TeleportKeyName);
        await _database.SaveSettingAsync("ta3.emergencyTeleportKey", options.EmergencyTeleportKeyName);
        await _database.SaveSettingAsync(
            "antiOverkill.deathThreshold",
            runOptions.AntiOverkill.DeathThreshold.ToString(CultureInfo.InvariantCulture));
        await _database.SaveSettingAsync(
            "antiOverkill.deathWindowMinutes",
            runOptions.AntiOverkill.DeathWindow.TotalMinutes.ToString(CultureInfo.InvariantCulture));
        await _database.SaveSettingAsync(
            "antiOverkill.agendaDurationMinutes",
            runOptions.AntiOverkill.AgendaDuration.TotalMinutes.ToString(CultureInfo.InvariantCulture));
    }

    private static TaDestination ParseTaDestination(object? selectedItem) =>
        string.Equals(selectedItem?.ToString(), "T.A 2", StringComparison.Ordinal)
            ? TaDestination.Ta2
            : TaDestination.Ta3;

    private static bool TryParseSchedule(string text, out DateTime scheduledAt)
    {
        var formats = new[] { "H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss" };
        if (!DateTime.TryParseExact(
                text.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            scheduledAt = default;
            return false;
        }

        var now = DateTime.Now;
        scheduledAt = now.Date + parsed.TimeOfDay;
        if (scheduledAt < now)
        {
            scheduledAt = now - scheduledAt <= TimeSpan.FromMinutes(2)
                ? now.AddSeconds(2)
                : scheduledAt.AddDays(1);
        }

        return true;
    }

    private static bool TryParseDuration(string text, out TimeSpan duration)
    {
        var parsed = double.TryParse(
            text.Trim(),
            NumberStyles.Float,
            CultureInfo.CurrentCulture,
            out var minutes) ||
            double.TryParse(
                text.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out minutes);
        duration = parsed && minutes > 0 && minutes <= 1440
            ? TimeSpan.FromMinutes(minutes)
            : TimeSpan.Zero;
        return duration > TimeSpan.Zero;
    }

    private static bool TryParseVirtualKey(string keyName, out int virtualKey)
    {
        if (keyName.Length == 1)
        {
            var character = keyName[0];
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = character;
                return true;
            }
        }

        if (keyName.StartsWith('F') &&
            int.TryParse(keyName[1..], out var functionNumber) &&
            functionNumber is >= 1 and <= 12)
        {
            virtualKey = 0x70 + functionNumber - 1;
            return true;
        }

        virtualKey = 0;
        return false;
    }

    private static IReadOnlyList<string> BuildKeyChoices()
    {
        var keys = new List<string>();
        keys.AddRange(Enumerable.Range(0, 10).Select(value => value.ToString(CultureInfo.InvariantCulture)));
        keys.AddRange(Enumerable.Range('A', 26).Select(value => ((char)value).ToString()));
        keys.AddRange(Enumerable.Range(1, 12).Select(value => $"F{value}"));
        return keys;
    }

    private void ShowValidation(string message) =>
        MessageBox.Show(
            this,
            message,
            "PEXBOT — Verifique a configuração",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    protected override void OnClosing(CancelEventArgs e)
    {
        _runCancellation?.Cancel();
        _updateDownloadCancellation?.Cancel();
        _engine.Log -= OnEngineLog;
        _engine.StatusChanged -= OnEngineStatusChanged;
        base.OnClosing(e);
    }
}
