using System.Collections.ObjectModel;
using CCU_Emulator.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CCU_Emulator.Emulation;

public sealed record WeatherEntry(
    WeatherSlot Slot, WeatherType Type, double Temperature, double MaxTemperature, double MinTemperature, int PrecipitationPercent);

public sealed record NotificationEntry(int Id, int Category, DateTime? Timestamp, string Title, string SubTitle, string Detail);

/// <summary>Everything the emulated CCU currently knows about the phone. Mutate only on the owner (UI) thread.</summary>
public sealed partial class CcuState : ObservableObject
{
    public const int MaxNotifications = 50;

    // Link
    [ObservableProperty] public partial bool BleConnected { get; set; }
    [ObservableProperty] public partial bool SppConnected { get; set; }
    [ObservableProperty] public partial bool Authenticated { get; set; }
    [ObservableProperty] public partial int AppId { get; set; }

    // Music
    [ObservableProperty] public partial string MusicArtist { get; set; } = "";
    [ObservableProperty] public partial string MusicAlbum { get; set; } = "";
    [ObservableProperty] public partial string MusicSong { get; set; } = "";
    [ObservableProperty] public partial int MusicDurationSeconds { get; set; }
    [ObservableProperty] public partial int MusicElapsedSeconds { get; set; }
    [ObservableProperty] public partial PlaybackState MusicPlayback { get; set; }
    [ObservableProperty] public partial double MusicRate { get; set; }

    // Weather
    [ObservableProperty] public partial string WeatherLocation { get; set; } = "";
    public ObservableCollection<WeatherEntry> Weather { get; } = [];

    // Calls
    [ObservableProperty] public partial bool CallRinging { get; set; }
    [ObservableProperty] public partial bool CallActive { get; set; }
    [ObservableProperty] public partial string CallerName { get; set; } = "";
    [ObservableProperty] public partial string CallerNumber { get; set; } = "";
    [ObservableProperty] public partial int MissedCalls { get; set; }

    // Phone
    [ObservableProperty] public partial int Battery { get; set; }
    [ObservableProperty] public partial bool Charging { get; set; }
    [ObservableProperty] public partial int PhoneVolume { get; set; }
    [ObservableProperty] public partial int MediaVolume { get; set; }
    [ObservableProperty] public partial CellSignal Signal { get; set; }
    [ObservableProperty] public partial ThermalState Thermal { get; set; }
    [ObservableProperty] public partial bool HeadsetConnected { get; set; }
    [ObservableProperty] public partial bool OnHook { get; set; }
    [ObservableProperty] public partial DateTime? PhoneTime { get; set; }

    // Notifications
    [ObservableProperty] public partial bool[] NotificationCategories { get; set; } = new bool[12];
    [ObservableProperty] public partial NotificationEntry? LatestNotification { get; set; }
    public ObservableCollection<NotificationEntry> Notifications { get; } = [];

    // Virtual Pit Board
    [ObservableProperty] public partial byte[]? VpbImage { get; set; }
    [ObservableProperty] public partial bool VpbReceiving { get; set; }
    [ObservableProperty] public partial int VpbReceivedBytes { get; set; }
    [ObservableProperty] public partial int VpbTotalBytes { get; set; }

    /// <summary>Inserts or replaces the entry for its slot, keeping the list ordered by slot value.</summary>
    public void UpsertWeather(WeatherEntry entry)
    {
        for (var i = 0; i < Weather.Count; i++)
        {
            if (Weather[i].Slot == entry.Slot) { Weather[i] = entry; return; }
            if ((byte)Weather[i].Slot > (byte)entry.Slot) { Weather.Insert(i, entry); return; }
        }
        Weather.Add(entry);
    }

    /// <summary>Adds (or replaces, by id) a notification at the top of the list.</summary>
    public void AddNotification(NotificationEntry entry)
    {
        RemoveNotification(entry.Id);
        Notifications.Insert(0, entry);
        while (Notifications.Count > MaxNotifications) Notifications.RemoveAt(Notifications.Count - 1);
        LatestNotification = entry;
    }

    public void RemoveNotification(int id)
    {
        for (var i = 0; i < Notifications.Count; i++)
        {
            if (Notifications[i].Id != id) continue;
            Notifications.RemoveAt(i);
            break;
        }
        if (LatestNotification?.Id == id) LatestNotification = Notifications.FirstOrDefault();
    }
}
