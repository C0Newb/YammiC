using CNCO.YccuSdk.Can;
using CNCO.YccuSdk.Handlers;
using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Navigation;
using CNCO.YccuSdk.Screens;
using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.App;
using CNCO.YccuSdk.Packets.Ccu;
using CNCO.YccuSdk.Platform;
using CNCO.YccuSdk.Reels;
using CNCO.YccuSdk.Providers;
using CNCO.YccuSdk.Vpb;
using CNCO.YccuSdk.Yrc;
using SkiaSharp;

namespace CNCO.YccuSdk.Bike;

/// <summary>
/// Optional pieces for <see cref="BikeService"/>. Anything left null is simply not wired up.
/// </summary>
public class BikeServiceOptions
{
    /// <summary>
    /// Weather source; defaults to <see cref="DemoWeatherProvider"/>.
    /// </summary>
    public IYmcWeatherProvider? WeatherProvider { get; init; }

    /// <summary>
    /// Phone-call events (from CNCO.Unify.Device). Null: no call relaying.
    /// </summary>
    public IYmcCallStateSource? CallSource { get; init; }

    /// <summary>
    /// Media-session events (from CNCO.Unify.Device). Null: no media relaying.
    /// </summary>
    public IYmcMediaSource? MediaSource { get; init; }

    /// <summary>
    /// Handlebar media buttons. Null: handler not registered.
    /// </summary>
    public IYmcMediaController? MediaController { get; init; }

    /// <summary>
    /// Handlebar call answer/reject. Null: handler not registered.
    /// </summary>
    public IYmcCallController? CallController { get; init; }

    /// <summary>
    /// Whether a bike headset is reported as connected (the CCU gates music metadata behind it).
    /// </summary>
    public bool HeadsetConnected { get; init; } = true;

    /// <summary>
    /// The Kotlin app pushed a "YammiC - It works!" notification on every connect.
    /// </summary>
    public bool SendTestNotification { get; init; } = true;

    /// <summary>
    /// If set, sent to the VPB 15s after connecting (the Kotlin app's sandwich image). Any common image format.
    /// </summary>
    public byte[]? DemoVpbImage { get; init; }

    /// <summary>
    /// If set, played on the navigation screen once it becomes active (the Kotlin app's rotating sandwich). GIF or WebP.
    /// </summary>
    public byte[]? DemoNavigationAnimation { get; init; }

    /// <summary>
    /// Battery saver state (from CNCO.Unify.Device). Null: the phone is never treated as saving power.
    /// </summary>
    public IYmcPowerSource? PowerSource { get; init; }

    /// <summary>
    /// Draws the custom screens' pages (the UI project provides an Avalonia one). Null: custom screens aren't drawn.
    /// </summary>
    public IScreenRenderer? ScreenRenderer { get; init; }

    /// <summary>
    /// Report a saved home and office to the CCU so its "Go home"/"Go to work" buttons reach the phone (they are
    /// answered with a pit board message). Turn off if the CCU rejects the navigation link.
    /// </summary>
    public bool ReportHomeAndOffice { get; init; } = true;

    public bool VpbEnabled { get; init; } = true;

    public bool NavigationEnabled { get; init; } = true;

    /// <summary>
    /// Plays the reels folder for Reels Mode. Null: Reels Mode is unavailable (the platform has no video support).
    /// </summary>
    public IReelsPlatform? ReelsPlatform { get; init; }
}

/// <summary>
/// The platform-neutral part of the Kotlin BikeForegroundService: owns the request handlers, the
/// on-connect sequence, the VPB / navigation toggles and the call + media relays. The Android head wraps this in a
/// real foreground service (notification, keep-alive); everything else lives here so other platforms can reuse it.
/// </summary>
public class BikeService
{
    private readonly Logger _log;
    private readonly YmcConnectionBroker _controller;
    private readonly NavigationSdk _navigation;
    private readonly YmcPlatform _platform;
    private readonly BikeServiceOptions _options;
    private readonly CancellationToken _lifetime;

    private readonly YmcCallStateRelay? _phoneCallMonitor;
    private readonly YmcMediaStateRelay? _mediaStateMonitor;

    private const string AutoConnectKey = "auto_connect";
    private const string HomeAndWorkKey = "report_home_work";
    private const string LegacyDashboardKey = "nav_dashboard";
    private const string NavModeKey = "nav_mode";
    private const string NavRefreshKey = "nav_refresh";
    private const string ReelsFolderKey = "reels_folder";
    private const string ReelsFolderNameKey = "reels_folder_name";
    private const string VpbScreenKey = "vpb_screen";
    private const string CanDataKey = "can_data";
    private const string CanIdsKey = "can_ids";
    private const string CanExtraIdsKey = "can_extra_ids";

    private readonly CanTelemetry _can;
    private bool _canDataEnabled;
    private IReadOnlyList<int> _canIds = CanSignals.DefaultIds;
    private IReadOnlyList<int> _canExtraIds = Array.Empty<int>();
    // IDs other features need regardless of the user's CAN settings (the Track Timer's "ready" flag and recorded signals). Internal:
    // not shown to or saved for the user.
    private IReadOnlyList<int> _requiredCanIds = Array.Empty<int>();
    // While true the custom VPB screen stays off the board so a message from another feature (the track name) is not overwritten.
    private volatile bool _boardOverride;
    private CancellationTokenSource? _boardMessage;
    private readonly BikeDiscovery _discovery;
    private readonly YrcSettingsClient _yrc;
    private readonly ProfileLibrary _profileLibrary;
    private readonly LapTimer _lapTimer;
    private readonly ScreenDocumentStore _screenDocs;
    private readonly ScreenController _navController;
    private readonly ScreenController _vpbController;
    private readonly ScreenPresenter _navScreen;
    private readonly RoadNameSender _roadName;
    private readonly ScreenPresenter _vpbScreen;
    private bool _vpbScreenEnabled;
    private DateTime _connectedAt = DateTime.MaxValue;
    private NavMode _navMode;
    private readonly ReelsController? _reels;
    private CancellationTokenSource? _dayNightLoop;

    private bool _homeAndWorkEnabled;

    private bool _autoConnectEnabled;
    private bool _vpbEnabled;
    private bool _navigationEnabled;
    private string _vpbStatus = "Idle";
    private bool _navigationWasActive;
    private bool _started;

