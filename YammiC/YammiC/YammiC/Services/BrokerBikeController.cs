using System;
using System.Collections.Generic;
using System.Threading;
using CNCO.YccuSdk.Can;
using System.Threading.Tasks;
using CNCO.YccuSdk.Vpb;
using CNCO.YccuSdk;
using CNCO.YccuSdk.Bike;
using CNCO.YccuSdk.Navigation;
using CNCO.YccuSdk.Screens;
using CNCO.YccuSdk.Yrc;
using CNCO.Unify;
using CNCO.Unify.Device;

namespace YammiC.Services;

/// <summary>
/// <see cref="IBikeController"/> over the SDK. Built from a <see cref="BikeService"/>, which owns the
/// VPB/navigation toggles, VPB status and media metadata. The broker-only constructor is for heads that
/// have no service; there the toggles are held locally and media is always empty.
/// </summary>
public class BrokerBikeController : IBikeController
{
    private readonly YmcConnectionBroker _broker;
    private readonly NavigationSdk? _navigation;
    private readonly BikeService? _service;
    private readonly ProfileLibrary _fallbackLibrary = new(new MemoryKeyValueStore());
    private readonly object _gate = new();
    private BikeSnapshot _snapshot;

    public BrokerBikeController(BikeService service)
        : this(service.Controller, service.Navigation, service)
    {
    }

    public BrokerBikeController(YmcConnectionBroker broker, NavigationSdk? navigation = null)
        : this(broker, navigation, null)
    {
    }

    private BrokerBikeController(YmcConnectionBroker broker, NavigationSdk? navigation, BikeService? service)
    {
        _broker = broker;
        _navigation = navigation;
        _service = service;
        _snapshot = new BikeSnapshot
        {
            Connection = broker.State,
            Navigation = navigation?.State ?? new NavigationState.Idle(),
        };
        if (service != null)
            _snapshot = FromService(_snapshot, service);

        broker.StateChanged += state => Update(s => s with { Connection = state });
        if (navigation != null)
            navigation.StateChanged += state => Update(s => s with { Navigation = state });
        if (service != null)
        {
            service.StatusChanged += () => Update(s => FromService(s, service));
            service.CanChanged += RaiseCanChanged;
            service.Yrc.Updated += () => RideModesChanged?.Invoke();
            service.MediaMetadataChanged += media => Update(s => s with { Media = media });
        }
    }

    private static BikeSnapshot FromService(BikeSnapshot s, BikeService service) => s with
    {
        VpbEnabled = service.VpbEnabled,
        NavigationEnabled = service.NavigationEnabled,
        VpbStatus = service.VpbStatus,
        Media = service.MediaMetadata,
    };

    public BikeSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    public event Action? Changed;

    public bool CanConnect => true;
    // Notification access only exists on Android; the monitor throws elsewhere.
    public bool NeedsNotificationAccess
    {
        get
        {
            if (!Platform.IsAndroid()) return false;
            try { return !DeviceRuntime.MediaSessionMonitor.HasAccess; }
            catch (Exception) { return false; }
        }
    }
    public bool MusicMetadataEnabled { get; set; } = true;   // TODO: persist.
    public bool PhoneMetadataEnabled { get; set; } = true;   // TODO: persist.
    public bool AutoReconnectEnabled
    {
        // Persisted by the service. Without a service (broker-only heads) there is no auto connect to toggle.
        get => _service?.AutoConnectEnabled ?? _autoReconnectFallback;
        set
        {
            if (_service != null) _service.AutoConnectEnabled = value;
            else _autoReconnectFallback = value;
        }
    }
    private bool _autoReconnectFallback = true;

    public bool CanDataEnabled
    {
        get => _service?.CanDataEnabled ?? false;
        set
        {
            if (_service != null) _service.CanDataEnabled = value;
        }
    }

    public IReadOnlyList<int> CanSignalIds
    {
        get => _service?.CanSignalIds ?? CanSignals.DefaultIds;
        set
        {
            if (_service != null) _service.CanSignalIds = value;
        }
    }

    public IReadOnlyList<int> CanExtraIds
    {
        get => _service?.CanExtraIds ?? Array.Empty<int>();
        set
        {
            if (_service != null) _service.CanExtraIds = value;
        }
    }

    public Task<string> DiscoverSupportedSignalsAsync() =>
        _service?.DiscoverSupportedSignalsAsync() ?? Task.FromResult("Not available on this platform.");

    public ProfileLibrary ProfileLibrary => _service?.ProfileLibrary ?? _fallbackLibrary;

    public IReadOnlyList<YrcProfile> BikeProfiles => _service?.Yrc.Profiles ?? Array.Empty<YrcProfile>();

    public event Action? RideModesChanged;

    public Task<string> ReadRideModesAsync() =>
        _service?.Yrc.RefreshAsync() ?? Task.FromResult("Not available on this platform.");

