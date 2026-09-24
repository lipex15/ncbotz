using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BotNC.App.Services;

namespace BotNC.App;

public partial class StatisticsPanel : UserControl
{
    private AppDatabase? _database;
    private StatisticsStore? _store;
    private Func<string>? _session;
    private bool _busy;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private string NickKey(int client) => $"users.{ActivationService.ProfileKey}.statistics.nick{client}";

    public StatisticsPanel()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await RefreshAsync();
        Unloaded += (_, _) => _timer.Stop();
    }

    internal async Task ConfigureAsync(AppDatabase database, Func<string> session)
    {
        _database = database;
        _session = session;
        _store = new StatisticsStore(Path.GetDirectoryName(database.DatabasePath)!);
        Nick1.Text = await database.GetSettingAsync(NickKey(1)) ?? "";
        Nick2.Text = await database.GetSettingAsync(NickKey(2)) ?? "";
        await _store.InitializeAsync();
        await RefreshAsync();
    }

    private async void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) { _timer.Start(); await RefreshAsync(); }
        else _timer.Stop();
    }
    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e) => await RefreshAsync();
    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void OnSaveNicks(object sender, RoutedEventArgs e)
    {
        if (_database is null) return;
        try
        {
            await _database.SaveSettingAsync(NickKey(1), Nick1.Text.Trim());
            await _database.SaveSettingAsync(NickKey(2), Nick2.Text.Trim());
            NickStatus.Text = "Nicks salvos. São rótulos visuais, não identificação da conta no jogo.";
            await RefreshAsync();
        }
        catch (Exception exception) { NickStatus.Text = $"Não foi possível salvar: {exception.Message}"; }
    }

    internal async Task RefreshAsync()
    {
        if (_store is null || _database is null || _busy || !IsVisible) return;
        _busy = true;
        try
        {
            var now = DateTimeOffset.Now;
            var since = Period.SelectedIndex == 1 ? DateTimeOffset.MinValue :
                new DateTimeOffset(DateTime.Today.AddDays(Period.SelectedIndex == 2 ? -6 : 0));
            int? client = Client.SelectedIndex is 1 or 2 ? Client.SelectedIndex : null;
            var rows = await _store.ReadAsync(ActivationService.ProfileKey, since, now.AddTicks(1), client,
                Period.SelectedIndex == 1 ? _session?.Invoke() ?? "not-started" : null);
            var nick = client == 2 ? Nick2.Text.Trim() : client == 1 ? Nick1.Text.Trim() :
                string.Join(" e ", new[] { Nick1.Text.Trim(), Nick2.Text.Trim() }.Where(value => value.Length > 0).Distinct());
            var salutation = now.Hour is >= 6 and < 12 ? "Bom dia" : now.Hour is >= 12 and < 18 ? "Boa tarde" : "Boa noite";
            Greeting.Text = nick.Length == 0 ? $"{salutation}! Seu progresso, de perto." : $"{salutation}, {nick}!";
            var expenses = rows.Where(row => row.Kind == "expense").ToArray();
            Gold.Text = expenses.Sum(row => row.Gold ?? 0).ToString("N0");
            GoldHint.Text = $"ouro · {expenses.Count(row => row.Gold is null)} valor(es) não apurado(s) · {rows.Count(row => row.Kind == "pending")} ação(ões) sem confirmação";
            Emergencies.Text = rows.Count(row => row.Kind == "emergency").ToString();
            EmergencyHint.Text = $"Incidentes com TP enviado · {rows.Count(row => row.Kind == "town")} chegada(s) observada(s) à cidade";
            Rewards.Text = rows.Count(row => row.Kind == "reward").ToString();
            Boss.Text = rows.Count(row => row.Kind == "boss").ToString();
            var farm = TimeSpan.FromSeconds(rows.Where(row => row.Kind == "farm").Sum(row => row.Quantity));
            Farm.Text = $"{(int)farm.TotalHours}h {farm.Minutes:00}m";
            Routines.Text = rows.Count(row => row.Kind == "routine").ToString();
            var groups = expenses.GroupBy(row => row.Description);
            GoldBreakdown.Text = expenses.Length == 0 ? "Nenhum gasto registrado. Os dados começam nesta versão; não há preenchimento retroativo." :
                string.Join("\n", groups.Select(group => $"{group.Key}: {group.Sum(row => row.Gold ?? 0):N0} ouro" +
                    (group.Any(row => row.Gold is null) ? $" · {group.Count(row => row.Gold is null)} valor(es) não apurado(s)" : "")));
            var history = rows.Where(row => row.Kind != "farm").Take(100).ToArray();
            History.ItemsSource = history;
            EmptyHistory.Visibility = history.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var bossLines = new List<string>();
            foreach (var number in client.HasValue ? new[] { client.Value } : new[] { 1, 2 })
            {
                var prefix = $"users.{ActivationService.ProfileKey}.client{number}.routines.loveBoss";
                var day = await _database.GetSettingAsync(prefix + ".completedCycle");
                var week = await _database.GetSettingAsync(prefix + ".week");
                var count = await _database.GetSettingAsync(prefix + ".weeklyCount");
                var currentWeek = week == LoveBossSchedule.WeeklyKey(DateTimeOffset.UtcNow) && int.TryParse(count, out _);
                var progress = currentWeek ? Math.Clamp(int.Parse(count!), 0, 5) : (int?)null;
                bossLines.Add($"Cliente {number}: " + (day == LoveBossSchedule.DailyKey(DateTimeOffset.UtcNow) ? "concluído neste ciclo" : "conclusão deste ciclo ainda não registrada") +
                    (progress is { } value ? $" · semana: {value}/5 · faltam {5 - value}" : " · progresso semanal ainda não observado"));
            }
            BossProgress.Text = string.Join("\n", bossLines);
            RefreshStatus.Text = $"Atualizado às {now:HH:mm:ss} · horário do PC · dados salvos neste ambiente";
        }
        catch (Exception exception) { RefreshStatus.Text = $"Painel sem atualização: {exception.Message}. O bot continua independente."; }
        finally { _busy = false; }
    }
}
