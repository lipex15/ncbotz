using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BotNC.App.Models;
using BotNC.App.Services;

namespace BotNC.App;

public partial class MainWindow : Window
{
    public ObservableCollection<FarmScheduleStepEditor> Client1ScheduleSteps { get; } = [];
    public ObservableCollection<FarmScheduleStepEditor> Client2ScheduleSteps { get; } = [];
    private readonly GameWindowService _gameWindows = new();
    private readonly ScreenCaptureService _capture = new();
    private readonly AppDatabase _database = new();
    private readonly PauseController _pause = new();
    private readonly WindowsInputService _input = new();
    private readonly BotAutomationEngine _engine;
    private readonly AppUpdateService _updates = new();
    private CancellationTokenSource? _runCancellation;
    private CancellationTokenSource? _updateDownloadCancellation;
    private CancellationTokenSource? _scheduledStartCancellation;
    private TaskCompletionSource<bool>? _runFinished;
    private AvailableUpdate? _availableUpdate;
    private bool _updateBusy;
    private bool _databaseReady;
    private bool _environmentReady;
    private BotRunState _stateBeforePause = BotRunState.Waiting;
    private string? _savedClient1Title;
    private string? _savedClient2Title;
    private FarmCoordinate? _client1CustomCoordinate;
    private FarmCoordinate? _client2CustomCoordinate;
    private readonly Dictionary<TaDestination, FarmCoordinate> _client1TaCoordinates = [];
    private readonly Dictionary<TaDestination, FarmCoordinate> _client2TaCoordinates = [];
    private readonly HashSet<TaDestination> _client1TaCoordinatesEnabled = [];
    private readonly HashSet<TaDestination> _client2TaCoordinatesEnabled = [];
    private FarmCoordinate? _client1AbbeyCoordinate;
    private FarmCoordinate? _client2AbbeyCoordinate;
    private bool _capturingCustomCoordinate;
    private string? _lastSeenUpdateVersion;
    private readonly DispatcherTimer _updateCheckTimer = new()
    {
        Interval = TimeSpan.FromMinutes(15)
    };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        var recognition = new VisualRecognitionService(_database, _capture);
        _engine = new BotAutomationEngine(
            _gameWindows,
            _input,
            recognition,
            _capture,
            _database);
        _engine.Log += OnEngineLog;
        _engine.StatusChanged += OnEngineStatusChanged;
        _engine.AudioStatusChanged += OnEngineAudioStatusChanged;

        Ta1ComboBox.ItemsSource = new[] { "T.A 1 (Codex)", "T.A 2", "T.A 3" };
        Ta2ComboBox.ItemsSource = new[] { "T.A 1 (Codex)", "T.A 2", "T.A 3" };
        Ta1ComboBox.SelectedIndex = 2;
        Ta2ComboBox.SelectedIndex = 1;
        GuildDirectiveAreaComboBox.ItemsSource = new[] { "Mapa Aberto", "T.A", "Masmorras" };
        GuildDirectiveAreaComboBox.SelectedIndex = 0;
        var clientScopes = new[] { "Ambos", "Cliente 1", "Cliente 2", "Nenhum" };
        DailyMissionsScopeComboBox.ItemsSource = clientScopes;
        GuildDirectiveScopeComboBox.ItemsSource = clientScopes;
        MailScopeComboBox.ItemsSource = clientScopes;
        DailyShopScopeComboBox.ItemsSource = clientScopes;
        AntiOverkillScopeComboBox.ItemsSource = clientScopes;
        FarmScheduleScopeComboBox.ItemsSource = clientScopes;
        DailyMissionsScopeComboBox.SelectedItem = "Nenhum";
        GuildDirectiveScopeComboBox.SelectedItem = "Nenhum";
        MailScopeComboBox.SelectedItem = "Ambos";
        DailyShopScopeComboBox.SelectedItem = "Nenhum";
        AntiOverkillScopeComboBox.SelectedItem = "Ambos";
        FarmScheduleScopeComboBox.SelectedItem = "Nenhum";
        var scheduleDestinations = new[] { "Abadia da Lembrança", "Masmorra Anônima · Estreito de Tenerys" };
        Client1ScheduleDestinationComboBox.ItemsSource = scheduleDestinations;
        Client2ScheduleDestinationComboBox.ItemsSource = scheduleDestinations;
        var anonymousLevels = new[] { "Nv. 86", "Nv. 97", "Nv. 110" };
        Client1AnonymousLevelComboBox.ItemsSource = anonymousLevels;
        Client2AnonymousLevelComboBox.ItemsSource = anonymousLevels;
        Client1AnonymousLevelComboBox.SelectedItem = "Nv. 97";
        Client2AnonymousLevelComboBox.SelectedItem = "Nv. 97";
        UpdateAnonymousLevelVisibility();
        Client1ScheduleDestinationComboBox.SelectedIndex = 0;
        Client2ScheduleDestinationComboBox.SelectedIndex = 0;
        Client1ScheduleSteps.CollectionChanged += (_, _) => UpdateScheduleTotals();
        Client2ScheduleSteps.CollectionChanged += (_, _) => UpdateScheduleTotals();

