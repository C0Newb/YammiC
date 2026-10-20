using System.Globalization;
using CCU_Emulator.Emulation;
using CCU_Emulator.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCU_Emulator.ViewModels;

/// <summary>What the emulated dash shows. Priority: call, then VPB image, then the idle screen.</summary>
public sealed partial class DashViewModel : ViewModelBase
{
    /// <summary>How long a notification banner stays on screen.</summary>
    public static TimeSpan BannerDuration { get; set; } = TimeSpan.FromSeconds(8);

    private int _bannerToken;

    public CcuState State { get; }

    [ObservableProperty] public partial bool BannerVisible { get; set; }

    public DashViewModel(CcuState state)
    {
        State = state;
        State.PropertyChanged += (_, e) =>
        {
            OnPropertyChanged(string.Empty);   // every derived property reads State; refresh them all
            if (e.PropertyName == nameof(CcuState.LatestNotification) && State.LatestNotification is not null)
            {
                BannerVisible = true;
                _ = HideBannerLaterAsync();
            }
        };
        State.Weather.CollectionChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    private async Task HideBannerLaterAsync()
    {
        var token = ++_bannerToken;
        await Task.Delay(BannerDuration);
        if (token == _bannerToken) BannerVisible = false;
    }

    [RelayCommand]
    private void DismissBanner()
    {
        _bannerToken++;
        BannerVisible = false;
    }

    // Screen selection
    public bool ShowCall => State.CallRinging || State.CallActive;
    public bool ShowVpb => !ShowCall && State.VpbImage is not null;
    public bool ShowIdle => !ShowCall && State.VpbImage is null;
    public byte[]? VpbImageBytes => State.VpbImage;
    public bool VpbReceiving => State.VpbReceiving;
    public string VpbProgressText => State.VpbReceiving ? $"Receiving image {State.VpbReceivedBytes}/{State.VpbTotalBytes} bytes" : "";

    // Call
    public string CallTitle => State.CallRinging ? "Incoming call" : "In call";
    public string CallerLine => !string.IsNullOrEmpty(State.CallerName) ? $"{State.CallerName}  {State.CallerNumber}".Trim() : State.CallerNumber;

    // Idle screen
    public string ClockText => State.PhoneTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "--:--";

    public string StatusText
        => $"{State.Signal} · {(State.HeadsetConnected ? "Headset" : "No headset")} · {State.Battery}%{(State.Charging ? " ⚡" : "")}";

    public string PlaybackText => State.MusicPlayback switch
    {
        PlaybackState.Playing => "▶",
        PlaybackState.Paused => "⏸",
        _ => "■",
    };

    public string MusicTitle => string.IsNullOrEmpty(State.MusicSong) ? "No music" : State.MusicSong;

    public string MusicSubtitle => string.IsNullOrEmpty(State.MusicSong) ? "" : $"{State.MusicArtist} — {State.MusicAlbum}";

    public double MusicProgress
        => State.MusicDurationSeconds <= 0 ? 0 : Math.Clamp(100.0 * State.MusicElapsedSeconds / State.MusicDurationSeconds, 0, 100);

    public string WeatherText
    {
        get
        {
            var current = State.Weather.FirstOrDefault(w => w.Slot == WeatherSlot.Current);
            if (current is null) return "No weather";
            var place = string.IsNullOrEmpty(State.WeatherLocation) ? "" : $"{State.WeatherLocation}: ";
            return $"{place}{current.Temperature.ToString("0.#", CultureInfo.InvariantCulture)}°C {current.Type} ({current.PrecipitationPercent}%)";
        }
    }

    // Notification banner
    public string BannerTitle => State.LatestNotification?.Title ?? "";
    public string BannerBody => State.LatestNotification?.Detail ?? "";
}