    public Task<string> SendRideModesAsync(IReadOnlyList<YrcProfile> profiles) =>
        _service?.Yrc.SendAsync(profiles) ?? Task.FromResult("Not available on this platform.");

    public LapTimer? LapTimer => _service?.LapTimer;

    public CanValues CanValues => _service?.CanValues ?? CanValues.Empty;

    public IReadOnlyList<CanRawReading> CanRaw => _service?.Can.Raw() ?? Array.Empty<CanRawReading>();

    public event Action? CanChanged;

    private int _canPending;

    // Bike data can arrive every 100 ms; the pages only need it about twice a second.
    private void RaiseCanChanged()
    {
        if (Interlocked.Exchange(ref _canPending, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            Interlocked.Exchange(ref _canPending, 0);
            CanChanged?.Invoke();
        });
    }

    public bool HomeAndWorkEnabled
    {
        get => _service?.HomeAndWorkEnabled ?? _homeAndWorkFallback;
        set
        {
            if (_service != null) _service.HomeAndWorkEnabled = value;
            else _homeAndWorkFallback = value;
        }
    }
    private bool _homeAndWorkFallback = true;

    public NavMode NavigationMode
    {
        get => _service?.NavigationMode ?? _navModeFallback;
        set
        {
            if (_service != null) _service.NavigationMode = value;
            else _navModeFallback = value;
        }
    }
    private NavMode _navModeFallback = NavMode.Ui;

    public NavRefreshMode NavRefresh
    {
        get => _service?.NavRefreshMode ?? _navRefreshFallback;
        set
        {
            if (_service != null) _service.NavRefreshMode = value;
            else _navRefreshFallback = value;
        }
    }
    private NavRefreshMode _navRefreshFallback = NavRefreshMode.Auto;

    public bool ReelsSupported => _service?.ReelsSupported ?? false;
    public string? ReelsFolderName => _service?.ReelsFolderName;

    public void SetReelsFolder(string folderUri, string? displayName) => _service?.SetReelsFolder(folderUri, displayName);

    public bool VpbScreenEnabled
    {
        get => _service?.VpbScreenEnabled ?? false;
        set
        {
            if (_service != null) _service.VpbScreenEnabled = value;
        }
    }

    // Without a service there is no saved screen store; edits just don't persist.
    public string GetScreenAxaml(ScreenTarget target) =>
        _service?.GetScreenAxaml(target) ?? ScreenDefaults.Document(target).ToAxaml();

    public IReadOnlyList<ScreenIssue> SaveScreenAxaml(ScreenTarget target, string axaml) =>
        _service?.SaveScreenAxaml(target, axaml) ?? Array.Empty<ScreenIssue>();

    public void ResetScreenDocument(ScreenTarget target) => _service?.ResetScreenDocument(target);

    public void Connect() => _broker.ConnectAndSend();
    public void Disconnect() => _broker.Disconnect();
    public void ClearLogs() => _broker.ClearLogs();

    public void SaveCredentials(string ccuId, string passKey, string deviceName)
    {
        _broker.UpdateCredentials(ccuId, passKey);
        _broker.UpdateDeviceName(deviceName);
    }

    public void SetVpbEnabled(bool enabled)
    {
        if (_service != null)
        {
            _service.SetVpbEnabled(enabled);
            return;
        }
        Update(s => s with { VpbEnabled = enabled, VpbStatus = enabled ? "Idle" : "Disabled" });
    }

    public void SetNavigationEnabled(bool enabled)
    {
        if (_service != null)
        {
            _service.SetNavigationEnabled(enabled);
            return;
        }
        if (_navigation != null)
        {
            if (enabled) _navigation.Activate();
            else _navigation.Deactivate();
        }
        Update(s => s with { NavigationEnabled = enabled });
    }

    public async Task<bool> SendVpbImageAsync(byte[] encodedImage)
    {
        if (_service != null) return await _service.SendVpbImageAsync(encodedImage);

        var image = await Task.Run(() => VpbImageBuilder.FromEncodedImage(encodedImage));
        return image != null && await _broker.SendVirtualPitBoardMessageAsync(image);
    }

    public Task<bool> ClearVpbAsync() => _service != null ? _service.ClearVpbAsync() : _broker.ClearVirtualPitBoardAsync();

    public void OpenNotificationAccessSettings()
    {
        if (!Platform.IsAndroid()) return;
        try { DeviceRuntime.MediaSessionMonitor.OpenAccessSettings(); }
        catch (Exception)
        {
        }
    }

    private void Update(Func<BikeSnapshot, BikeSnapshot> change)
    {
        lock (_gate) _snapshot = change(_snapshot);
        RaiseChanged();
    }

    private int _changedPending;

    // The log can add dozens of lines a second; the pages read the latest snapshot, so one notification per 150 ms is plenty.
    private void RaiseChanged()
    {
        if (Interlocked.Exchange(ref _changedPending, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(150);
            Interlocked.Exchange(ref _changedPending, 0);
            Changed?.Invoke();
        });
    }
}