        TeleportKeyComboBox.ItemsSource = BuildKeyChoices();
        TeleportKeyComboBox.Text = "E";
        EmergencyTeleportKeyComboBox.ItemsSource = BuildKeyChoices();
        EmergencyTeleportKeyComboBox.Text = "7";
        ScheduleTextBox.Text = "11:50";
        DurationTextBox.Text = "30";
        DeathThresholdTextBox.Text = "3";
        DeathWindowTextBox.Text = "30";
        AgendaDurationTextBox.Text = "60";
        Title = AppIdentity.DisplayName;
        AppNameText.Text = "PEXBOT";
        TestChannelBadge.Visibility = AppIdentity.IsTesting ? Visibility.Visible : Visibility.Collapsed;
        if (AppIdentity.IsTesting)
        {
            UpdatesIntroText.Text = "Canal de testes: atualizações independentes da versão usada pelos demais.";
        }
        InstalledVersionText.Text = $"v{AppUpdateService.CurrentVersion.ToString(3)}";
        ApplicationVersionText.Text = AppIdentity.IsTesting
            ? $"TESTE · v{AppUpdateService.CurrentVersion.ToString(3)}"
            : $"v{AppUpdateService.CurrentVersion.ToString(3)}";
        DatabasePathText.Text = $"Dados: {_database.DatabasePath} · Log: {_engine.RuntimeLogPath}";
        _updateCheckTimer.Tick += async (_, _) => await CheckForUpdatesAsync(userInitiated: false);
    }

    private void OnShowOverview(object sender, RoutedEventArgs e)
    {
        OverviewPanel.Visibility = Visibility.Visible;
        AbbeyPanel.Visibility = Visibility.Collapsed;
        RoutinesPanel.Visibility = Visibility.Collapsed;
        ProtectionPanel.Visibility = Visibility.Collapsed;
        UpdatesPanel.Visibility = Visibility.Collapsed;
        FarmSchedulePanel.Visibility = Visibility.Collapsed;
        OverviewNavigationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#266FEA"));
        UpdatesNavigationButton.Background = Brushes.Transparent;
        AbbeyNavigationButton.Background = Brushes.Transparent;
        RoutinesNavigationButton.Background = Brushes.Transparent;
        ProtectionNavigationButton.Background = Brushes.Transparent;
        FarmScheduleNavigationButton.Background = Brushes.Transparent;
    }

    private void OnSelectBothSapheras(object sender, RoutedEventArgs e)
    {
        Client1SapherasCheckBox.IsChecked = true;
        Client2SapherasCheckBox.IsChecked = true;
    }

    private static bool ScopeIncludesClient(object? selectedScope, int clientNumber) =>
        selectedScope?.ToString() == "Ambos" ||
        selectedScope?.ToString() == $"Cliente {clientNumber}";

    private static string ScopeFromClients(bool client1, bool client2) => (client1, client2) switch
    {
        (true, true) => "Ambos",
        (true, false) => "Cliente 1",
        (false, true) => "Cliente 2",
        _ => "Nenhum"
    };

    private void OnShowSapheras(object sender, RoutedEventArgs e)
    {
        OnShowOverview(sender, e);
        SapherasSettingsSection.BringIntoView();
    }

    private void OnShowFarm(object sender, RoutedEventArgs e)
    {
        OnShowOverview(sender, e);
        OverviewScrollViewer.ScrollToTop();
    }

    private async void OnShowUpdates(object sender, RoutedEventArgs e)
    {
        OverviewPanel.Visibility = Visibility.Collapsed;
        AbbeyPanel.Visibility = Visibility.Collapsed;
        RoutinesPanel.Visibility = Visibility.Collapsed;
        ProtectionPanel.Visibility = Visibility.Collapsed;
        UpdatesPanel.Visibility = Visibility.Visible;
        FarmSchedulePanel.Visibility = Visibility.Collapsed;
        OverviewNavigationButton.Background = Brushes.Transparent;
        AbbeyNavigationButton.Background = Brushes.Transparent;
        RoutinesNavigationButton.Background = Brushes.Transparent;
        ProtectionNavigationButton.Background = Brushes.Transparent;
        FarmScheduleNavigationButton.Background = Brushes.Transparent;
        UpdatesNavigationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#266FEA"));
        UpdatesNotificationDot.Visibility = Visibility.Collapsed;
        if (_availableUpdate is not null)
        {
            _lastSeenUpdateVersion = _availableUpdate.Version.ToString(3);
            await _database.SaveSettingAsync("updates.lastSeenVersion", _lastSeenUpdateVersion);
        }
    }

    private void OnShowAbbey(object sender, RoutedEventArgs e)
    {
        OverviewPanel.Visibility = Visibility.Collapsed;
        RoutinesPanel.Visibility = Visibility.Collapsed;
        UpdatesPanel.Visibility = Visibility.Collapsed;
        ProtectionPanel.Visibility = Visibility.Collapsed;
        FarmSchedulePanel.Visibility = Visibility.Collapsed;
        AbbeyPanel.Visibility = Visibility.Visible;
        OverviewNavigationButton.Background = Brushes.Transparent;
        UpdatesNavigationButton.Background = Brushes.Transparent;
        AbbeyNavigationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#266FEA"));
        RoutinesNavigationButton.Background = Brushes.Transparent;
        ProtectionNavigationButton.Background = Brushes.Transparent;
        FarmScheduleNavigationButton.Background = Brushes.Transparent;
    }

    private void OnShowRoutines(object sender, RoutedEventArgs e)
    {
        OverviewPanel.Visibility = Visibility.Collapsed;
        AbbeyPanel.Visibility = Visibility.Collapsed;
        ProtectionPanel.Visibility = Visibility.Collapsed;
        UpdatesPanel.Visibility = Visibility.Collapsed;
        FarmSchedulePanel.Visibility = Visibility.Collapsed;
        RoutinesPanel.Visibility = Visibility.Visible;
        OverviewNavigationButton.Background = Brushes.Transparent;
        AbbeyNavigationButton.Background = Brushes.Transparent;
        ProtectionNavigationButton.Background = Brushes.Transparent;
        UpdatesNavigationButton.Background = Brushes.Transparent;
        RoutinesNavigationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#266FEA"));
        FarmScheduleNavigationButton.Background = Brushes.Transparent;
    }

    private void OnShowProtection(object sender, RoutedEventArgs e)
    {
        OverviewPanel.Visibility = Visibility.Collapsed;
        AbbeyPanel.Visibility = Visibility.Collapsed;
        RoutinesPanel.Visibility = Visibility.Collapsed;
        UpdatesPanel.Visibility = Visibility.Collapsed;
        FarmSchedulePanel.Visibility = Visibility.Collapsed;
        ProtectionPanel.Visibility = Visibility.Visible;
        OverviewNavigationButton.Background = Brushes.Transparent;
        AbbeyNavigationButton.Background = Brushes.Transparent;
        RoutinesNavigationButton.Background = Brushes.Transparent;
        UpdatesNavigationButton.Background = Brushes.Transparent;
        ProtectionNavigationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#266FEA"));
        FarmScheduleNavigationButton.Background = Brushes.Transparent;
    }

    private void OnShowFarmSchedule(object sender, RoutedEventArgs e)
    {
        OverviewPanel.Visibility = Visibility.Collapsed;
        AbbeyPanel.Visibility = Visibility.Collapsed;
        RoutinesPanel.Visibility = Visibility.Collapsed;
        ProtectionPanel.Visibility = Visibility.Collapsed;
        UpdatesPanel.Visibility = Visibility.Collapsed;
        FarmSchedulePanel.Visibility = Visibility.Visible;
        OverviewNavigationButton.Background = Brushes.Transparent;
        AbbeyNavigationButton.Background = Brushes.Transparent;
        RoutinesNavigationButton.Background = Brushes.Transparent;
        ProtectionNavigationButton.Background = Brushes.Transparent;
        UpdatesNavigationButton.Background = Brushes.Transparent;
        FarmScheduleNavigationButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#266FEA"));
    }

    internal void ShowUpdatesForScreenshot() => OnShowUpdates(this, new RoutedEventArgs());
    internal void ShowAbbeyForScreenshot() => OnShowAbbey(this, new RoutedEventArgs());
    internal void ShowProtectionForScreenshot() => OnShowProtection(this, new RoutedEventArgs());
    internal void ShowRoutinesForScreenshot() => OnShowRoutines(this, new RoutedEventArgs());
    internal void ShowFarmScheduleForScreenshot() => OnShowFarmSchedule(this, new RoutedEventArgs());

    private void OnAddClient1ScheduleStep(object sender, RoutedEventArgs e) =>
        AddScheduleStep(Client1ScheduleSteps, Client1ScheduleDestinationComboBox.SelectedItem,
            Client1ScheduleDurationTextBox.Text, Client1AnonymousLevelComboBox.SelectedItem);

    private void OnAddClient2ScheduleStep(object sender, RoutedEventArgs e) =>
        AddScheduleStep(Client2ScheduleSteps, Client2ScheduleDestinationComboBox.SelectedItem,
            Client2ScheduleDurationTextBox.Text, Client2AnonymousLevelComboBox.SelectedItem);

    private void AddScheduleStep(
        ObservableCollection<FarmScheduleStepEditor> steps,
        object? destination,
        string durationText,
        object? anonymousLevel)
    {
        if (!double.TryParse(durationText, NumberStyles.Float, CultureInfo.CurrentCulture, out var minutes) &&
            !double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out minutes) ||
            minutes is <= 0 or > 10080)
        {
            ShowValidation("Informe uma duração entre 1 e 10080 minutos para a etapa.");
            return;
        }

        var parsedDestination = ParseFarmScheduleDestination(destination);
        var level = parsedDestination == FarmScheduleDestination.AnonymousDungeon &&
            int.TryParse(anonymousLevel?.ToString()?.Replace("Nv.", "", StringComparison.OrdinalIgnoreCase).Trim(), out var parsedLevel) && parsedLevel is 86 or 97 or 110
                ? parsedLevel
                : 97;
        steps.Add(new FarmScheduleStepEditor(parsedDestination, minutes, level));
    }

    private void OnScheduleDestinationChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        UpdateAnonymousLevelVisibility();

    private void UpdateAnonymousLevelVisibility()
    {
        if (Client1AnonymousLevelComboBox is null || Client2AnonymousLevelComboBox is null)
            return;
        Client1AnonymousLevelComboBox.Visibility = ParseFarmScheduleDestination(Client1ScheduleDestinationComboBox?.SelectedItem) == FarmScheduleDestination.AnonymousDungeon
            ? Visibility.Visible : Visibility.Collapsed;
        Client2AnonymousLevelComboBox.Visibility = ParseFarmScheduleDestination(Client2ScheduleDestinationComboBox?.SelectedItem) == FarmScheduleDestination.AnonymousDungeon
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateScheduleTotals()
    {
        if (Client1ScheduleTotalText is null || Client2ScheduleTotalText is null)
            return;
        Client1ScheduleTotalText.Text = $"Tempo total: {Client1ScheduleSteps.Sum(step => step.DurationMinutes):0} min";
        Client2ScheduleTotalText.Text = $"Tempo total: {Client2ScheduleSteps.Sum(step => step.DurationMinutes):0} min";
    }

    private async void OnSaveFarmSchedule(object sender, RoutedEventArgs e)
    {
        if (!_databaseReady || _runCancellation is not null)
            return;
        try
        {
            await _database.SaveSettingAsync("farmSchedule.scope", FarmScheduleScopeComboBox.SelectedItem?.ToString() ?? "Nenhum");
            await _database.SaveSettingAsync("farmSchedule.client1", JsonSerializer.Serialize(
                Client1ScheduleSteps.Select(step => step.ToModel()).ToArray()));
            await _database.SaveSettingAsync("farmSchedule.client2", JsonSerializer.Serialize(
                Client2ScheduleSteps.Select(step => step.ToModel()).ToArray()));
            AddLog("Agenda de farm salva para os clientes selecionados.");
        }
        catch (Exception exception)
        {
            ShowValidation($"Não foi possível salvar a agenda: {exception.GetBaseException().Message}");
        }
    }

    private void OnRemoveClient1ScheduleStep(object sender, RoutedEventArgs e) =>
        RemoveScheduleStep(Client1ScheduleSteps, Client1ScheduleListBox.SelectedIndex);

    private void OnRemoveClient2ScheduleStep(object sender, RoutedEventArgs e) =>
        RemoveScheduleStep(Client2ScheduleSteps, Client2ScheduleListBox.SelectedIndex);

    private void OnMoveClient1ScheduleUp(object sender, RoutedEventArgs e) =>
        MoveScheduleStep(Client1ScheduleSteps, Client1ScheduleListBox.SelectedIndex, -1);

    private void OnMoveClient1ScheduleDown(object sender, RoutedEventArgs e) =>
        MoveScheduleStep(Client1ScheduleSteps, Client1ScheduleListBox.SelectedIndex, 1);

    private void OnMoveClient2ScheduleUp(object sender, RoutedEventArgs e) =>
        MoveScheduleStep(Client2ScheduleSteps, Client2ScheduleListBox.SelectedIndex, -1);

    private void OnMoveClient2ScheduleDown(object sender, RoutedEventArgs e) =>
        MoveScheduleStep(Client2ScheduleSteps, Client2ScheduleListBox.SelectedIndex, 1);

    private static void RemoveScheduleStep(ObservableCollection<FarmScheduleStepEditor> steps, int index)
    {
        if (index >= 0 && index < steps.Count)
        {
            steps.RemoveAt(index);
        }
    }

    private static void MoveScheduleStep(ObservableCollection<FarmScheduleStepEditor> steps, int index, int offset)
    {
        var target = index + offset;
        if (index >= 0 && index < steps.Count && target >= 0 && target < steps.Count)
        {
            steps.Move(index, target);
        }
    }

    private async void OnCheckForUpdates(object sender, RoutedEventArgs e) =>
        await CheckForUpdatesAsync(userInitiated: true);

    private async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (_updateBusy)
        {
            return;
        }

        _updateBusy = true;
        CheckUpdatesButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        var previousStatus = UpdateStatusText.Text;
        if (userInitiated || _availableUpdate is null)
        {
            UpdateStatusText.Text = "Consultando versões publicadas…";
        }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var checkedUpdate = await _updates.CheckAsync(timeout.Token);
            _availableUpdate = checkedUpdate;
            UpdateStatusText.Text = _availableUpdate is null
                ? "Nenhuma atualização disponível. Sua versão está em dia."
                : $"Versão v{_availableUpdate.Version.ToString(3)} disponível para instalar.";
            InstallUpdateButton.IsEnabled = _availableUpdate is not null;
            if (_availableUpdate is not null && UpdatesPanel.Visibility == Visibility.Visible)
            {
                _lastSeenUpdateVersion = _availableUpdate.Version.ToString(3);
                await _database.SaveSettingAsync("updates.lastSeenVersion", _lastSeenUpdateVersion);
            }

            UpdatesNotificationDot.Visibility = _availableUpdate is not null &&
                !string.Equals(_lastSeenUpdateVersion, _availableUpdate.Version.ToString(3), StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = userInitiated
                ? $"Não foi possível verificar: {exception.GetBaseException().Message}"
                : _availableUpdate is not null
                    ? previousStatus
                    : "Verificação automática indisponível; o aplicativo tentará novamente.";
        }
        finally
        {
            _updateBusy = false;
            CheckUpdatesButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = _availableUpdate is not null;
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
            UpdateStatusText.Text = "Pacote conferido. O PEXBOT será atualizado e reaberto automaticamente…";
            if (_runFinished is { } activeRun)
            {
                _runCancellation?.Cancel();
                await activeRun.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }

            AppUpdateService.LaunchSilentUpdate(installer);
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
            ValidateEnvironment();
            _environmentReady = true;
            await LoadSettingsAsync();
            _lastSeenUpdateVersion = await _database.GetSettingAsync("updates.lastSeenVersion");
            RefreshClients();
            SetStatus(BotRunState.Stopped, "Bot parado", "Configure o módulo e clique em Iniciar.");
            AddLog("PEXBOT iniciado.");
            AddLog("Banco de imagens carregado com sucesso.");
            AddLog("Ambiente validado: Windows, captura visual e resolução compatíveis.");
            AddLog("O instalador do PEXBOT já inclui o runtime necessário; nenhuma instalação adicional é exigida.");
            _ = CheckForUpdatesAsync(userInitiated: false);
            _updateCheckTimer.Start();
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
        UpdateCustomCoordinateControls();
    }

    private void OnTaDestinationChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_databaseReady || Client1CustomCoordinateCheckBox is null || Client2CustomCoordinateCheckBox is null)
        {
            return;
        }

        if (sender == Ta1ComboBox)
        {
            var destination = ParseTaDestination(Ta1ComboBox.SelectedItem);
            _client1CustomCoordinate = _client1TaCoordinates.GetValueOrDefault(destination);
            Client1CustomCoordinateCheckBox.IsChecked = _client1TaCoordinatesEnabled.Contains(destination);
        }
        else
        {
            var destination = ParseTaDestination(Ta2ComboBox.SelectedItem);
            _client2CustomCoordinate = _client2TaCoordinates.GetValueOrDefault(destination);
            Client2CustomCoordinateCheckBox.IsChecked = _client2TaCoordinatesEnabled.Contains(destination);
        }

        UpdateCustomCoordinateLabels();
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
        UpdateCustomCoordinateControls();
    }

    private async void OnCustomCoordinateModeChanged(object sender, RoutedEventArgs e)
    {
        UpdateCustomCoordinateControls();
        if (!_databaseReady)
        {
            return;
        }

        try
        {
            if (sender == Client1CustomCoordinateCheckBox)
            {
                var destination = ParseTaDestination(Ta1ComboBox.SelectedItem);
                if (Client1CustomCoordinateCheckBox.IsChecked == true)
                    _client1TaCoordinatesEnabled.Add(destination);
                else
                    _client1TaCoordinatesEnabled.Remove(destination);
                await _database.SaveSettingAsync(
                    $"client1.customFarm.{destination}.enabled",
                    (Client1CustomCoordinateCheckBox.IsChecked == true).ToString().ToLowerInvariant());
            }
            else if (sender == Client2CustomCoordinateCheckBox)
            {
                var destination = ParseTaDestination(Ta2ComboBox.SelectedItem);
                if (Client2CustomCoordinateCheckBox.IsChecked == true)
                    _client2TaCoordinatesEnabled.Add(destination);
                else
                    _client2TaCoordinatesEnabled.Remove(destination);
                await _database.SaveSettingAsync(
                    $"client2.customFarm.{destination}.enabled",
                    (Client2CustomCoordinateCheckBox.IsChecked == true).ToString().ToLowerInvariant());
            }
        }
        catch (Exception exception)
        {
            AddLog($"Não foi possível salvar o modo de coordenada personalizada: {exception.GetBaseException().Message}");
        }
    }

    private async void OnCaptureClient1Coordinate(object sender, RoutedEventArgs e) =>
        await CaptureCustomCoordinateAsync(1);

    private async void OnCaptureClient2Coordinate(object sender, RoutedEventArgs e) =>
        await CaptureCustomCoordinateAsync(2);

    private async void OnCaptureClient1AbbeyCoordinate(object sender, RoutedEventArgs e) =>
        await CaptureCustomCoordinateAsync(1, abbey: true);

    private async void OnCaptureClient2AbbeyCoordinate(object sender, RoutedEventArgs e) =>
        await CaptureCustomCoordinateAsync(2, abbey: true);

    private async Task CaptureCustomCoordinateAsync(int clientNumber, bool abbey = false)
    {
        if (_capturingCustomCoordinate || _runCancellation is not null)
        {
            return;
        }

        if (!_databaseReady)
        {
            ShowValidation("Aguarde o carregamento das configurações antes de capturar o ponto.");
            return;
        }

        var target = clientNumber == 1
            ? Client1ComboBox.SelectedItem as GameWindowTarget
            : Client2ComboBox.SelectedItem as GameWindowTarget;
        if (target is null)
        {
            ShowValidation($"Selecione a janela do Cliente {clientNumber} antes de capturar a coordenada.");
            return;
        }

        _capturingCustomCoordinate = true;
        UpdateCustomCoordinateControls();
        AddLog(
            $"Cliente {clientNumber}: captura iniciada. Clique no ponto desejado do mapa dentro da janela do jogo.");
        try
        {
            await Task.Delay(250);
            WindowState = WindowState.Minimized;
            await Task.Delay(300);
            if (!_gameWindows.Activate(target))
            {
                throw new InvalidOperationException($"Não foi possível trazer {target.Title} para frente.");
            }

            var clicked = await WindowsInputService.CaptureNextLeftClickAsync(
                TimeSpan.FromSeconds(60),
                CancellationToken.None);
            var mapped = _gameWindows.MapScreenPointToReference(target, clicked.X, clicked.Y);
            var coordinate = new FarmCoordinate(mapped.X, mapped.Y);
            if (abbey && clientNumber == 1)
            {
                _client1AbbeyCoordinate = coordinate;
                Client1AbbeyCustomCheckBox.IsChecked = true;
            }
            else if (abbey)
            {
                _client2AbbeyCoordinate = coordinate;
                Client2AbbeyCustomCheckBox.IsChecked = true;
            }
            else if (clientNumber == 1)
            {
                _client1CustomCoordinate = coordinate;
                _client1TaCoordinates[ParseTaDestination(Ta1ComboBox.SelectedItem)] = coordinate;
                Client1CustomCoordinateCheckBox.IsChecked = true;
            }
            else
            {
                _client2CustomCoordinate = coordinate;
                _client2TaCoordinates[ParseTaDestination(Ta2ComboBox.SelectedItem)] = coordinate;
                Client2CustomCoordinateCheckBox.IsChecked = true;
            }

            await SaveCustomCoordinateAsync(clientNumber, coordinate, enabled: true, abbey: abbey);
            UpdateCustomCoordinateLabels();
            AddLog($"Cliente {clientNumber}: coordenada personalizada salva em ({coordinate.X}, {coordinate.Y}).");
        }
        catch (Exception exception)
        {
            ShowValidation($"A coordenada não foi capturada: {exception.GetBaseException().Message}");
            AddLog($"Cliente {clientNumber}: captura de coordenada cancelada ou inválida.");
        }
        finally
        {
            WindowState = WindowState.Normal;
            Activate();
            _capturingCustomCoordinate = false;
            UpdateCustomCoordinateControls();
        }
    }

    private async Task SaveCustomCoordinateAsync(
        int clientNumber,
        FarmCoordinate coordinate,
        bool enabled,
        bool abbey = false)
    {
        var destination = clientNumber == 1
            ? ParseTaDestination(Ta1ComboBox.SelectedItem)
            : ParseTaDestination(Ta2ComboBox.SelectedItem);
        var prefix = abbey ? $"client{clientNumber}.abbey.customFarm" : $"client{clientNumber}.customFarm.{destination}";
        await _database.SaveSettingAsync($"{prefix}.enabled", enabled.ToString().ToLowerInvariant());
        await _database.SaveSettingAsync($"{prefix}.x", coordinate.X.ToString(CultureInfo.InvariantCulture));
        await _database.SaveSettingAsync($"{prefix}.y", coordinate.Y.ToString(CultureInfo.InvariantCulture));
    }

    private void UpdateCustomCoordinateLabels()
    {
        if (Client1CustomCoordinateText is null || Client2CustomCoordinateText is null)
        {
            return;
        }

        Client1CustomCoordinateText.Text = _client1CustomCoordinate is { } client1
            ? $"({client1.X}, {client1.Y})"
            : "Não definida";
        Client2CustomCoordinateText.Text = _client2CustomCoordinate is { } client2
            ? $"({client2.X}, {client2.Y})"
            : "Não definida";
        Client1AbbeyCoordinateText.Text = _client1AbbeyCoordinate is { } abbey1
            ? $"({abbey1.X}, {abbey1.Y})" : "Não definida";
        Client2AbbeyCoordinateText.Text = _client2AbbeyCoordinate is { } abbey2
            ? $"({abbey2.X}, {abbey2.Y})" : "Não definida";
    }

    private void UpdateCustomCoordinateControls()
    {
        if (Client1CustomCoordinateCheckBox is null || Client2CustomCoordinateCheckBox is null ||
            CaptureClient1CoordinateButton is null || CaptureClient2CoordinateButton is null)
        {
            return;
        }

        var editable = _runCancellation is null && !_capturingCustomCoordinate;
        var client1Enabled = EnableClient1CheckBox?.IsChecked == true;
        var client2Enabled = EnableClient2CheckBox?.IsChecked == true;
        Client1CustomCoordinateCheckBox.IsEnabled = editable && client1Enabled;
        Client2CustomCoordinateCheckBox.IsEnabled = editable && client2Enabled;
        CaptureClient1CoordinateButton.IsEnabled = editable && client1Enabled;
        CaptureClient2CoordinateButton.IsEnabled = editable && client2Enabled;
        if (Client1AbbeyCheckBox is not null && Client2AbbeyCheckBox is not null)
        {
            Client1AbbeyCheckBox.IsEnabled = editable && client1Enabled;
            Client2AbbeyCheckBox.IsEnabled = editable && client2Enabled;
            Client1AbbeyCustomCheckBox.IsEnabled = editable && client1Enabled;
            Client2AbbeyCustomCheckBox.IsEnabled = editable && client2Enabled;
            CaptureClient1AbbeyButton.IsEnabled = editable && client1Enabled;
            CaptureClient2AbbeyButton.IsEnabled = editable && client2Enabled;
            Client1AbbeyLimitTextBox.IsEnabled = editable && client1Enabled;
            Client2AbbeyLimitTextBox.IsEnabled = editable && client2Enabled;
        }
    }

    private void OnUseNow(object sender, RoutedEventArgs e)
    {
        ScheduleTextBox.Text = DateTime.Now.AddSeconds(5).ToString("HH:mm:ss");
    }

    private async void OnStartBot(object sender, RoutedEventArgs e)
    {
        CancelScheduledStart();
        if (!_databaseReady)
        {
            ShowValidation("O banco visual ainda não terminou de carregar.");
            return;
        }

        if (!_environmentReady)
        {
            ShowValidation("O ambiente do Windows não passou pela validação inicial.");
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

        if (client1 is not null && Client1CustomCoordinateCheckBox.IsChecked == true &&
            _client1CustomCoordinate is null)
        {
            ShowValidation("A coordenada personalizada do Cliente 1 está ativa, mas ainda não foi capturada.");
            return;
        }

        if (client2 is not null && Client2CustomCoordinateCheckBox.IsChecked == true &&
            _client2CustomCoordinate is null)
        {
            ShowValidation("A coordenada personalizada do Cliente 2 está ativa, mas ainda não foi capturada.");
            return;
        }

        if (client1 is not null && ParseTaDestination(Ta1ComboBox.SelectedItem) == TaDestination.Ta1Codex &&
            (Client1CustomCoordinateCheckBox.IsChecked != true || _client1CustomCoordinate is null))
        {
            ShowValidation("A T.A 1 (Codex) exige um ponto personalizado capturado no mapa com zoom mínimo.");
            return;
        }

        if (client2 is not null && ParseTaDestination(Ta2ComboBox.SelectedItem) == TaDestination.Ta1Codex &&
            (Client2CustomCoordinateCheckBox.IsChecked != true || _client2CustomCoordinate is null))
        {
            ShowValidation("A T.A 1 (Codex) exige um ponto personalizado capturado no mapa com zoom mínimo.");
            return;
        }

        if (client1 is not null && Client1AbbeyCheckBox.IsChecked == true &&
            Client1AbbeyCustomCheckBox.IsChecked == true && _client1AbbeyCoordinate is null)
        {
            ShowValidation("Capture o ponto personalizado da Abadia para o Cliente 1.");
            return;
        }

        if (client2 is not null && Client2AbbeyCheckBox.IsChecked == true &&
            Client2AbbeyCustomCheckBox.IsChecked == true && _client2AbbeyCoordinate is null)
        {
            ShowValidation("Capture o ponto personalizado da Abadia para o Cliente 2.");
            return;
        }

        if (!int.TryParse(Client1AbbeyLimitTextBox.Text, out var abbeyLimit1) || abbeyLimit1 is < 1 or > 100 ||
            !int.TryParse(Client2AbbeyLimitTextBox.Text, out var abbeyLimit2) || abbeyLimit2 is < 1 or > 100)
        {
            ShowValidation("Informe de 1 a 100 entradas pagas semanais pela Agenda para cada cliente.");
            return;
        }

        foreach (var selectedClient in new[] { client1, client2 }.Where(client => client is not null))
        {
            var size = _gameWindows.GetWindowSize(selectedClient!);
            if (size.Width <= 0 || size.Height <= 0)
            {
                ShowValidation($"A janela {selectedClient!.Title} não está mais disponível. Atualize a lista de clientes.");
                return;
            }
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

        if (!TryParseClock(DailyMissionsTimeTextBox.Text, out var dailyMissionsAt) ||
            !TryParseClock(GuildDirectiveTimeTextBox.Text, out var guildDirectiveAt) ||
            !TryParseClock(DailyShopTimeTextBox.Text, out var dailyShopAt))
        {
            ShowValidation("Informe os horários das rotinas no formato HH:mm.");
            return;
        }

        if ((client1 is not null && ScopeIncludesClient(FarmScheduleScopeComboBox.SelectedItem, 1) && Client1ScheduleSteps.Count == 0) ||
            (client2 is not null && ScopeIncludesClient(FarmScheduleScopeComboBox.SelectedItem, 2) && Client2ScheduleSteps.Count == 0))
        {
            ShowValidation("Adicione ao menos uma etapa da Agenda para cada cliente ativo.");
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
                1,
                Client1CustomCoordinateCheckBox.IsChecked == true
                    ? _client1CustomCoordinate
                    : null,
                Client1AbbeyCheckBox.IsChecked == true,
                abbeyLimit1,
                Client1AbbeyCustomCheckBox.IsChecked == true ? _client1AbbeyCoordinate : null,
                ScopeIncludesClient(FarmScheduleScopeComboBox.SelectedItem, 1),
                ScopeIncludesClient(DailyMissionsScopeComboBox.SelectedItem, 1),
                ScopeIncludesClient(GuildDirectiveScopeComboBox.SelectedItem, 1),
                ScopeIncludesClient(MailScopeComboBox.SelectedItem, 1),
                ScopeIncludesClient(AntiOverkillScopeComboBox.SelectedItem, 1),
                ScopeIncludesClient(DailyShopScopeComboBox.SelectedItem, 1),
                _client1TaCoordinates.Where(pair => _client1TaCoordinatesEnabled.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value)));
        }

        if (client2 is not null)
        {
            clients.Add(
                new AutomationClientOptions(
                    "Cliente 2",
                    client2,
                    ParseTaDestination(Ta2ComboBox.SelectedItem),
                    Client2SapherasCheckBox.IsChecked == true,
                    client1 is null ? 1 : 2,
                    Client2CustomCoordinateCheckBox.IsChecked == true
                        ? _client2CustomCoordinate
                        : null,
                    Client2AbbeyCheckBox.IsChecked == true,
                    abbeyLimit2,
                    Client2AbbeyCustomCheckBox.IsChecked == true ? _client2AbbeyCoordinate : null,
                    ScopeIncludesClient(FarmScheduleScopeComboBox.SelectedItem, 2),
                    ScopeIncludesClient(DailyMissionsScopeComboBox.SelectedItem, 2),
                    ScopeIncludesClient(GuildDirectiveScopeComboBox.SelectedItem, 2),
                    ScopeIncludesClient(MailScopeComboBox.SelectedItem, 2),
                    ScopeIncludesClient(AntiOverkillScopeComboBox.SelectedItem, 2),
                    ScopeIncludesClient(DailyShopScopeComboBox.SelectedItem, 2),
                    _client2TaCoordinates.Where(pair => _client2TaCoordinatesEnabled.Contains(pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value)));
        }

        var antiOverkill = new AntiOverkillOptions(deathThreshold, deathWindow, agendaDuration);
        var dailyRoutines = new DailyRoutineOptions(
            clients.Any(client => client.EnableDailyMissions),
            dailyMissionsAt,
            clients.Any(client => client.EnableGuildDirective),
            guildDirectiveAt,
            ParseGuildDirectiveArea(GuildDirectiveAreaComboBox.SelectedItem),
            clients.Any(client => client.EnableDailyShop),
            dailyShopAt);
        var farmSchedule = new FarmScheduleOptions(
            clients.Any(client => client.UseFarmSchedule),
            Client1ScheduleSteps.Select(item => item.ToModel()).ToArray(),
            Client2ScheduleSteps.Select(item => item.ToModel()).ToArray());
        var runOptions = new BotRunOptions(options, antiOverkill, dailyRoutines, farmSchedule, clients);
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
        CancelScheduledStart();
        _runCancellation?.Cancel();
    }

    private async void OnScheduleStart(object sender, RoutedEventArgs e)
    {
        if (_scheduledStartCancellation is not null)
        {
            CancelScheduledStart();
            SetStatus(BotRunState.Stopped, "Início programado cancelado", "O bot não será iniciado automaticamente.");
            return;
        }

        if (_runCancellation is not null || !_databaseReady || !_environmentReady)
        {
            ShowValidation("Aguarde o aplicativo estar pronto e o bot parado para programar o início.");
            return;
        }

        if (!int.TryParse(StartDelayMinutesTextBox.Text.Trim(), out var minutes) || minutes is < 0 or > 1440)
        {
            ShowValidation("Informe de 0 a 1440 minutos para programar o início.");
            return;
        }

        try
        {
            await _database.SaveSettingAsync(
                "bot.startDelayMinutes", minutes.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception exception)
        {
            ShowValidation($"Não foi possível salvar o agendamento: {exception.GetBaseException().Message}");
            return;
        }
        if (minutes == 0)
        {
            OnStartBot(StartButton, new RoutedEventArgs());
            return;
        }
        var cancellation = new CancellationTokenSource();
        _scheduledStartCancellation = cancellation;
        ScheduleStartButton.Content = "Cancelar início";
        StartDelayMinutesTextBox.IsEnabled = false;
        var startsAt = DateTime.Now.AddMinutes(minutes);
        AddLog($"Início programado para {startsAt:HH:mm:ss} ({minutes} min).");
        try
        {
            while (DateTime.Now < startsAt)
            {
                var remaining = startsAt - DateTime.Now;
                SetStatus(
                    BotRunState.Waiting,
                    "Início programado",
                    $"O bot começará às {startsAt:HH:mm:ss} (faltam {remaining:hh\\:mm\\:ss}).");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
            }

            if (!cancellation.IsCancellationRequested)
            {
                CancelScheduledStart();
                OnStartBot(StartButton, new RoutedEventArgs());
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    private void CancelScheduledStart()
    {
        var cancellation = _scheduledStartCancellation;
        _scheduledStartCancellation = null;
        cancellation?.Cancel();
        if (ScheduleStartButton is not null)
        {
            ScheduleStartButton.Content = "Programar início";
            ScheduleStartButton.IsEnabled = _runCancellation is null;
            StartDelayMinutesTextBox.IsEnabled = _runCancellation is null;
        }
    }

    private void OnClearLog(object sender, RoutedEventArgs e) => LogListBox.Items.Clear();

    private async void OnCopyLog(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _engine.RuntimeLogPath;
            string content;
            if (File.Exists(path))
            {
                await using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                content = await reader.ReadToEndAsync();
            }
            else
            {
                content = string.Join(
                    Environment.NewLine,
                    LogListBox.Items.Cast<object>().Select(item => item.ToString()));
            }

            Clipboard.SetText(content);
            CopyLogButton.Content = "Copiado!";
            await Task.Delay(1800);
            CopyLogButton.Content = "Copiar log";
        }
        catch (Exception exception)
        {
            CopyLogButton.Content = "Falhou";
            AddLog($"Não foi possível copiar o log: {exception.GetBaseException().Message}");
        }
    }

    private void ValidateEnvironment()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            throw new PlatformNotSupportedException(
                "O PEXBOT requer Windows 10 versão 2004 ou mais recente, ou Windows 11.");
        }

        var (width, height) = _capture.GetPrimaryScreenSize();
        if (width != 1920 || height != 1080)
        {
            throw new InvalidOperationException(
                $"A resolução precisa ser 1920×1080. Detectado: {width}×{height}. Ajuste a tela do Windows antes de iniciar o bot.");
        }

        var scale = GameWindowService.GetSystemScalePercent();
        if (scale != 100)
        {
            throw new InvalidOperationException(
                $"A escala do Windows precisa estar em 100%. Detectado: {scale}%. Ajuste a escala antes de iniciar o bot.");
        }

        if (!GameWindowCaptureSession.IsCaptureSupported)
        {
            throw new PlatformNotSupportedException(
                "A captura individual das janelas não está disponível neste Windows. Instale as atualizações do Windows e tente novamente.");
        }
    }

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
        DailyMissionsScopeComboBox.IsEnabled = !isRunning;
        DailyMissionsTimeTextBox.IsEnabled = !isRunning;
        GuildDirectiveScopeComboBox.IsEnabled = !isRunning;
        MailScopeComboBox.IsEnabled = !isRunning;
        DailyShopScopeComboBox.IsEnabled = !isRunning;
        DailyShopTimeTextBox.IsEnabled = !isRunning;
        AntiOverkillScopeComboBox.IsEnabled = !isRunning;
        GuildDirectiveTimeTextBox.IsEnabled = !isRunning;
        GuildDirectiveAreaComboBox.IsEnabled = !isRunning;
        FarmScheduleScopeComboBox.IsEnabled = !isRunning;
        FarmScheduleEditorGrid.IsEnabled = !isRunning;
        Client1AbbeyLimitTextBox.IsEnabled = !isRunning && EnableClient1CheckBox.IsChecked == true;
        Client2AbbeyLimitTextBox.IsEnabled = !isRunning && EnableClient2CheckBox.IsChecked == true;
        ScheduleStartButton.IsEnabled = !isRunning;
        StartDelayMinutesTextBox.IsEnabled = !isRunning && _scheduledStartCancellation is null;
        UpdateCustomCoordinateControls();
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
        var startDelay = await _database.GetSettingAsync("bot.startDelayMinutes");
        var client1CustomEnabled = await _database.GetSettingAsync("client1.customFarm.enabled");
        var client1CustomX = await _database.GetSettingAsync("client1.customFarm.x");
        var client1CustomY = await _database.GetSettingAsync("client1.customFarm.y");
        var client2CustomEnabled = await _database.GetSettingAsync("client2.customFarm.enabled");
        var client2CustomX = await _database.GetSettingAsync("client2.customFarm.x");
        var client2CustomY = await _database.GetSettingAsync("client2.customFarm.y");
        await LoadTaCoordinatesAsync(1, _client1TaCoordinates, _client1TaCoordinatesEnabled);
        await LoadTaCoordinatesAsync(2, _client2TaCoordinates, _client2TaCoordinatesEnabled);
        var abbey1Enabled = await _database.GetSettingAsync("client1.abbey.enabled");
        var abbey2Enabled = await _database.GetSettingAsync("client2.abbey.enabled");
        var abbey1Limit = await _database.GetSettingAsync("client1.farmSchedule.weeklyEntryLimit");
        var abbey2Limit = await _database.GetSettingAsync("client2.farmSchedule.weeklyEntryLimit");
        if (string.IsNullOrWhiteSpace(abbey1Limit) &&
            int.TryParse(await _database.GetSettingAsync("client1.abbey.returnLimit"), out var legacyAbbey1Limit))
            abbey1Limit = (Math.Max(0, legacyAbbey1Limit) + 1).ToString(CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(abbey2Limit) &&
            int.TryParse(await _database.GetSettingAsync("client2.abbey.returnLimit"), out var legacyAbbey2Limit))
            abbey2Limit = (Math.Max(0, legacyAbbey2Limit) + 1).ToString(CultureInfo.InvariantCulture);
        var abbey1CustomEnabled = await _database.GetSettingAsync("client1.abbey.customFarm.enabled");
        var abbey2CustomEnabled = await _database.GetSettingAsync("client2.abbey.customFarm.enabled");
        var abbey1X = await _database.GetSettingAsync("client1.abbey.customFarm.x");
        var abbey1Y = await _database.GetSettingAsync("client1.abbey.customFarm.y");
        var abbey2X = await _database.GetSettingAsync("client2.abbey.customFarm.x");
        var abbey2Y = await _database.GetSettingAsync("client2.abbey.customFarm.y");
        var dailyEnabled = await _database.GetSettingAsync("routines.daily.enabled");
        var dailyScope = await _database.GetSettingAsync("routines.daily.scope");
        var dailyTime = await _database.GetSettingAsync("routines.daily.time");
        var directiveEnabled = await _database.GetSettingAsync("routines.directive.enabled");
        var directiveScope = await _database.GetSettingAsync("routines.directive.scope");
        var mailScope = await _database.GetSettingAsync("routines.mail.scope");
        var dailyShopScope = await _database.GetSettingAsync("routines.dailyShop.scope");
        var dailyShopTime = await _database.GetSettingAsync("routines.dailyShop.time");
        var antiOverkillScope = await _database.GetSettingAsync("antiOverkill.scope");
        var directiveTime = await _database.GetSettingAsync("routines.directive.time");
        var directiveArea = await _database.GetSettingAsync("routines.directive.area");
        var farmScheduleEnabled = await _database.GetSettingAsync("farmSchedule.enabled");
        var farmScheduleScope = await _database.GetSettingAsync("farmSchedule.scope");
        var client1FarmSchedule = await _database.GetSettingAsync("farmSchedule.client1");
        var client2FarmSchedule = await _database.GetSettingAsync("farmSchedule.client2");
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

        Ta1ComboBox.SelectedItem = TaDisplayName(client1Ta, "T.A 3");
        Ta2ComboBox.SelectedItem = TaDisplayName(client2Ta, "T.A 2");
        Client1SapherasCheckBox.IsChecked = !string.Equals(client1Sapheras, "false", StringComparison.OrdinalIgnoreCase);
        Client2SapherasCheckBox.IsChecked = string.Equals(client2Sapheras, "true", StringComparison.OrdinalIgnoreCase);
        EnableClient1CheckBox.IsChecked = !string.Equals(client1Enabled, "false", StringComparison.OrdinalIgnoreCase);
        EnableClient2CheckBox.IsChecked = string.Equals(client2Enabled, "true", StringComparison.OrdinalIgnoreCase);
        DeathThresholdTextBox.Text = string.IsNullOrWhiteSpace(deathThreshold) ? "3" : deathThreshold;
        DeathWindowTextBox.Text = string.IsNullOrWhiteSpace(deathWindow) ? "30" : deathWindow;
        AgendaDurationTextBox.Text = string.IsNullOrWhiteSpace(agendaDuration) ? "60" : agendaDuration;
        StartDelayMinutesTextBox.Text = string.IsNullOrWhiteSpace(startDelay) ? "0" : startDelay;
        var selectedTa1 = ParseTaDestination(Ta1ComboBox.SelectedItem);
        var selectedTa2 = ParseTaDestination(Ta2ComboBox.SelectedItem);
        _client1CustomCoordinate = _client1TaCoordinates.GetValueOrDefault(selectedTa1) ?? ParseFarmCoordinate(client1CustomX, client1CustomY);
        _client2CustomCoordinate = _client2TaCoordinates.GetValueOrDefault(selectedTa2) ?? ParseFarmCoordinate(client2CustomX, client2CustomY);
        if (_client1CustomCoordinate is not null) _client1TaCoordinates.TryAdd(selectedTa1, _client1CustomCoordinate);
        if (_client2CustomCoordinate is not null) _client2TaCoordinates.TryAdd(selectedTa2, _client2CustomCoordinate);
        if (_client1CustomCoordinate is not null && string.Equals(client1CustomEnabled, "true", StringComparison.OrdinalIgnoreCase))
            _client1TaCoordinatesEnabled.Add(selectedTa1);
        if (_client2CustomCoordinate is not null && string.Equals(client2CustomEnabled, "true", StringComparison.OrdinalIgnoreCase))
            _client2TaCoordinatesEnabled.Add(selectedTa2);
        Client1CustomCoordinateCheckBox.IsChecked =
            _client1CustomCoordinate is not null &&
            (_client1TaCoordinatesEnabled.Contains(selectedTa1) ||
             string.Equals(client1CustomEnabled, "true", StringComparison.OrdinalIgnoreCase));
        Client2CustomCoordinateCheckBox.IsChecked =
            _client2CustomCoordinate is not null &&
            (_client2TaCoordinatesEnabled.Contains(selectedTa2) ||
             string.Equals(client2CustomEnabled, "true", StringComparison.OrdinalIgnoreCase));
        Client1AbbeyCheckBox.IsChecked = string.Equals(abbey1Enabled, "true", StringComparison.OrdinalIgnoreCase);
        Client2AbbeyCheckBox.IsChecked = string.Equals(abbey2Enabled, "true", StringComparison.OrdinalIgnoreCase);
        Client1AbbeyLimitTextBox.Text = string.IsNullOrWhiteSpace(abbey1Limit) ? "1" : abbey1Limit;
        Client2AbbeyLimitTextBox.Text = string.IsNullOrWhiteSpace(abbey2Limit) ? "1" : abbey2Limit;
        _client1AbbeyCoordinate = ParseFarmCoordinate(abbey1X, abbey1Y);
        _client2AbbeyCoordinate = ParseFarmCoordinate(abbey2X, abbey2Y);
        Client1AbbeyCustomCheckBox.IsChecked = _client1AbbeyCoordinate is not null &&
            string.Equals(abbey1CustomEnabled, "true", StringComparison.OrdinalIgnoreCase);
        Client2AbbeyCustomCheckBox.IsChecked = _client2AbbeyCoordinate is not null &&
            string.Equals(abbey2CustomEnabled, "true", StringComparison.OrdinalIgnoreCase);
        DailyMissionsScopeComboBox.SelectedItem = dailyScope ?? (string.Equals(dailyEnabled, "true", StringComparison.OrdinalIgnoreCase) ? "Ambos" : "Nenhum");
        GuildDirectiveScopeComboBox.SelectedItem = directiveScope ?? (string.Equals(directiveEnabled, "true", StringComparison.OrdinalIgnoreCase) ? "Ambos" : "Nenhum");
        MailScopeComboBox.SelectedItem = mailScope ?? "Ambos";
        DailyShopScopeComboBox.SelectedItem = dailyShopScope ?? "Nenhum";
        DailyShopTimeTextBox.Text = string.IsNullOrWhiteSpace(dailyShopTime) ? "13:05" : dailyShopTime;
        AntiOverkillScopeComboBox.SelectedItem = antiOverkillScope ?? "Ambos";
        DailyMissionsTimeTextBox.Text = string.IsNullOrWhiteSpace(dailyTime) ? "04:05" : dailyTime;
        GuildDirectiveTimeTextBox.Text = string.IsNullOrWhiteSpace(directiveTime) ? "04:05" : directiveTime;
        GuildDirectiveAreaComboBox.SelectedItem = directiveArea switch
        {
            nameof(GuildDirectiveArea.Ta) => "T.A",
            nameof(GuildDirectiveArea.Dungeon) => "Masmorras",
            _ => "Mapa Aberto"
        };
        FarmScheduleScopeComboBox.SelectedItem = farmScheduleScope ?? (string.Equals(farmScheduleEnabled, "true", StringComparison.OrdinalIgnoreCase) ? "Ambos" : "Nenhum");
        LoadScheduleSteps(Client1ScheduleSteps, client1FarmSchedule);
        LoadScheduleSteps(Client2ScheduleSteps, client2FarmSchedule);
        UpdateCustomCoordinateLabels();
        UpdateCustomCoordinateControls();
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
            await SaveClientCustomCoordinateSettingsAsync(1, client1.CustomFarmCoordinate);
            await SaveClientAbbeySettingsAsync(1, client1);
        }

        if (client2 is not null)
        {
            await _database.SaveSettingAsync("client2.title", client2.Target.Title);
            await _database.SaveSettingAsync("client2.ta", client2.Destination.ToString());
            await _database.SaveSettingAsync("client2.sapheras", client2.UseSapheras.ToString().ToLowerInvariant());
            await SaveClientCustomCoordinateSettingsAsync(2, client2.CustomFarmCoordinate);
            await SaveClientAbbeySettingsAsync(2, client2);
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
        await _database.SaveSettingAsync("routines.daily.enabled", runOptions.DailyRoutines.EnableDailyMissions.ToString().ToLowerInvariant());
        await _database.SaveSettingAsync("routines.daily.scope", DailyMissionsScopeComboBox.SelectedItem?.ToString() ?? "Nenhum");
        await _database.SaveSettingAsync("routines.daily.time", runOptions.DailyRoutines.DailyMissionsAt.ToString(@"hh\:mm", CultureInfo.InvariantCulture));
        await _database.SaveSettingAsync("routines.directive.enabled", runOptions.DailyRoutines.EnableGuildDirective.ToString().ToLowerInvariant());
        await _database.SaveSettingAsync("routines.directive.scope", GuildDirectiveScopeComboBox.SelectedItem?.ToString() ?? "Nenhum");
        await _database.SaveSettingAsync("routines.mail.scope", MailScopeComboBox.SelectedItem?.ToString() ?? "Ambos");
        await _database.SaveSettingAsync("routines.dailyShop.enabled", runOptions.DailyRoutines.EnableDailyShop.ToString().ToLowerInvariant());
        await _database.SaveSettingAsync("routines.dailyShop.scope", DailyShopScopeComboBox.SelectedItem?.ToString() ?? "Nenhum");
        await _database.SaveSettingAsync("routines.dailyShop.time", runOptions.DailyRoutines.DailyShopAt.ToString(@"hh\:mm", CultureInfo.InvariantCulture));
        await _database.SaveSettingAsync("antiOverkill.scope", AntiOverkillScopeComboBox.SelectedItem?.ToString() ?? "Ambos");
        await _database.SaveSettingAsync("routines.directive.time", runOptions.DailyRoutines.GuildDirectiveAt.ToString(@"hh\:mm", CultureInfo.InvariantCulture));
        await _database.SaveSettingAsync("routines.directive.area", runOptions.DailyRoutines.GuildDirectiveArea.ToString());
        await _database.SaveSettingAsync("farmSchedule.enabled", runOptions.FarmSchedule.Enabled.ToString().ToLowerInvariant());
        await _database.SaveSettingAsync("farmSchedule.scope", FarmScheduleScopeComboBox.SelectedItem?.ToString() ?? "Nenhum");
        await _database.SaveSettingAsync("farmSchedule.client1", JsonSerializer.Serialize(runOptions.FarmSchedule.Client1));
        await _database.SaveSettingAsync("farmSchedule.client2", JsonSerializer.Serialize(runOptions.FarmSchedule.Client2));
    }

    private static void LoadScheduleSteps(
        ObservableCollection<FarmScheduleStepEditor> target,
        string? json)
    {
        target.Clear();
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            foreach (var step in JsonSerializer.Deserialize<FarmScheduleStep[]>(json) ?? [])
            {
                if (step.Duration > TimeSpan.Zero)
                {
                    if (step.Destination is FarmScheduleDestination.Abbey or FarmScheduleDestination.AnonymousDungeon)
                        target.Add(new FarmScheduleStepEditor(step.Destination, step.Duration.TotalMinutes, step.AnonymousDungeonLevel));
                }
            }
        }
        catch (JsonException)
        {
            target.Clear();
        }
    }

    private static string TaDisplayName(string? stored, string fallback) => stored switch
    {
        nameof(TaDestination.Ta1Codex) => "T.A 1 (Codex)",
        nameof(TaDestination.Ta2) => "T.A 2",
        nameof(TaDestination.Ta3) => "T.A 3",
        _ => fallback
    };

    private async Task SaveClientCustomCoordinateSettingsAsync(
        int clientNumber,
        FarmCoordinate? coordinate)
    {
        var destination = clientNumber == 1
            ? ParseTaDestination(Ta1ComboBox.SelectedItem)
            : ParseTaDestination(Ta2ComboBox.SelectedItem);
        var prefix = $"client{clientNumber}.customFarm.{destination}";
        await _database.SaveSettingAsync(
            $"{prefix}.enabled",
            (coordinate is not null).ToString().ToLowerInvariant());
        if (coordinate is null)
        {
            return;
        }

        await _database.SaveSettingAsync($"{prefix}.x", coordinate.X.ToString(CultureInfo.InvariantCulture));
        await _database.SaveSettingAsync($"{prefix}.y", coordinate.Y.ToString(CultureInfo.InvariantCulture));
    }

    private async Task LoadTaCoordinatesAsync(
        int clientNumber,
        Dictionary<TaDestination, FarmCoordinate> coordinates,
        HashSet<TaDestination> enabled)
    {
        coordinates.Clear();
        enabled.Clear();
        foreach (var destination in Enum.GetValues<TaDestination>())
        {
            var prefix = $"client{clientNumber}.customFarm.{destination}";
            var coordinate = ParseFarmCoordinate(
                await _database.GetSettingAsync($"{prefix}.x"),
                await _database.GetSettingAsync($"{prefix}.y"));
            if (coordinate is not null)
            {
                coordinates[destination] = coordinate;
                if (string.Equals(await _database.GetSettingAsync($"{prefix}.enabled"), "true", StringComparison.OrdinalIgnoreCase))
                    enabled.Add(destination);
            }
        }
    }

    private async Task SaveClientAbbeySettingsAsync(int clientNumber, AutomationClientOptions client)
    {
        var prefix = $"client{clientNumber}.abbey";
        await _database.SaveSettingAsync($"{prefix}.enabled", client.UseAbbey.ToString().ToLowerInvariant());
        await _database.SaveSettingAsync(
            $"client{clientNumber}.farmSchedule.weeklyEntryLimit",
            client.WeeklyAgendaEntryLimit.ToString(CultureInfo.InvariantCulture));
        var custom = client.AbbeyCustomFarmCoordinate;
        await _database.SaveSettingAsync($"{prefix}.customFarm.enabled", (custom is not null).ToString().ToLowerInvariant());
        if (custom is not null)
        {
            await _database.SaveSettingAsync($"{prefix}.customFarm.x", custom.X.ToString(CultureInfo.InvariantCulture));
            await _database.SaveSettingAsync($"{prefix}.customFarm.y", custom.Y.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static FarmCoordinate? ParseFarmCoordinate(string? xText, string? yText)
    {
        if (!int.TryParse(xText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(yText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ||
            x is < 0 or >= 1920 || y is < 0 or >= 1040)
        {
            return null;
        }

        return new FarmCoordinate(x, y);
    }

    private static TaDestination ParseTaDestination(object? selectedItem) => selectedItem?.ToString() switch
    {
        "T.A 1 (Codex)" => TaDestination.Ta1Codex,
        "T.A 2" => TaDestination.Ta2,
        _ => TaDestination.Ta3
    };

    private static GuildDirectiveArea ParseGuildDirectiveArea(object? selectedItem) => selectedItem?.ToString() switch
    {
        "T.A" => GuildDirectiveArea.Ta,
        "Masmorras" => GuildDirectiveArea.Dungeon,
        _ => GuildDirectiveArea.OpenMap
    };

    private static FarmScheduleDestination ParseFarmScheduleDestination(object? selectedItem) => selectedItem?.ToString() switch
    {
        "Masmorra Anônima · Estreito de Tenerys" => FarmScheduleDestination.AnonymousDungeon,
        _ => FarmScheduleDestination.Abbey
    };

    private static bool TryParseClock(string text, out TimeSpan time)
    {
        if (TimeSpan.TryParseExact(text.Trim(), new[] { @"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss" },
                CultureInfo.InvariantCulture, out time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1))
        {
            return true;
        }

        time = default;
        return false;
    }

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
        _updateCheckTimer.Stop();
        CancelScheduledStart();
        _runCancellation?.Cancel();
        _updateDownloadCancellation?.Cancel();
        _engine.Log -= OnEngineLog;
        _engine.StatusChanged -= OnEngineStatusChanged;
        base.OnClosing(e);
    }

    public sealed record FarmScheduleStepEditor(
        FarmScheduleDestination Destination,
        double DurationMinutes,
        int AnonymousDungeonLevel = 97)
    {
        public string DisplayText => Destination == FarmScheduleDestination.AnonymousDungeon
            ? $"{DestinationName(Destination)} · Nv. {AnonymousDungeonLevel} · {DurationMinutes:0.#} min"
            : $"{DestinationName(Destination)} · {DurationMinutes:0.#} min";

        public FarmScheduleStep ToModel() => new(Destination, TimeSpan.FromMinutes(DurationMinutes), AnonymousDungeonLevel);

        private static string DestinationName(FarmScheduleDestination destination) => destination switch
        {
            FarmScheduleDestination.Abbey => "Abadia da Lembrança",
            FarmScheduleDestination.AnonymousDungeon => "Estreito de Tenerys",
            _ => "Destino antigo"
        };
    }
}
