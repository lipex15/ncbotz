using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BotNC.App.Models;

namespace BotNC.App;

public partial class MainWindow
{
    internal static void VerifyAgendaEditor()
    {
        var step = new FarmScheduleStepEditor(FarmScheduleDestination.AnonymousDungeon, 90, 97);
        foreach (var level in new[] { 86, 97, 110 })
        {
            step.DestinationLabel = $"Estreito de Tenerys · Nv. {level}";
            if (step.ToModel().AnonymousDungeonLevel != level || !step.IsAnonymous)
                throw new InvalidOperationException("Nível da Agenda não preservado.");
        }
        step.DestinationLabel = "T.A 1";
        if (step.ToModel().Destination != FarmScheduleDestination.Ta1) throw new InvalidOperationException("Destino da Agenda inválido.");
        foreach (var invalid in new[] { "", "abc", "NaN", "Infinity", "-1", "10081" })
        {
            step.DurationText = invalid;
            if (string.IsNullOrEmpty(step.Error)) throw new InvalidOperationException("Duração inválida aceita.");
        }
        step.DurationText = "45";
        var restored = System.Text.Json.JsonSerializer.Deserialize<FarmScheduleStep>(
            System.Text.Json.JsonSerializer.Serialize(step.ToModel()));
        if (restored?.Duration.TotalMinutes != 45) throw new InvalidOperationException("Persistência da Agenda inválida.");
        var steps = new ObservableCollection<FarmScheduleStepEditor> { step, new(FarmScheduleDestination.Abbey, 60) };
        steps.Move(0, 1);
        if (!ReferenceEquals(steps[1], step)) throw new InvalidOperationException("Reordenação perdeu etapa.");
    }
    internal void ShowAgendaLayoutForScreenshot()
    {
        OnShowFarmSchedule(this, new RoutedEventArgs());
        Client1ScheduleSteps.Clear(); Client2ScheduleSteps.Clear();
        foreach (var step in new[] { new FarmScheduleStepEditor(FarmScheduleDestination.Abbey, 60), new(FarmScheduleDestination.AnonymousDungeon, 90), new(FarmScheduleDestination.Ta1, 30) })
            Client1ScheduleSteps.Add(step);
        foreach (var step in new[] { new FarmScheduleStepEditor(FarmScheduleDestination.Ta1, 45), new(FarmScheduleDestination.Abbey, 60), new(FarmScheduleDestination.AnonymousDungeon, 60) })
            Client2ScheduleSteps.Add(step);
        FarmScheduleScopeComboBox.SelectedItem = "Ambos";
    }
    private Point _stageDragOrigin;
    private void OnToggleSchedulePopup(object sender, RoutedEventArgs e) => ScheduleStartPopup.IsOpen = !ScheduleStartPopup.IsOpen;
    private void OnStagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FarmScheduleStepEditor.DurationText)) UpdateScheduleTotals();
    }
    private ObservableCollection<FarmScheduleStepEditor>? StageOwner(FarmScheduleStepEditor step) =>
        Client1ScheduleSteps.Contains(step) ? Client1ScheduleSteps : Client2ScheduleSteps.Contains(step) ? Client2ScheduleSteps : null;
    private void OnAddEditableStage(object sender, RoutedEventArgs e)
    {
        if (_runCancellation is not null) return;
        var steps = (sender as FrameworkElement)?.Tag?.ToString() == "2" ? Client2ScheduleSteps : Client1ScheduleSteps;
        steps.Add(new(FarmScheduleDestination.Abbey, 60));
    }
    private void OnDeleteStage(object sender, RoutedEventArgs e)
    {
        if (_runCancellation is not null || (sender as FrameworkElement)?.DataContext is not FarmScheduleStepEditor step) return;
        StageOwner(step)?.Remove(step);
        step.PropertyChanged -= OnStagePropertyChanged;
    }
    private void OnStageGripDown(object sender, MouseButtonEventArgs e) => _stageDragOrigin = e.GetPosition(this);
    private void OnStageGripMove(object sender, MouseEventArgs e)
    {
        if (_runCancellation is not null || e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement { DataContext: FarmScheduleStepEditor step } element) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _stageDragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _stageDragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(element, new DataObject(typeof(FarmScheduleStepEditor), step), DragDropEffects.Move);
    }
    private void OnStageDrop(object sender, DragEventArgs e)
    {
        if (_runCancellation is not null || !e.Data.GetDataPresent(typeof(FarmScheduleStepEditor)) ||
            e.Data.GetData(typeof(FarmScheduleStepEditor)) is not FarmScheduleStepEditor source ||
            (sender as FrameworkElement)?.DataContext is not FarmScheduleStepEditor destination) return;
        var owner = StageOwner(source);
        if (owner is null || owner != StageOwner(destination)) return;
        owner.Move(owner.IndexOf(source), owner.IndexOf(destination));
        e.Handled = true;
    }
    private void OnStageKeyDown(object sender, KeyEventArgs e)
    {
        if (_runCancellation is not null || Keyboard.Modifiers != ModifierKeys.Alt ||
            e.Key is not (Key.Up or Key.Down) || sender is not ListBox list) return;
        var element = Keyboard.FocusedElement as FrameworkElement;
        if (element?.DataContext is not FarmScheduleStepEditor step || StageOwner(step) is not { } owner) return;
        MoveScheduleStep(owner, owner.IndexOf(step), e.Key == Key.Up ? -1 : 1);
        e.Handled = true;
    }
    private bool ValidateEditableAgenda()
    {
        foreach (var (client, steps) in new[] { (1, Client1ScheduleSteps), (2, Client2ScheduleSteps) })
        {
            var invalid = steps.FirstOrDefault(step => !string.IsNullOrEmpty(step.Error));
            if (invalid is null) continue;
            ShowValidation($"Agenda do Cliente {client}, etapa {invalid.Position}: {invalid.Error}");
            return false;
        }
        return true;
    }

    public sealed class FarmScheduleStepEditor : INotifyPropertyChanged, IDataErrorInfo
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public static string[] DestinationChoices { get; } = ["Abadia", "Estreito de Tenerys · Nv. 86", "Estreito de Tenerys · Nv. 97", "Estreito de Tenerys · Nv. 110", "T.A 1"];
        public static int[] LevelChoices { get; } = [86, 97, 110];
        private FarmScheduleDestination _destination;
        private int _level, _position;
        private string _durationText;
        public FarmScheduleStepEditor(FarmScheduleDestination destination, double durationMinutes, int anonymousDungeonLevel = 97)
        {
            _destination = destination; _level = anonymousDungeonLevel;
            _durationText = durationMinutes.ToString("0.########", CultureInfo.CurrentCulture);
        }
        private void Changed(string property) => PropertyChanged?.Invoke(this, new(property));
        public int Position { get => _position; set { if (_position == value) return; _position = value; Changed(nameof(Position)); } }
        public FarmScheduleDestination Destination => _destination;
        public bool IsAnonymous => _destination == FarmScheduleDestination.AnonymousDungeon;
        public string DestinationLabel
        {
            get => _destination == FarmScheduleDestination.Abbey ? "Abadia" : IsAnonymous ? $"Estreito de Tenerys · Nv. {_level}" : "T.A 1";
            set { _destination = value == "Abadia" ? FarmScheduleDestination.Abbey : value.StartsWith("Estreito", StringComparison.Ordinal) ? FarmScheduleDestination.AnonymousDungeon : FarmScheduleDestination.Ta1; if (IsAnonymous && int.TryParse(value.Split(' ').Last(), out var level) && LevelChoices.Contains(level)) _level = level; Changed(nameof(DestinationLabel)); Changed(nameof(AnonymousDungeonLevel)); Changed(nameof(IsAnonymous)); Changed(nameof(DisplayText)); }
        }
        public int AnonymousDungeonLevel { get => _level; set { _level = value; Changed(nameof(AnonymousDungeonLevel)); } }
        public string DurationText { get => _durationText; set { _durationText = value; Changed(nameof(DurationText)); Changed(nameof(Error)); } }
        public double DurationMinutes => double.TryParse(_durationText, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
            double.TryParse(_durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : double.NaN;
        public string Error => !double.IsFinite(DurationMinutes) || DurationMinutes <= 0 || DurationMinutes > 10080
            ? "Informe uma duração maior que zero e até 10080 minutos." : IsAnonymous && !LevelChoices.Contains(_level) ? "Selecione o nível 86, 97 ou 110." : "";
        public string this[string columnName] => columnName == nameof(DurationText) ? Error : "";
        public string DisplayText => $"{DestinationLabel} · {DurationMinutes:0.#} min";
        public FarmScheduleStep ToModel()
        {
            if (Error.Length != 0) throw new ArgumentException(Error);
            return new(Destination, TimeSpan.FromMinutes(DurationMinutes), AnonymousDungeonLevel);
        }
    }
}