    public BikeService(
        YmcConnectionBroker controller,
        NavigationSdk navigation,
        YmcPlatform platform,
        Logger logger,
        BikeServiceOptions? options = null,
        CancellationToken lifetime = default)
    {
        _controller = controller;
        _navigation = navigation;
        _platform = platform;
        _log = logger;
        _options = options ?? new BikeServiceOptions();
        _lifetime = lifetime;
        _vpbEnabled = _options.VpbEnabled;
        _autoConnectEnabled = platform.Storage.GetString(AutoConnectKey) != "0"; // on unless the user turned it off
        _navigationEnabled = _options.NavigationEnabled;
        var storedHomeAndWork = platform.Storage.GetString(HomeAndWorkKey);
        _homeAndWorkEnabled = storedHomeAndWork == null ? _options.ReportHomeAndOffice : storedHomeAndWork != "0";
        _navMode = LoadNavMode(platform.Storage);
        _navRefreshMode = NavRefreshProfile.ParseMode(platform.Storage.GetString(NavRefreshKey));
        _batterySaver = _options.PowerSource?.IsPowerSaveMode ?? false;
        if (_options.ReelsPlatform != null)
            _reels = new ReelsController(_options.ReelsPlatform, navigation, () => ReelsFolder, logger, () => NavRefresh);
        _screenDocs = new ScreenDocumentStore(platform.Storage);
        var screenMedia = new ScreenMediaActions(this);
        _navController = new ScreenController(_screenDocs.Load(ScreenTarget.Navigation), screenMedia);
        _vpbController = new ScreenController(_screenDocs.Load(ScreenTarget.VirtualPitBoard), screenMedia);
        _vpbScreenEnabled = platform.Storage.GetString(VpbScreenKey) == "1"; // off unless the user turned it on
        _canDataEnabled = platform.Storage.GetString(CanDataKey) == "1"; // off unless the user turned it on
        var storedIds = platform.Storage.GetString(CanIdsKey);
        if (storedIds != null)
            _canIds = storedIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out var id) ? id : -1).Where(CanSignals.KnownIds.Contains).ToList();
        _can = new CanTelemetry(logger);
        _discovery = new BikeDiscovery(logger);
        _yrc = new YrcSettingsClient(controller, logger);
        _profileLibrary = new ProfileLibrary(platform.Storage);
        _yrc.Library = _profileLibrary;
        _lapTimer = new LapTimer(controller, logger);
        var storedExtra = platform.Storage.GetString(CanExtraIdsKey);
        if (!string.IsNullOrEmpty(storedExtra))
            _canExtraIds = storedExtra.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out var id) ? id : -1).Where(id => id is >= 0 and <= 0xFFFF).ToList();
        _can.Updated += () => CanChanged?.Invoke();

        ScreenSnapshot CurrentSnapshot(ScreenController controller) =>
            controller.Snapshot(new ScreenContext(DateTime.Now, _mediaStateMonitor?.CurrentMetadata, DayNightClock.For(DateTime.Now) == NavDayNightMode.Night, 1f,
                _canDataEnabled ? _can.Values(DateTime.UtcNow) : null, _albumArt));

        _roadName = new RoadNameSender(() => navigation.State is NavigationState.Active, navigation.SetRoadName);

        _navScreen = new ScreenPresenter(
            "NavScreen",
            ScreenTarget.Navigation,
            _options.ScreenRenderer,
            () => _navController.CurrentPage,
            () => CurrentSnapshot(_navController),
            () => (navigation.DisplaySize.Width, navigation.DisplaySize.Height),
            () => navigation.State is NavigationState.Active && navigation.ImagesWanted,
            jpeg =>
            {
                navigation.PushImage(jpeg);
                return Task.FromResult(true);
            },
            tick: () => NavRefresh.ScreenTick,
            minInterval: TimeSpan.Zero,
            quality: () => NavRefresh.ScreenJpegQuality,
            logger,
            wantsFastTick: () => WantsFastTick(_navController.CurrentPage),
            fastTick: () => _navController.CurrentPage.UsesScroll ? NavRefresh.ScrollStep : NavRefresh.ScreenFastTick,
            scrollStep: () => NavRefresh.ScrollStep,
            observe: snapshot => _roadName.Update(_navController.RoadNameFor(snapshot.Context)));

        // A pit board picture is a multi-chunk transfer (about half a second on the link), so: check every second, send at most
        // every 3 s, wait 5 s after the bike refused one (it does while it is on a screen that can't show it), and not while the
        // connect sequence is still using the board (its "Connected" message).
        _vpbScreen = new ScreenPresenter(
            "VpbScreen",
            ScreenTarget.VirtualPitBoard,
            _options.ScreenRenderer,
            () => _vpbController.CurrentPage,
            () => CurrentSnapshot(_vpbController),
            () => ScreenSizes.DesignSize(ScreenTarget.VirtualPitBoard),
            // The pit board can't be seen (and the bike refuses it) while the bike is showing the navigation screen.
            () => _vpbEnabled && _vpbScreenEnabled && !_boardOverride && _controller.IsBikeConnected &&
                  _navigation.ImageDemand != NavImageDemand.Wanted &&
                  DateTime.UtcNow - _connectedAt > TimeSpan.FromSeconds(8),
            async jpeg =>
            {
                var sent = await SendBoardAsync(jpeg).ConfigureAwait(false);
                if (sent) SetVpbStatus("Custom screen");
                return sent;
            },
            tick: () => TimeSpan.FromSeconds(1),
            minInterval: TimeSpan.FromSeconds(3),
            quality: () => 85,
            logger,
            retryInterval: TimeSpan.FromSeconds(5));

        _navController.Changed += () => _navScreen.Refresh();
        // When the bike starts asking for the picture, draw it afresh (nothing was rendered while it was not looking).
        _navigation.ImageDemandChanged += demand =>
        {
            if (demand == NavImageDemand.Wanted)
            {
                _roadName.Reset(); // the bike may have dropped the text while it was on another screen
                _navScreen.Invalidate();
            }
            // The bike has just left the navigation screen: the pit board may be showable now, so try it without waiting.
            if (demand == NavImageDemand.Paused) _vpbScreen.RetryNow();
            // Reels play sound on the phone: when the bike leaves the navigation screen it goes quiet, and returns with it.
            if (_navMode == NavMode.Reels && _navigationWasActive)
            {
                if (demand == NavImageDemand.Paused) _reels?.Suspend();
                else if (demand == NavImageDemand.Wanted) _reels?.Resume();
            }
        };
        _vpbController.Changed += () => _vpbScreen.Refresh();

        if (_options.CallSource != null)
        {
            _phoneCallMonitor = new YmcCallStateRelay(_options.CallSource, () => _controller.MessageBroker, logger);
        }
        if (_options.MediaSource != null)
        {
            _mediaStateMonitor = new YmcMediaStateRelay(_options.MediaSource, () => _controller.MessageBroker, logger);
            _mediaStateMonitor.LatestMetadataChanged += metadata =>
            {
                MediaMetadataChanged?.Invoke(metadata);
                // The picture is fetched off this thread (it may be the main one) and the screens redraw once it is in.
                _ = Task.Run(() =>
                {
                    RefreshAlbumArt(metadata);
                    _navScreen.Refresh();
                    _vpbScreen.Refresh();
                });
            };
        }
    }

    // The current track's picture, fetched when the track changes (and retried now and then while a track has none, since apps
    // often attach it a moment after the title).
    private volatile byte[]? _albumArt;
    private string? _albumArtKey;
    private long _albumArtTriedAt;

    private void RefreshAlbumArt(YmcMediaMetaData metadata)
    {
        var source = _options.MediaSource;
        if (source == null) return;

        var key = $"{metadata.ArtistName}|{metadata.AlbumName}|{metadata.SongName}";
        var now = System.Environment.TickCount64;
        lock (_artLock)
        {
            if (key == _albumArtKey && (_albumArt != null || now - _albumArtTriedAt < 3000)) return;
            if (key != _albumArtKey) _albumArt = null; // a new track: don't show the last one's cover while this one loads
            _albumArtKey = key;
            _albumArtTriedAt = now;
        }

        byte[]? art = null;
        try
        {
            art = source.GetArtwork();
        }
        catch (Exception t)
        {
            _log.Debug($"album art: {t.GetType().Name}: {t.Message}");
        }
        _albumArt = art;
    }

    private readonly object _artLock = new();

    public YmcConnectionBroker Controller => _controller;

    public NavigationSdk Navigation => _navigation;

    // -- Feature toggles ------------------------------------------------------
    // VPB (Virtual Pit Board) and navigation share the CCU's display and may interfere with each
    // other. These let the user disable one independently to isolate/avoid conflicts.

    public bool VpbEnabled => _vpbEnabled;

    public bool NavigationEnabled => _navigationEnabled;

    public string VpbStatus => _vpbStatus;

    /// <summary>
    /// Latest media metadata, or null when there is no media source / nothing yet.
    /// </summary>
    public YmcMediaMetaData? MediaMetadata => _mediaStateMonitor?.LatestMetadata;

    /// <summary>
    /// Raised when <see cref="VpbEnabled"/>, <see cref="NavigationEnabled"/> or <see cref="VpbStatus"/> change.
    /// </summary>
    public event Action? StatusChanged;

    public event Action<YmcMediaMetaData?>? MediaMetadataChanged;

    /// <summary>
    /// Toggles VPB pushes. When disabled, all VPB sends (onConnect, ticker, nav-button echo) are
    /// dropped and any currently-displayed VPB message is cleared. Takes effect immediately if
    /// the bike is connected.
    /// </summary>
    public void SetVpbEnabled(bool enabled)
    {
        _vpbEnabled = enabled;
        if (!enabled)
        {
            SetVpbStatus("Disabled");
            if (_controller.IsBikeConnected)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _controller.ClearVirtualPitBoardAsync().ConfigureAwait(false);
                    }
                    catch (Exception t)
                    {
                        _log.Debug($"setVpbEnabled: clearVirtualPitBoard failed: {t.Message}");
                    }
                });
            }
        }
        else
        {
            SetVpbStatus("Idle");
        }
    }

    /// <summary>
    /// Shows a turn-by-turn instruction on the bike's guidance display (it travels over the navigation link, so the bike's navigation
    /// screen must be open). Returns false when that link is not up.
    /// </summary>
    public bool ShowTurn(Navigation.Protocol.TurnGuidance turn, double remainingMeters = 0, int remainingMinutes = 0) =>
        _navigation.SetTurn(turn, remainingMeters, remainingMinutes);

    /// <summary>Takes the instruction off the guidance display.</summary>
    public bool ClearTurn() => _navigation.ClearTurn();

    /// <summary>
    /// Sends an image (PNG, JPEG, ...) to the Virtual Pit Board, converting it to the 600x306 JPEG the CCU wants.
    /// Returns false if VPB is disabled, the bike isn't connected, the image can't be decoded, or the CCU rejected it.
    /// </summary>
    public async Task<bool> SendVpbImageAsync(byte[] encodedImage)
    {
        if (!_vpbEnabled) return false;
        var image = await Task.Run(() => VpbImageBuilder.FromEncodedImage(encodedImage)).ConfigureAwait(false);
        if (image == null)
        {
            _log.Debug("sendVpbImage: failed to decode image");
            return false;
        }
        var sent = await SendBoardAsync(image).ConfigureAwait(false);
        if (sent) SetVpbStatus("Custom image");
        return sent;
    }

    /// <summary>
    /// Clears whatever the Virtual Pit Board is showing. False if not connected or the CCU rejected it.
    /// </summary>
    public async Task<bool> ClearVpbAsync()
    {
        var cleared = await _controller.ClearVirtualPitBoardAsync().ConfigureAwait(false);
        if (cleared) SetVpbStatus("Idle");
        return cleared;
    }

    /// <summary>
    /// Toggles the navigation-screen link. Activates/deactivates it immediately if the bike is
    /// connected; otherwise just remembered for the next connection.
    /// </summary>
    public void SetNavigationEnabled(bool enabled)
    {
        _navigationEnabled = enabled;
        StatusChanged?.Invoke();
        if (!_controller.IsBikeConnected) return;
        if (enabled)
        {
            _navigation.Activate();
        }
        else
        {
            _navigation.Deactivate();
        }
    }

    private void SetVpbStatus(string status)
    {
        _vpbStatus = status;
        StatusChanged?.Invoke();
    }

    // -- Lifecycle ------------------------------------------------------------

    /// <summary>
    /// Registers the CCU request handlers and the on-connect job, and starts the monitors. Idempotent.
    /// </summary>
    public void Start()
    {
        if (_started) return;
        _started = true;

        var handler = _controller.MessageHandler;
        if (_options.MediaController != null)
        {
            handler.RegisterHandler(
                YmcCommandCode.MusicControlRequest,
                (ph, d) => new CcuMusicControlRequest(ph, d),
                new CcuMediaControlRequestHandler(_options.MediaController, _log));
        }
        handler.RegisterHandler(
            YmcCommandCode.PhoneVolumeControlRequest,
            (ph, d) => new CcuVolumeControlRequest(ph, d),
            new CcuVolumeControlRequestHandler(_platform.Volume, _log));
        handler.RegisterHandler(
            YmcCommandCode.BluetoothHeadsetStateRequest,
            (ph, d) => new CcuBluetoothHeadsetStateRequest(ph, d),
            new CcuBluetoothHeadsetStateRequestHandler(_options.HeadsetConnected));
        handler.RegisterHandler(
            YmcCommandCode.WeatherRequest,
            (ph, d) => new CcuWeatherRequest(ph, d),
            new CcuWeatherRequestHandler(_options.WeatherProvider ?? new DemoWeatherProvider()));
        if (_options.CallController != null)
        {
            handler.RegisterHandler(
                YmcCommandCode.IncomingCallControl,
                (ph, d) => new CcuIncomingCallControlRequest(ph, d),
                new CcuIncomingCallControlRequestHandler(_options.CallController));
        }
        handler.RegisterHandler(
            YmcCommandCode.PhoneCellSignalLevelRequest,
            (ph, d) => new CcuPhoneCellSignalLevelRequest(ph, d),
            new CcuPhoneCellSignalLevelRequestHandler(_platform.PhoneStatus));

        _controller.AddOnConnectJob(OnConnectAsync);
        _can.Start(_controller.Notifications, _lifetime);
        _discovery.Start(_controller.Notifications, _lifetime);
        _yrc.Start(_controller.Notifications, _lifetime);
        _lapTimer.Start(_controller.Notifications, _lifetime);

        _phoneCallMonitor?.Start();
        _mediaStateMonitor?.Start();

        var power = _options.PowerSource;
        if (power != null)
        {
            power.PowerSaveModeChanged += OnPowerSaveModeChanged;
            power.Start();
            _batterySaver = power.IsPowerSaveMode;
        }
        ApplyNavRefresh();

        _navigation.StateChanged += OnNavigationStateChanged;
        _navigation.ReportHomeAndOffice = _homeAndWorkEnabled;
        _navigation.SetDayNightMode(DayNightClock.For(DateTime.Now));
        if (_vpbScreenEnabled) _vpbScreen.Start();
        _navigation.OnZoomInRequested = () =>
        {
            _log.Debug("Navigation: CCU requested zoom in");
            OnZoomRequest(zoomIn: true);
        };
        _navigation.OnZoomOutRequested = () =>
        {
            _log.Debug("Navigation: CCU requested zoom out");
            OnZoomRequest(zoomIn: false);
        };
        // Three demo favorites until there is a real saved-places source.
        _navigation.SetPois(NavPoiList.Favorites, new[]
        {
            new NavPoi("Sandwich Shop", "Main St", 1, NavPoiDirection.Front),
            new NavPoi("Coffee Stop", "Oak Ave", 2, NavPoiDirection.Right),
            new NavPoi("Scenic Overlook", "Ridge Rd", 3, NavPoiDirection.BackLeft),
        });
        _navigation.OnPoiSelected = selection =>
        {
            var name = selection.Poi?.Name ?? $"item {selection.ItemIndex}";
            _log.Debug($"Navigation: CCU selected {selection.List} #{selection.ItemIndex} ({name}), routeOption={selection.RouteOption}");
            SendNavRequestNotification($"Selected: {name}");
        };
        _navigation.OnGoHomeRequested = () =>
        {
            _log.Debug("Navigation: CCU requested 'Go home'");
            SendNavRequestNotification("Go home clicked");
        };
        _navigation.OnGoToWorkRequested = () =>
        {
            _log.Debug("Navigation: CCU requested 'Go to work'");
            SendNavRequestNotification("Go to work clicked");
        };
    }

    /// <summary>
    /// Stops the monitors and tears the connection down.
    /// </summary>
    public void Stop()
    {
        _phoneCallMonitor?.Stop();
        _mediaStateMonitor?.Stop();
        var power = _options.PowerSource;
        if (power != null)
        {
            power.PowerSaveModeChanged -= OnPowerSaveModeChanged;
            power.Stop();
        }
        _navigation.StateChanged -= OnNavigationStateChanged;
        _navScreen.Stop();
        _reels?.Stop();
        _vpbScreen.Stop();
        StopDayNightLoop();
        _navigation.Deactivate();
        _controller.Disconnect();
        _started = false;
    }

    /// <summary>
    /// Call after the user grants READ_PHONE_STATE etc. at runtime so monitoring can start.
    /// </summary>
    public void RestartPhoneCallMonitor() => _phoneCallMonitor?.Start();

    /// <summary>
    /// Re-starts the call and media monitors. Both are safe to call repeatedly and pick up where a
    /// permission or access grant was missing the last time (e.g. when the app is resumed after the user visited settings).
    /// </summary>
    public void RestartMonitors()
    {
        _phoneCallMonitor?.Start();
        _mediaStateMonitor?.Start();
    }

    // -- Bike (CAN) data ---------------------------------------------------

    /// <summary>
    /// The latest CAN readings (raw and decoded).
    /// </summary>
    public CanTelemetry Can => _can;

    /// <summary>
    /// The bike's ride-mode settings (profiles).
    /// </summary>
    public YrcSettingsClient Yrc => _yrc;

    /// <summary>
    /// The rider's saved ride modes.
    /// </summary>
    public ProfileLibrary ProfileLibrary => _profileLibrary;

    /// <summary>
    /// A stopwatch that reports laps to the bike.
    /// </summary>
    public LapTimer LapTimer => _lapTimer;

    /// <summary>
    /// Decoded CAN values that are fresh right now (all null while <see cref="CanDataEnabled"/> is off).
    /// </summary>
    public CanValues CanValues => _canDataEnabled ? _can.Values(DateTime.UtcNow) : CanValues.Empty;

    /// <summary>
    /// Raised when a CAN response arrived (as often as the poll interval), or the setting changed.
    /// </summary>
    public event Action? CanChanged;

    /// <summary>
    /// Whether the bike is asked for CAN data on connect (it adds continuous Bluetooth traffic). Persisted; turning it on
    /// asks right away when connected. Turning it off stops using the data now but the bike keeps sending until the next connection.
    /// </summary>
    public bool CanDataEnabled
    {
        get => _canDataEnabled;
        set
        {
            if (_canDataEnabled == value) return;
            _canDataEnabled = value;
            _platform.Storage.SetString(CanDataKey, value ? "1" : "0");
            if (value && _controller.IsBikeConnected)
            {
                var broker = _controller.MessageBroker;
                if (broker != null) _ = Task.Run(() => RunOnConnectStepAsync("request CAN data", () => RequestCanDataAsync(broker)));
            }
            CanChanged?.Invoke();
        }
    }

    private Task RequestCanDataAsync(YmcMessageBroker broker)
    {
        // The user's own selection only counts while their CAN toggle is on; the required IDs are always asked for.
        var ids = (_canDataEnabled ? _canIds.Concat(_canExtraIds) : Enumerable.Empty<int>()).Concat(_requiredCanIds).Distinct().ToList();
        return ids.Count == 0
            ? Task.CompletedTask
            : new AppCanIntervalRequest(broker.PacketHandler, ids, CanIntervalMs).SendAsync(broker.ConnectionContext);
    }

    /// <summary>
    /// CAN IDs that another feature needs even when the user's CAN data is off (not persisted; the feature sets them again at
    /// start-up). They share the one request interval. Applied right away when connected.
    /// </summary>
    public void SetRequiredCanIds(IEnumerable<int> ids)
    {
        var list = ids.Where(id => id is >= 0 and <= 0xFFFF).Distinct().OrderBy(i => i).ToList();
        if (list.SequenceEqual(_requiredCanIds)) return;
        _requiredCanIds = list;
        if (!_controller.IsBikeConnected) return;
        var broker = _controller.MessageBroker;
        if (broker != null) _ = Task.Run(() => RunOnConnectStepAsync("request CAN data", () => RequestCanDataAsync(broker)));
    }

    /// <summary>
    /// Shows <paramref name="text"/> on the board and keeps the custom VPB screen off it until <see cref="ReleaseBoardAsync"/>.
    /// Returns whether the message was sent.
    /// </summary>
    public async Task<bool> ShowBoardMessageAsync(string text)
    {
        _boardOverride = true;
        _boardMessage?.Cancel();
        var cancellation = _boardMessage = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        if (!_vpbEnabled) return false;

        // The bike changes screens at the moment a session arms (it stops the navigation picture), and answers a pit board request
        // sent in that moment with silence or a rejection. So ask again every few seconds until it takes the message, or the
        // message is released, or about the time the bike needs to become ready at all has passed.
        var image = VpbImageBuilder.FromText(text, SKColors.White);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        for (var attempt = 1; !cancellation.IsCancellationRequested && DateTime.UtcNow < deadline; attempt++)
        {
            if (_controller.IsBikeConnected)
            {
                var sent = await SendBoardAsync(image).ConfigureAwait(false);
                if (sent)
                {
                    SetVpbStatus("Message");
                    _log.Info($"Board message '{text}' shown after {attempt} attempt(s)");
                    return true;
                }
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        _log.Info($"Board message '{text}' not shown (released or the bike did not accept it)");
        return false;
    }

    /// <summary>
    /// Lets the custom VPB screen draw again after <see cref="ShowBoardMessageAsync"/>.
    /// </summary>
    public Task ReleaseBoardAsync()
    {
        _boardMessage?.Cancel();
        _boardOverride = false;
        _vpbScreen.Invalidate();
        return Task.CompletedTask;
    }

    // The page and the screens refresh about twice a second, so asking more than 4 times a second only adds traffic.
    private const int CanIntervalMs = 250;

    /// <summary>
    /// Additional CAN IDs to ask for, any value 0..0xFFFF, for exploring what the bike reports (they show up in the raw table
    /// if the bike answers). Persisted; applied right away when connected.
    /// </summary>
    public IReadOnlyList<int> CanExtraIds
    {
        get => _canExtraIds;
        set
        {
            var ids = value.Where(id => id is >= 0 and <= 0xFFFF).Distinct().OrderBy(i => i).Take(64).ToList();
            if (ids.SequenceEqual(_canExtraIds)) return;
            _canExtraIds = ids;
            _platform.Storage.SetString(CanExtraIdsKey, string.Join(",", ids));
            if (_canDataEnabled && _controller.IsBikeConnected)
            {
                var broker = _controller.MessageBroker;
                if (broker != null) _ = Task.Run(() => RunOnConnectStepAsync("request CAN data", () => RequestCanDataAsync(broker)));
            }
            CanChanged?.Invoke();
        }
    }

    /// <summary>
    /// Asks the bike for its ECU server list and, for each server, the data IDs it supports. Returns a text report.
    /// </summary>
    public async Task<string> DiscoverSupportedSignalsAsync()
    {
        var broker = _controller.MessageBroker;
        if (broker == null) return "Not connected.";

        _discovery.Reset();
        await new AppConnectServerListRequest(broker.PacketHandler).SendAsync(broker.ConnectionContext).ConfigureAwait(false);
        if (!await BikeDiscovery.WaitAsync(() => _discovery.Servers != null, TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            return "The bike did not answer the server list request (see the log for any raw reply).";

        foreach (var server in _discovery.Servers!)
        {
            await new AppSupportIdListRequest(broker.PacketHandler, server).SendAsync(broker.ConnectionContext).ConfigureAwait(false);
            await BikeDiscovery.WaitAsync(() => _discovery.HasSupportFor(server), TimeSpan.FromSeconds(4)).ConfigureAwait(false);
        }
        return _discovery.Report();
    }

    /// <summary>
    /// The CAN IDs asked for (a subset of <see cref="CanSignals.KnownIds"/>). Persisted; applied right away when connected.
    /// </summary>
    public IReadOnlyList<int> CanSignalIds
    {
        get => _canIds;
        set
        {
            var ids = value.Where(CanSignals.KnownIds.Contains).Distinct().OrderBy(i => i).ToList();
            if (ids.SequenceEqual(_canIds)) return;
            _canIds = ids;
            _platform.Storage.SetString(CanIdsKey, string.Join(",", ids));
            if (_canDataEnabled && _controller.IsBikeConnected)
            {
                var broker = _controller.MessageBroker;
                if (broker != null) _ = Task.Run(() => RunOnConnectStepAsync("request CAN data", () => RequestCanDataAsync(broker)));
            }
            CanChanged?.Invoke();
        }
    }

    // -- Auto connect ---------------------------------------------------------

    /// <summary>
    /// Whether the navigation link reports a saved home and work address, which activates the CCU's "Go home" /
    /// "Go to work" buttons. Persisted; takes effect immediately by restarting the navigation link if it is running.
    /// </summary>
    public bool HomeAndWorkEnabled
    {
        get => _homeAndWorkEnabled;
        set
        {
            if (_homeAndWorkEnabled == value) return;
            _homeAndWorkEnabled = value;
            _platform.Storage.SetString(HomeAndWorkKey, value ? "1" : "0");
            _navigation.ReportHomeAndOffice = value;
            if (_navigationEnabled && _controller.IsBikeConnected)
            {
                _navigation.Deactivate();
                _navigation.Activate();
            }
        }
    }

    /// <summary>
    /// Whether <see cref="OnBikeBluetoothConnected"/> starts a connection attempt.
    /// </summary>
    public bool AutoConnectEnabled
    {
        get => _autoConnectEnabled;
        set
        {
            _autoConnectEnabled = value;
            _platform.Storage.SetString(AutoConnectKey, value ? "1" : "0");
        }
    }

    /// <summary>
    /// Call for every Bluetooth device that connects to the phone; works out whether it is the bike and, if so, auto connects.
    /// The bike is recognised by its configured name, any YCCU name, or the address it had when last found (its Classic Bluetooth
    /// name can differ from the BLE name it advertises, so the address is the reliable one). Every event is logged so a
    /// missed auto connect can be diagnosed.
    /// </summary>
    public void OnBluetoothDeviceConnected(string? name, string? address)
    {
        var knownAddress = _controller.BikeAddress;
        var configuredName = _controller.State.DeviceName;
        var isBike =
            (!string.IsNullOrEmpty(address) && string.Equals(address, knownAddress, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(name) &&
             (string.Equals(name, configuredName, StringComparison.OrdinalIgnoreCase) ||
              name.StartsWith("YCCU", StringComparison.OrdinalIgnoreCase) ||
              name.Contains("CCU", StringComparison.OrdinalIgnoreCase)));
        _log.Debug($"bluetooth: ACL connected name={name ?? "?"} address={address ?? "?"} knownBike={knownAddress ?? "?"} isBike={isBike}");
        if (isBike) OnBikeBluetoothConnected(name ?? address);
    }

    /// <summary>
    /// Call when the OS reports that the phone's Bluetooth connected to the bike (a YCCU device).
    /// Starts a connection attempt unless one is running, the bike is already connected, or auto connect is off.
    /// This is event driven, so unlike polling/scanning it costs nothing while the bike is away.
    /// </summary>
    public void OnBikeBluetoothConnected(string? deviceName)
    {
        if (!AutoConnectEnabled)
        {
            _log.Debug($"autoConnect: {deviceName} connected, but auto connect is off");
            return;
        }
        if (_controller.IsBikeConnected || _controller.IsConnecting)
        {
            _log.Debug($"autoConnect: {deviceName} connected, already connected/connecting");
            return;
        }

        _log.Debug($"autoConnect: {deviceName} connected, connecting");
        try
        {
            _controller.ConnectAndSend();
        }
        catch (Exception e)
        {
            _log.Debug($"autoConnect: failed to start connecting: {e.Message}");
        }
    }

    // -- On connect -----------------------------------------------------------

    // Each step below is isolated in its own try/catch: a DDT timeout on an earlier
    // step (e.g. the test notification) must not prevent later steps (the real call
    // state and media metadata push) from running. AddOnConnectJob only isolates
    // failures between separately-registered jobs, not between lines within one job.
    private async Task OnConnectAsync(YmcMessageBroker broker)
    {
        _connectedAt = DateTime.UtcNow;
        _can.Clear();
        _yrc.Clear();
        _vpbScreen.Invalidate();
        await RunOnConnectStepAsync("phone state", async () =>
        {
            await broker.SendPhoneStateAsync(_options.HeadsetConnected).ConfigureAwait(false);
            await broker.SendPhoneThermalStateAsync().ConfigureAwait(false);
            await broker.SendNotificationCategoryAsync(incomingCall: true, missedCall: true, social: true, other: true)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);

        if (_options.SendTestNotification)
        {
            await RunOnConnectStepAsync(
                "test notification",
                () => broker.SendNotificationAsync(id: 1, title: "YammiC", detail: "It works!")).ConfigureAwait(false);
        }

        if (_phoneCallMonitor != null)
        {
            await RunOnConnectStepAsync("first time call state", _phoneCallMonitor.PushCurrentCallStateAsync).ConfigureAwait(false);
        }
        if (_mediaStateMonitor != null)
        {
            await RunOnConnectStepAsync("first time media state", () =>
            {
                _mediaStateMonitor.PushMetadata();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        await RunOnConnectStepAsync("send Connected message via VPB", async () =>
        {
            if (_vpbEnabled)
            {
                var vpbConnected = await _controller.SendVirtualPitBoardMessageAsync(
                    VpbImageBuilder.FromText("Connected!", SKColors.Green)).ConfigureAwait(false);
                _log.Debug($"Virtual Pit Board Connected message result: {vpbConnected}");
                if (vpbConnected) SetVpbStatus("Connected message");
            }
            else
            {
                _log.Debug("VPB disabled, skipping Connected message");
            }
        }).ConfigureAwait(false);

        if (_canDataEnabled || _requiredCanIds.Count > 0)
            await RunOnConnectStepAsync("request CAN data", () => RequestCanDataAsync(broker)).ConfigureAwait(false);

        await RunOnConnectStepAsync("activate navigation screen", () =>
        {
            if (_navigationEnabled)
            {
                _navigation.Activate();
            }
            else
            {
                _log.Debug("Navigation disabled, skipping activate");
            }
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        if (_options.DemoVpbImage != null && !_vpbScreenEnabled) // the custom screen replaces the demo image
        {
            // The Kotlin job awaited a 15s delay right here, which held up the "Done" state and the periodic jobs
            // for that long. Run it on the side instead.
            _ = Task.Run(() => RunOnConnectStepAsync("send demo image via VPB (delayed 15s)", async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(15), _lifetime).ConfigureAwait(false);
                if (!_vpbEnabled)
                {
                    _log.Debug("VPB disabled, skipping demo image");
                    return;
                }
                var vpbImage = VpbImageBuilder.FromEncodedImage(_options.DemoVpbImage);
                if (vpbImage == null)
                {
                    _log.Debug("Failed to decode demo VPB image");
                    return;
                }
                var result = await _controller.SendVirtualPitBoardMessageAsync(vpbImage).ConfigureAwait(false);
                _log.Debug($"Demo image send result: {result}");
                if (result) SetVpbStatus("Demo image");
            }));
        }
    }

    /// <summary>
    /// Runs one onConnect step in isolation: a DDT timeout or other failure here is logged and
    /// swallowed instead of aborting the remaining steps in the same onConnect job.
    /// </summary>
    private async Task RunOnConnectStepAsync(string label, Func<Task> block)
    {
        _log.Debug($"onConnect step: {label}");
        try
        {
            await block().ConfigureAwait(false);
        }
        catch (Exception t)
        {
            _log.Debug($"onConnect step failed: {label}: {t.Message}");
        }
    }

    // Every pit board send goes through here so the log shows, next to the bike's answer, what the bike was doing: its screen value
    // (CAN 0x03BF byte 4, low nibble; only known while the Track Timer asks for it) and whether it wanted the navigation picture.
    private async Task<bool> SendBoardAsync(byte[] image)
    {
        var sent = await _controller.SendVirtualPitBoardMessageAsync(image).ConfigureAwait(false);
        var screen = "unknown";
        foreach (var reading in _can.Raw())
        {
            if (reading.Id == 0x03BF && reading.Data.Length >= 5) screen = (reading.Data[4] & 0x0F).ToString();
        }
        _log.Debug($"pit board {(sent ? "accepted" : "refused")}: bike screen value {screen}, navigation picture {_navigation.ImageDemand}");
        return sent;
    }

    // Bike data refreshes faster; so does a playing track whose position is on the page (it moves every second) and a page
    // with scrolling text while a track shows.
    private bool WantsFastTick(ScreenPage page)
    {
        if (_canDataEnabled && page.UsesCan) return true;

        var media = _mediaStateMonitor?.CurrentMetadata;
        if (media == null) return false;
        if (page.UsesMediaPosition && media.PlaybackState == YmcMediaPlaybackState.Playing) return true;
        return page.UsesScroll && new ScreenContext(DateTime.Now, media, false).HasMusic;
    }

    private void OnNavigationStateChanged(NavigationState state)
    {
        // The state is re-published with every frame sent; only react to the transitions into and out of Active.
        var active = state is NavigationState.Active;
        var justBecameActive = active && !_navigationWasActive;
        var justBecameInactive = !active && _navigationWasActive;
        _navigationWasActive = active;

        if (justBecameInactive)
        {
            _navScreen.Stop();
            _reels?.Stop();
            StopDayNightLoop();
        }
        if (!justBecameActive) return;

        StartDayNightLoop();
        ApplyNavigationContent();
    }

    /// <summary>
    /// What the navigation screen shows once active, per <see cref="NavigationMode"/>: the custom screen, the reels, or
    /// the demo animation.
    /// </summary>
    private void ApplyNavigationContent()
    {
        try
        {
            var mode = _navMode;
            if (mode == NavMode.Reels && _reels == null)
            {
                _log.Debug("Reels Mode isn't available on this platform, showing the custom screen instead");
                mode = NavMode.Ui;
            }

            if (mode == NavMode.Reels)
            {
                _log.Debug("Navigation active! Starting reels");
                _navScreen.Stop();
                _reels!.Start();
                return;
            }

            _reels?.Stop();
            if (mode == NavMode.Ui)
            {
                _log.Debug("Navigation active! Showing custom screen");
                _roadName.Reset();
                _navScreen.Start();
                return;
            }

            _navScreen.Stop();
            if (_options.DemoNavigationAnimation == null) return;
            _log.Debug("Navigation active! Pushing demo animation");
            _navigation.PushAnimatedImage(_options.DemoNavigationAnimation, quality: 90);
            _navigation.SetRoadName("This is a test");
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to update the navigation screen! {ex.Message}", ex);
        }
    }

    // -- Day / night ----------------------------------------------------------

    private void StartDayNightLoop()
    {
        StopDayNightLoop();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        _dayNightLoop = cancellation;
        var token = cancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    _navigation.SetDayNightMode(DayNightClock.For(DateTime.Now));
                    _navScreen.Refresh();
                    _vpbScreen.Refresh();
                    await Task.Delay(TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped.
            }
        });
    }

    private void StopDayNightLoop()
    {
        _dayNightLoop?.Cancel();
        _dayNightLoop = null;
    }

    // -- Navigation refresh rate ----------------------------------------------

    private NavRefreshMode _navRefreshMode;
    private volatile bool _batterySaver;
    private NavRefreshProfile? _appliedRefresh;

    /// <summary>
    /// The profile in force now: the chosen mode, with <see cref="NavRefreshMode.Auto"/> following the phone's battery saver.
    /// </summary>
    public NavRefreshProfile NavRefresh => NavRefreshProfile.For(_navRefreshMode, _batterySaver);

    /// <summary>
    /// How the navigation screen's refresh rate is chosen. Persisted; applies straight away.
    /// </summary>
    public NavRefreshMode NavRefreshMode
    {
        get => _navRefreshMode;
        set
        {
            if (_navRefreshMode == value) return;
            _navRefreshMode = value;
            _platform.Storage.SetString(NavRefreshKey, value.ToString());
            ApplyNavRefresh();
        }
    }

    private void OnPowerSaveModeChanged(bool on)
    {
        _batterySaver = on;
        ApplyNavRefresh();
    }

    /// <summary>
    /// Pushes the profile in force into the things that run at a rate. Does nothing if it didn't change.
    /// </summary>
    private void ApplyNavRefresh()
    {
        var profile = NavRefresh;
        if (ReferenceEquals(profile, _appliedRefresh)) return;
        var first = _appliedRefresh == null;
        _appliedRefresh = profile;
        _navigation.SetRates(profile.SenderFps, profile.IdleResendFps);
        _log.Debug($"navigation refresh: {profile.Name} (mode {_navRefreshMode}, battery saver {(_batterySaver ? "on" : "off")})");
        if (!first) _reels?.RefreshProfile();
    }

    // -- Custom screens -------------------------------------------------------

    /// <summary>
    /// The saved screen for <paramref name="target"/> (the built-in one if none was saved) as AXAML text.
    /// </summary>
    public string GetScreenAxaml(ScreenTarget target) => ScreenControllerFor(target).Document.ToAxaml();

    /// <summary>
    /// Checks and saves <paramref name="axaml"/> as the screen for <paramref name="target"/> and puts it on the bike's display
    /// straight away (if that screen is running). Returns the problems and saves nothing if there are any.
    /// </summary>
    public IReadOnlyList<ScreenIssue> SaveScreenAxaml(ScreenTarget target, string axaml)
    {
        ScreenDocument document;
        try
        {
            document = ScreenDocument.Parse(axaml);
        }
        catch (ScreenDocumentException e)
        {
            return new[] { new ScreenIssue(e.Line, e.Message) };
        }

        var issues = new List<ScreenIssue>();
        foreach (var page in document.Pages)
            foreach (var issue in ScreenXamlValidator.Validate(page.Xaml))
                issues.Add(issue with { Message = $"page \"{page.Id}\": {issue.Message}" });
        if (issues.Count > 0) return issues;

        document.Target = target;
        _screenDocs.Save(target, document);
        UseScreenDocument(target, document);
        return Array.Empty<ScreenIssue>();
    }

    /// <summary>
    /// Discards the saved screen for <paramref name="target"/> and goes back to the built-in one.
    /// </summary>
    public void ResetScreenDocument(ScreenTarget target)
    {
        _screenDocs.Reset(target);
        UseScreenDocument(target, _screenDocs.Load(target));
    }

    /// <summary>
    /// False where pages can't be drawn (no renderer, or the platform can't load XAML at run time).
    /// </summary>
    public bool ScreenRenderingSupported => _options.ScreenRenderer != null;

    private ScreenController ScreenControllerFor(ScreenTarget target) => target == ScreenTarget.Navigation ? _navController : _vpbController;

    private void UseScreenDocument(ScreenTarget target, ScreenDocument document)
    {
        ScreenControllerFor(target).SetDocument(document);
        if (target == ScreenTarget.Navigation) _navScreen.Invalidate();
        else _vpbScreen.Invalidate();
    }

    /// <summary>
    /// Whether the Virtual Pit Board shows the custom VPB screen (kept up to date, e.g. clock and music) instead of
    /// the demo image and one-off messages. Persisted; off by default.
    /// Images sent by hand are replaced again the next time the screen's content changes.
    /// </summary>
    public bool VpbScreenEnabled
    {
        get => _vpbScreenEnabled;
        set
        {
            if (_vpbScreenEnabled == value) return;
            _vpbScreenEnabled = value;
            _platform.Storage.SetString(VpbScreenKey, value ? "1" : "0");
            if (value) _vpbScreen.Start();
            else _vpbScreen.Stop();
        }
    }

    private static NavMode LoadNavMode(IYmcKeyValueStore storage)
    {
        if (Enum.TryParse<NavMode>(storage.GetString(NavModeKey), out var saved) && Enum.IsDefined(saved)) return saved;
        // Before there were three modes the setting was "custom screen on/off", on unless turned off.
        return storage.GetString(LegacyDashboardKey) == "0" ? NavMode.Sandwich : NavMode.Ui;
    }

    /// <summary>
    /// What the navigation screen shows: the sandwich, the custom screen (<see cref="GetScreenAxaml"/>) or the reels.
    /// Persisted; takes effect immediately if the navigation screen is active.
    /// </summary>
    public NavMode NavigationMode
    {
        get => _navMode;
        set
        {
            if (_navMode == value) return;
            _navMode = value;
            _platform.Storage.SetString(NavModeKey, value.ToString());
            if (_navigationWasActive) ApplyNavigationContent();
        }
    }

    // -- Reels ----------------------------------------------------------------

    /// <summary>
    /// Whether this platform can play videos for Reels Mode.
    /// </summary>
    public bool ReelsSupported => _reels != null;

    /// <summary>
    /// The reels folder (an opaque platform URI), or null if none was chosen.
    /// </summary>
    public string? ReelsFolder
    {
        get
        {
            var folder = _platform.Storage.GetString(ReelsFolderKey);
            return string.IsNullOrEmpty(folder) ? null : folder;
        }
    }

    /// <summary>
    /// A short name of the reels folder for display.
    /// </summary>
    public string? ReelsFolderName
    {
        get
        {
            var name = _platform.Storage.GetString(ReelsFolderNameKey);
            return string.IsNullOrEmpty(name) ? null : name;
        }
    }

    /// <summary>
    /// Chooses the reels folder; restarts the reels if they are showing.
    /// </summary>
    public void SetReelsFolder(string folderUri, string? displayName)
    {
        _options.ReelsPlatform?.RememberFolder(folderUri);
        _platform.Storage.SetString(ReelsFolderKey, folderUri);
        _platform.Storage.SetString(ReelsFolderNameKey, displayName ?? "");
        if (_navigationWasActive && _navMode == NavMode.Reels) _reels?.Start();
    }

    /// <summary>
    /// The bike's zoom buttons. Reels: scroll the videos. UI: they are events for the current page (default: none, so nothing
    /// happens). Sandwich: zoom the picture.
    /// </summary>
    private void OnZoomRequest(bool zoomIn)
    {
        if (_navMode == NavMode.Reels && _reels != null)
        {
            if (zoomIn) _reels.Next();
            else _reels.Previous();
            // The bike greys out its buttons until it hears the zoom level back, even when nothing zoomed.
            _navigation.AcknowledgeZoom();
            return;
        }

        if (_navMode == NavMode.Ui)
        {
            _navController.Handle(zoomIn ? ScreenEvent.ZoomIn : ScreenEvent.ZoomOut);
            _navigation.AcknowledgeZoom();
            return;
        }

        if (zoomIn) _navigation.ZoomIn();
        else _navigation.ZoomOut();
        _navScreen.Refresh();
    }

    /// <summary>
    /// Sends a fake Virtual Pit Board notification announcing which nav-screen button was tapped.
    /// </summary>
    private void SendNavRequestNotification(string message)
    {
        if (!_vpbEnabled) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var image = VpbImageBuilder.FromText(message, SKColors.Green);
                var sent = await SendBoardAsync(image).ConfigureAwait(false);
                if (sent) SetVpbStatus(message);
            }
            catch (Exception t)
            {
                _log.Debug($"sendNavRequestNotification failed: {t.Message}");
            }
        });
    }

    /// <summary>
    /// Lets screen actions (Media.PlayPause/Next/Previous) drive the phone's music player.
    /// </summary>
    private sealed class ScreenMediaActions : IScreenMediaActions
    {
        private readonly BikeService _owner;

        public ScreenMediaActions(BikeService owner) => _owner = owner;

        public void PlayPause() =>
            Send(_owner.MediaMetadata?.PlaybackState == YmcMediaPlaybackState.Playing
                ? CcuMusicControlRequest.Action.Pause
                : CcuMusicControlRequest.Action.Play);

        public void Next() => Send(CcuMusicControlRequest.Action.NextTrack);

        public void Previous() => Send(CcuMusicControlRequest.Action.PreviousTrack);

        private void Send(CcuMusicControlRequest.Action action)
        {
            var controller = _owner._options.MediaController;
            if (controller != null && !controller.TryTransportControl(action)) controller.DispatchMediaKey(action);
        }
    }
}
