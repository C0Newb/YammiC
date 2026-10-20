using System;
using System.Collections.Generic;
using CNCO.YccuSdk.Can;
using System.Threading.Tasks;
using CNCO.YccuSdk;
using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Navigation;
using CNCO.YccuSdk.Screens;
using CNCO.YccuSdk.Yrc;

namespace YammiC.Services;

/// <summary>
/// In-memory controller for platforms without Bluetooth and for design-time previews. Connecting
/// walks through a fake handshake so the pages can be exercised without a bike.
/// </summary>
public class FakeBikeController : IBikeController
{
    private readonly object _gate = new();
    private BikeSnapshot _snapshot = new()
    {
        Connection = new BikeDebugState
        {
            DeviceName = "YCCU_01234567890123",
            CcuId = "01234567890123",
            PassKey = "012345",
        },
    };

    public BikeSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    public event Action? Changed;

    public bool CanConnect => true;
    public bool NeedsNotificationAccess => false;
    public bool MusicMetadataEnabled { get; set; } = true;
    public bool PhoneMetadataEnabled { get; set; } = true;
    public bool AutoReconnectEnabled { get; set; } = true;
    public bool HomeAndWorkEnabled { get; set; } = true;

    public bool CanDataEnabled { get; set; }

    public IReadOnlyList<int> CanSignalIds { get; set; } = CanSignals.DefaultIds;

    public IReadOnlyList<int> CanExtraIds { get; set; } = Array.Empty<int>();

    public Task<string> DiscoverSupportedSignalsAsync() =>
        Task.FromResult("Servers: 1, 2\n\nServer 1: 3 IDs\n0x0250 0x0279 0x027C (sample - no bike connected)");

    public ProfileLibrary ProfileLibrary { get; } = new(new MemoryKeyValueStore());

    private IReadOnlyList<YrcProfile> _bikeProfiles = Array.Empty<YrcProfile>();

    public IReadOnlyList<YrcProfile> BikeProfiles => _bikeProfiles;

    public event Action? RideModesChanged;

    public async Task<string> ReadRideModesAsync()
    {
        await Task.Delay(600);
        YrcValue V(int kind, int current, int min, int max) => new(kind, current, min, max);
        _bikeProfiles = new[]
        {
            new YrcProfile(28, 1, 4, "Track 1", new[] { V(1, 1, 1, 3), V(2, 3, 0, 9), V(3, 2, 0, 9), V(4, 1, 0, 3) }),
            new YrcProfile(29, 2, 4, "Track 2", new[] { V(1, 2, 1, 3), V(2, 5, 0, 9), V(3, 4, 0, 9), V(4, 2, 0, 3) }),
            new YrcProfile(30, 3, 4, "Wet", new[] { V(1, 3, 1, 3), V(2, 1, 0, 9), V(3, 1, 0, 9), V(4, 0, 0, 3) }),
            new YrcProfile(31, 4, 4, "Track 4", new[] { V(1, 1, 1, 3), V(2, 2, 0, 9), V(3, 2, 0, 9), V(4, 1, 0, 3) }),
        };
        RideModesChanged?.Invoke();
        return $"Read {_bikeProfiles.Count} ride modes (sample data)";
    }

    public async Task<string> SendRideModesAsync(IReadOnlyList<YrcProfile> profiles)
    {
        await Task.Delay(600);
        return $"Sent {profiles.Count} ride modes (simulated)";
    }

    public LapTimer? LapTimer => null;

    public CanValues CanValues => CanDataEnabled ? CanValues.Sample : CanValues.Empty;

    public IReadOnlyList<CanRawReading> CanRaw => CanDataEnabled
        ? new[]
        {
            new CanRawReading(0x027C, new byte[] { 0x11, 0xE0, 0x00, 0x60, 0, 0, 0, 0 }, DateTime.UtcNow, DateTime.UtcNow),
            new CanRawReading(0x0512, new byte[] { 0x8C, 0x5A, 0, 0, 0, 0, 0, 0 }, DateTime.UtcNow, DateTime.UtcNow),
        }
        : Array.Empty<CanRawReading>();

#pragma warning disable CS0067 // never raised: the sample data is static
    public event Action? CanChanged;
#pragma warning restore CS0067
    public NavMode NavigationMode { get; set; } = NavMode.Ui;
    public bool ReelsSupported => false;
    public string? ReelsFolderName { get; private set; }

    public void SetReelsFolder(string folderUri, string? displayName) => ReelsFolderName = displayName;
    public bool VpbScreenEnabled { get; set; }

    private readonly System.Collections.Generic.Dictionary<ScreenTarget, ScreenLayout> _layouts = new();

    public ScreenLayout GetScreenLayout(ScreenTarget target) =>
        (_layouts.TryGetValue(target, out var saved) ? saved : ScreenDefaults.For(target)).Clone();

    public void SaveScreenLayout(ScreenTarget target, ScreenLayout layout) => _layouts[target] = layout.Clone();

    public void ResetScreenLayout(ScreenTarget target) => _layouts.Remove(target);

    public void Connect()
    {
        Update(s => s with { Connection = s.Connection with { IsBusy = true, ConnectionLabel = "Connecting", LastError = "" } });
        Log("Connecting (simulated)");
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            Update(s => s with
            {
                Connection = s.Connection with { IsBusy = false, ConnectionLabel = "Done" },
                Media = new YmcMediaMetaData("Artist", "Album", "Demo Track", 180, YmcMediaPlaybackState.Playing, 1f, 42),
            });
            Log("Connected (simulated)");
        });
    }

    public void Disconnect()
    {
        Update(s => s with { Connection = s.Connection with { IsBusy = false, ConnectionLabel = "Idle" }, Media = null });
        Log("Disconnected");
    }

    public void ClearLogs() => Update(s => s with { Connection = s.Connection with { Logs = [] } });

    public void SaveCredentials(string ccuId, string passKey, string deviceName) =>
        Update(s => s with { Connection = s.Connection with { CcuId = ccuId.Trim(), PassKey = passKey.Trim(), DeviceName = deviceName.Trim() } });

    public void SetVpbEnabled(bool enabled) =>
        Update(s => s with { VpbEnabled = enabled, VpbStatus = enabled ? "Idle" : "Disabled" });

    public void SetNavigationEnabled(bool enabled) =>
        Update(s => s with { NavigationEnabled = enabled, Navigation = enabled ? new NavigationState.Connecting() : new NavigationState.Idle() });

    public async Task<bool> SendVpbImageAsync(byte[] encodedImage)
    {
        await Task.Delay(500);
        Update(s => s with { VpbStatus = "Custom image" });
        Log($"VPB image sent (simulated, {encodedImage.Length} bytes)");
        return true;
    }

    public async Task<bool> ClearVpbAsync()
    {
        await Task.Delay(200);
        Update(s => s with { VpbStatus = "Idle" });
        return true;
    }

    public void OpenNotificationAccessSettings()
    {
    }

    private void Log(string line) =>
        Update(s => s with { Connection = s.Connection with { Logs = [.. s.Connection.Logs, line] } });

    private void Update(Func<BikeSnapshot, BikeSnapshot> change)
    {
        lock (_gate) _snapshot = change(_snapshot);
        Changed?.Invoke();
    }
}
