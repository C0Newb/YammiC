using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CNCO.YccuSdk.Yrc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YammiC.Services;

namespace YammiC.ViewModels;

public record LapRow(string Number, string Time, string Status);

/// <summary>
/// A stopwatch for trying the bike's lap-time display: Start, Lap, Stop; each lap is sent to the bike.
/// </summary>
public partial class LapTimerViewModel : BikeViewModelBase
{
    private readonly LapTimer? _timer;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(100) };

    [ObservableProperty]
    public partial string Elapsed { get; set; } = "0:00.00";

    [ObservableProperty]
    public partial string CurrentLap { get; set; } = "0:00.00";

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(LapCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsRunning { get; set; }

    public bool IsAvailable => _timer != null;

    public ObservableCollection<LapRow> Laps { get; } = new();

    public LapTimerViewModel(IBikeController controller) : base(controller)
    {
        _timer = controller.LapTimer;
        Status = _timer == null
            ? "The lap timer needs a connection to the bike (not available in the simulator)."
            : "Start the timer, press Lap at each line. Each lap is sent to the bike and its answer shows below.";
        if (_timer != null) _timer.Updated += HandleUpdated;

        _tick.Tick += (_, _) => RefreshClock();
        _tick.Start();
        Attach();
        RefreshAll();
    }

    // Design-time constructor.
    public LapTimerViewModel() : this(new FakeBikeController())
    {
    }

    public override void Detach()
    {
        base.Detach();
        _tick.Stop();
        if (_timer != null) _timer.Updated -= HandleUpdated;
    }

    protected override void OnSnapshot(BikeSnapshot snapshot)
    {
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => _timer?.StartTimer();

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private Task Lap() => _timer?.LapAsync() ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private Task Stop() => _timer?.StopTimerAsync() ?? Task.CompletedTask;

    private bool CanStart() => IsAvailable && !IsRunning;

    private void HandleUpdated() => Dispatcher.UIThread.Post(RefreshAll);

    private void RefreshAll()
    {
        IsRunning = _timer?.IsRunning ?? false;
        RefreshClock();
        Laps.Clear();
        if (_timer == null) return;
        foreach (var lap in _timer.Laps.Reverse()) Laps.Add(new LapRow($"Lap {lap.Number}", Format(lap.Time), lap.Status));
    }

    private void RefreshClock()
    {
        if (_timer == null) return;
        Elapsed = Format(_timer.Elapsed);
        CurrentLap = Format(_timer.CurrentLap);
    }

    private static string Format(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 10:00}";
}
