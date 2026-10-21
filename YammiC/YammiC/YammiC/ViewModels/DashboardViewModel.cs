using System;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using CNCO.YccuSdk.Can;
using CNCO.YccuSdk.Tracks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YammiC.Services;

namespace YammiC.ViewModels;

/// <summary>
/// The home page: connection, live bike data, the track timer, what the bike's displays show and what is playing.
/// </summary>
public partial class DashboardViewModel : BikeViewModelBase
{
    private const string Dash = "—";

    private static readonly IBrush Connected = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43));
    private static readonly IBrush Connecting = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x1E));
    private static readonly IBrush Offline = new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x98));

    private readonly TrackTimerService _tracks;
    private bool _applying;

    public DashboardViewModel(IBikeController controller, TrackTimerService tracks) : base(controller)
    {
        _tracks = tracks;
        Attach();
        Controller.CanChanged += HandleCanChanged;
        _tracks.Changed += HandleTrackChanged;
        RefreshLive();
        RefreshTimer();
    }

    public DashboardViewModel(IBikeController controller) : this(controller, App.TrackTimer)
    {
    }

    // Design-time constructor.
    public DashboardViewModel() : this(new FakeBikeController())
    {
    }

    public override void Detach()
    {
        base.Detach();
        Controller.CanChanged -= HandleCanChanged;
        _tracks.Changed -= HandleTrackChanged;
    }

    // -- Connection ------------------------------------------------------

    [ObservableProperty]
    public partial bool NeedsNotificationAccess { get; set; }

    [ObservableProperty]
    public partial string BikeName { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial IBrush StatusBrush { get; set; } = Offline;

    [ObservableProperty]
    public partial string LastError { get; set; } = "";

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial bool CanConnect { get; set; } = true;

    [ObservableProperty]
    public partial bool CanDisconnect { get; set; }

    // -- Live bike data --------------------------------------------------

    [ObservableProperty]
    public partial string GearText { get; set; } = Dash;

    [ObservableProperty]
    public partial string RpmText { get; set; } = Dash;

    [ObservableProperty]
    public partial string WaterTempText { get; set; } = Dash;

    [ObservableProperty]
    public partial string IntakeTempText { get; set; } = Dash;

    [ObservableProperty]
    public partial string LiveHint { get; set; } = "";

    // -- Track timer -----------------------------------------------------

    [ObservableProperty]
    public partial string TimerStateText { get; set; } = "";

    [ObservableProperty]
    public partial string TimerTrackText { get; set; } = "";

    [ObservableProperty]
    public partial string TimerBestText { get; set; } = "";

    [ObservableProperty]
    public partial IBrush TimerBrush { get; set; } = Offline;

    // -- Bike displays and media -----------------------------------------

    [ObservableProperty]
    public partial bool VpbEnabled { get; set; } = true;

    [ObservableProperty]
    public partial string VpbStatus { get; set; } = "";

    [ObservableProperty]
    public partial string NavigationStatus { get; set; } = "";

    [ObservableProperty]
    public partial string MediaTrack { get; set; } = "Nothing playing";

    [ObservableProperty]
    public partial string MediaArtist { get; set; } = "";

    [ObservableProperty]
    public partial string MediaState { get; set; } = "";

    protected override void OnSnapshot(BikeSnapshot snapshot)
    {
        _applying = true;
        try
        {
            var c = snapshot.Connection;
            NeedsNotificationAccess = Controller.NeedsNotificationAccess;
            BikeName = string.IsNullOrWhiteSpace(c.DeviceName) ? "No bike set up" : c.DeviceName;
            LastError = c.LastError;
            HasError = !string.IsNullOrWhiteSpace(c.LastError);

            var button = c.GetConnectButtonState();
            CanConnect = Controller.CanConnect && button == ConnectButtonState.Idle;
            CanDisconnect = button == ConnectButtonState.Connected;
            StatusText = button switch
            {
                ConnectButtonState.Connected => "Connected",
                ConnectButtonState.Connecting => string.IsNullOrWhiteSpace(c.ConnectionLabel) ? "Connecting..." : c.ConnectionLabel,
                _ => "Not connected",
            };
            StatusBrush = button switch
            {
                ConnectButtonState.Connected => Connected,
                ConnectButtonState.Connecting => Connecting,
                _ => Offline,
            };

            var media = snapshot.Media;
            MediaTrack = string.IsNullOrWhiteSpace(media?.SongName) ? "Nothing playing" : media!.SongName;
            MediaArtist = media?.ArtistName ?? "";
            MediaState = media == null ? "" : media.PlaybackState.ToString();

            VpbEnabled = snapshot.VpbEnabled;
            VpbStatus = snapshot.VpbStatus;
            NavigationStatus = NavigationStateFormatter.Describe(snapshot.Navigation);
        }
        finally
        {
            _applying = false;
        }
        RefreshLive();
    }

    partial void OnVpbEnabledChanged(bool value)
    {
        if (!_applying) Controller.SetVpbEnabled(value);
    }

    private void HandleCanChanged() => Dispatcher.UIThread.Post(RefreshLive);

    private void HandleTrackChanged() => Dispatcher.UIThread.Post(RefreshTimer);

    private void RefreshLive()
    {
        var v = Controller.CanValues;
        GearText = CanSignals.GearText(v.Gear);
        RpmText = Num(v.Rpm, "0");
        WaterTempText = Num(v.WaterTempC, "0", " °C");
        IntakeTempText = Num(v.IntakeAirTempC, "0", " °C");

        LiveHint = !Controller.CanDataEnabled
            ? "Bike data is off. Turn it on in Bike Telemetry to see live values."
            : Controller.CanRaw.Count == 0
                ? "Waiting for the bike..."
                : "";
    }

    private void RefreshTimer()
    {
        var timer = _tracks.Timer;
        var track = timer.SelectedTrackId != null ? _tracks.Library.Get(timer.SelectedTrackId) : null;
        TimerStateText = timer.State switch
        {
            TimerState.Ready => "Armed",
            TimerState.Timing => timer.CurrentLap > 0 ? $"Timing, lap {timer.CurrentLap}" : "Timing",
            _ => _tracks.Enabled ? "Waiting for the bike" : "Off",
        };
        TimerBrush = timer.State switch
        {
            TimerState.Idle => Offline,
            TimerState.Ready => Connecting,
            _ => Connected,
        };
        TimerTrackText = track?.Name ?? "No track selected";
        var best = timer.BestLap;
        TimerBestText = best != null ? $"Best lap {best.Lap}: {TrackTimerViewModel.Format(best.Time)}" : "";
    }

    private static string Num(double? value, string format, string unit = "") =>
        value != null ? value.Value.ToString(format, CultureInfo.CurrentCulture) + unit : Dash;

    [RelayCommand]
    private void Connect() => Controller.Connect();

    [RelayCommand]
    private void Disconnect() => Controller.Disconnect();

    [RelayCommand]
    private void GrantNotificationAccess() => Controller.OpenNotificationAccessSettings();
}
