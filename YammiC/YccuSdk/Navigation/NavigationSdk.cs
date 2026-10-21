using CNCO.YccuSdk.Imaging;
using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Navigation.Protocol;
using SkiaSharp;

namespace CNCO.YccuSdk.Navigation;

/// <summary>
/// Public facade for the navigation-screen link: Activate()/Deactivate() the RFCOMM connection,
/// PushImage() to render a frame. Depends on <see cref="YmcConnectionBroker"/> only to read
/// <see cref="YmcConnectionBroker.IsBikeConnected"/> as a precondition - the RFCOMM link itself is
/// independent of the BLE connection the broker manages.
/// </summary>
public class NavigationSdk
{
    private readonly YmcConnectionBroker _controller;
    private int _maxFps;
    private readonly CancellationToken _lifetime;
    private readonly Logger _log;
    private readonly Func<INavByteChannel> _channelFactory;

    // Rate at which the last-pushed image is silently re-sent while nothing new comes in, so the
    // CCU keeps seeing a live feed instead of one frame followed by silence. Kept low (1-5fps)
    // since the content isn't actually changing.
    private int _idleResendFps;

    private readonly object _stateLock = new();
    private NavigationState _state = new NavigationState.Idle();

    // Guards entry into the connecting/active region. Flipped synchronously on the calling
    // thread, before any task is started, so two rapid Activate() calls can never both
    // pass the guard and open two RFCOMM sockets.
    private int _activeOrConnecting;

    private INavByteChannel? _channel;
    private NavigationEngine? _engine;

    // The in-flight Activate() task, so Deactivate() can cancel a connection attempt that
    // hasn't finished the handshake yet (otherwise it would complete after Deactivate() and
    // silently resurrect the channel/engine/Active state).
    private CancellationTokenSource? _jobCancellation;

    // The most recently pushed image, re-sent by the idle-resend loop below.
    private volatile byte[]? _lastImageBytes;
    private CancellationTokenSource? _idleResendCancellation;

    // Whether the bike is showing the navigation picture. Every picture, whatever mode made it, goes out through PushFrame (and
    // the idle resend), which hold back while this isn't open.
    private readonly NavImageGate _imageGate = new();

    /// <summary>Whether the bike has asked for the navigation picture (and not stopped asking). Producers can skip their work while false.</summary>
    public bool ImagesWanted => _imageGate.ImagesWanted;

    /// <summary>What the bike has said about the navigation picture on this connection (<see cref="NavImageDemand.Unknown"/> until it asks).</summary>
    public NavImageDemand ImageDemand => _imageGate.Demand;

    /// <summary>Raised when <see cref="ImageDemand"/> changes (e.g. so a screen can draw a fresh picture when the bike starts asking).</summary>
    public event Action<NavImageDemand>? ImageDemandChanged;

    /// <param name="controller">Connection broker, read for <see cref="YmcConnectionBroker.IsBikeConnected"/>.</param>
    /// <param name="channelFactory">Creates the byte channel to the CCU (e.g. Android's RFCOMM channel).</param>
    /// <param name="logger">Root logger.</param>
    /// <param name="lifetime">Cancelled when the owner shuts down.</param>
    public NavigationSdk(
        YmcConnectionBroker controller,
        Func<INavByteChannel> channelFactory,
        Logger logger,
        int maxFps = 15,
        int idleResendFps = 2,
        CancellationToken lifetime = default)
    {
        _controller = controller;
        _channelFactory = channelFactory;
        _maxFps = maxFps;
        _idleResendFps = idleResendFps;
        _lifetime = lifetime;
        _log = logger.Section("NavigationSdk");
        _imageGate.Changed += OnImageDemandChanged;
    }

    /// <summary>
    /// Changes how fast pictures go out, even while connected: the cap on pictures per second and how often the last picture is
    /// repeated when nothing new comes in.
    /// </summary>
    public void SetRates(int maxFps, int idleResendFps)
    {
        Volatile.Write(ref _maxFps, Math.Max(maxFps, 1));
        Volatile.Write(ref _idleResendFps, Math.Max(idleResendFps, 1));
        var engine = _engine;
        if (engine != null) engine.MaxFps = maxFps;
    }

    public NavigationState State
    {
        get
        {
            lock (_stateLock) return _state;
        }
    }

    /// <summary>Raised every time <see cref="State"/> changes (on whatever thread made the change).</summary>
    public event Action<NavigationState>? StateChanged;

    /// <summary>
    /// Fired when the rider taps "Go home" on the CCU. Read dynamically by the engine's
    /// receive loop, so it may be set before or after <see cref="Activate"/> is called.
    /// </summary>
    public Action? OnGoHomeRequested { get; set; }

    /// <summary>Fired when the rider taps "Go to work" on the CCU. See <see cref="OnGoHomeRequested"/>.</summary>
    public Action? OnGoToWorkRequested { get; set; }

    /// <summary>Fired when the rider taps zoom-in on the CCU's map screen. See <see cref="OnGoHomeRequested"/>.</summary>
    public Action? OnZoomInRequested { get; set; }

    /// <summary>Fired when the rider taps zoom-out on the CCU's map screen. See <see cref="OnGoHomeRequested"/>.</summary>
    public Action? OnZoomOutRequested { get; set; }

    /// <summary>Fired when the rider picks an entry on the CCU's Favorites / gas-station screen. See <see cref="OnGoHomeRequested"/>.</summary>
    public Action<NavPoiSelection>? OnPoiSelected { get; set; }

    private readonly object _poiLock = new();
    private IReadOnlyList<NavPoi> _favoritePois = Array.Empty<NavPoi>();
    private IReadOnlyList<NavPoi> _stationPois = Array.Empty<NavPoi>();

    /// <summary>
    /// Sets the entries shown on the CCU's Favorites or gas-station screen (at most 255). They are sent when the rider
    /// opens that screen, so set them before - or any time while - the link is active. An empty list is answered
    /// as "no entries", which is also what the CCU gets for a screen that was never set.
    /// </summary>
    public void SetPois(NavPoiList list, IReadOnlyList<NavPoi> pois)
    {
        lock (_poiLock)
        {
            if (list == NavPoiList.Favorites) _favoritePois = pois.ToArray();
            else _stationPois = pois.ToArray();
        }
    }

    private IReadOnlyList<NavPoi> GetPois(NavPoiList list)
    {
        lock (_poiLock) return list == NavPoiList.Favorites ? _favoritePois : _stationPois;
    }

    /// <summary>
    /// Whether the setup burst reports a saved home and office, which enables the CCU's "Go home"/"Go to work"
    /// buttons. Read at each <see cref="Activate"/>.
    /// </summary>
    public bool ReportHomeAndOffice { get; set; }

    private void SetState(NavigationState state)
    {
        lock (_stateLock) _state = state;
        StateChanged?.Invoke(state);
    }

    /// <summary>No-ops if already connecting/active. Errors if the bike isn't BLE-connected yet. Starts async.</summary>
    public void Activate()
    {
        if (Interlocked.CompareExchange(ref _activeOrConnecting, 1, 0) != 0)
        {
            _log.Debug("activate: already connecting/active, ignoring");
            return;
        }
        if (!_controller.IsBikeConnected)
        {
            Interlocked.Exchange(ref _activeOrConnecting, 0);
            SetState(new NavigationState.Error("bike not connected"));
            return;
        }
        SetState(new NavigationState.Connecting());

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        _jobCancellation = cancellation;
        var token = cancellation.Token;
        _ = Task.Run(() => ActivateAsync(token), token);
    }

    private async Task ActivateAsync(CancellationToken cancellationToken)
    {
        INavByteChannel? newChannel = null;
        try
        {
            newChannel = _channelFactory();
            var connectedChannel = newChannel;
            // No artificial timeout here: matching Pillion's Handshake.perform(), each
            // step blocks on the underlying blocking socket read and only unblocks via a
            // real I/O failure/exception (e.g. Deactivate() closing the channel).
            var handshake = await Task.Run(() =>
            {
                connectedChannel.Open();
                var h = new NavigationHandshake(connectedChannel, _log, ReportHomeAndOffice, DayNightMode, Zoom);
                h.Execute();
                return h;
            }, CancellationToken.None).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            PartNumber = handshake.PartNumber;
            DisplaySize = NavDisplaySize.FromPartNumber(handshake.PartNumber);
            _log.Debug($"display: partNumber={PartNumber ?? "?"} size={DisplaySize.Width}x{DisplaySize.Height}");

            // Publish Active before starting the engine so a fast first onFrameSent callback
            // can't be clobbered by a stale assignment made after engine.Start() returns.
            SetState(new NavigationState.Active(Fps: _maxFps, LastFrameKb: 0));
            _channel = connectedChannel;
            var engine = new NavigationEngine(
                channel: connectedChannel,
                reader: handshake.Reader,
                maxFps: Volatile.Read(ref _maxFps),
                lifetime: _lifetime,
                logger: _log,
                onFrameSent: (fps, lastFrameKb) => SetState(new NavigationState.Active(Fps: fps, LastFrameKb: lastFrameKb)),
                onGoHomeRequested: () => OnGoHomeRequested?.Invoke(),
                onGoOfficeRequested: () => OnGoToWorkRequested?.Invoke(),
                onZoomInRequested: () => OnZoomInRequested?.Invoke(),
                onZoomOutRequested: () => OnZoomOutRequested?.Invoke(),
                poiProvider: GetPois,
                onPoiSelected: selection => OnPoiSelected?.Invoke(selection),
                statusRefresh: () => NavigationHandshake.StatusRefresh(ReportHomeAndOffice, Zoom),
                onContentRequest: _imageGate.OnContentRequest);
            _engine = engine;

            // Nothing is known about the bike's screen on a new connection. Queue the notice before the loops start so a START
            // that follows straight away (which queues the real picture) replaces it rather than the other way round.
            _imageGate.Reset();
            SendGateNotice(engine);
            engine.Start();
            StartIdleResendLoop();
            _log.Debug("activate: handshake complete, streaming");
        }
        catch (OperationCanceledException)
        {
            TryClose(newChannel);
        }
        catch (Exception t)
        {
            _log.Debug($"activate: failed: {t.Message}");
            TryClose(newChannel);
            SetState(new NavigationState.Error(string.IsNullOrEmpty(t.Message) ? "connection failed" : t.Message));
        }
        finally
        {
            Interlocked.Exchange(ref _activeOrConnecting, 0);
        }
    }

    /// <summary>Stops the send loop and closes the RFCOMM socket. Safe to call when already idle.</summary>
    public void Deactivate()
    {
        _jobCancellation?.Cancel();
        _jobCancellation = null;
        _idleResendCancellation?.Cancel();
        _idleResendCancellation = null;
        StopAnimatedImage();
        _engine?.Stop();
        _engine = null;
        _imageGate.Reset();
        TryClose(_channel);
        _channel = null;
        Interlocked.Exchange(ref _activeOrConnecting, 0);
        SetState(new NavigationState.Idle());
    }

    private static void TryClose(INavByteChannel? channel)
    {
        try
        {
            channel?.Close();
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    /// <summary>
    /// Re-sends the last pushed image at the idle-resend rate for as long as the connection is active, so
    /// a static (unchanging) scene still looks like a live feed to the CCU instead of one frame
    /// followed by silence. No-ops each tick if nothing has been pushed yet.
    /// </summary>
    private void StartIdleResendLoop()
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        _idleResendCancellation = cancellation;
        var token = cancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var interval = TimeSpan.FromMilliseconds(1000L / Math.Max(Volatile.Read(ref _idleResendFps), 1));
                    await Task.Delay(interval, token).ConfigureAwait(false);
                    // Only repeat the picture when nothing new has come in: while frames are flowing a repeat is just a wasted
                    // 20-40 KB on a link that manages a few frames a second.
                    if (System.Environment.TickCount64 - Volatile.Read(ref _lastPushTicks) < interval.TotalMilliseconds) continue;
                    var image = _lastImageBytes;
                    if (image != null && _imageGate.ImagesWanted) _engine?.EnqueueImage(image);
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped.
            }
        });
    }

    /// <summary>Sets the road name shown on the CCU's navigation screen. No-ops (logged) if not Active.</summary>
    public void SetRoadName(string roadName)
    {
        var currentEngine = _engine;
        if (currentEngine == null)
        {
            _log.Debug("setRoadName: not active, dropping");
            return;
        }
        currentEngine.SendRoadName(roadName);
    }

    /// <summary>
    /// Enqueues already-encoded image bytes (480x240 JPEG) as the next frame and remembers it as the frame the
    /// idle-resend loop keeps re-sending. No-ops the send (logged) if not Active - the image is
    /// still remembered so it goes out once the connection becomes Active. Stops any animation
    /// started via <see cref="PushAnimatedImage"/> - a manual push always takes over.
    /// </summary>
    public void PushImage(byte[] image)
    {
        StopAnimatedImage();
        SetStillSource(null, 0);
        PushFrame(image);
    }

    /// <summary>
    /// Decodes <paramref name="encodedImage"/> (PNG, JPEG, ...), letterboxes it onto the CCU's expected 480x240
    /// navigation canvas (the dash renders 480x240 or 480x236 - see Pillion's PROTOCOL.md "Image channel"; any
    /// other resolution is not guaranteed to display), and re-compresses to JPEG at <paramref name="quality"/> (0-100) before pushing.
    /// </summary>
    public void PushImage(byte[] encodedImage, int quality)
    {
        using var source = SKBitmap.Decode(encodedImage);
        if (source == null)
        {
            _log.Debug("pushImage: failed to decode image");
            return;
        }
        PushImage(source, quality);
    }

    /// <summary>Same as <see cref="PushImage(byte[], int)"/> for an already-decoded bitmap.</summary>
    public void PushImage(SKBitmap source, int quality = 90)
    {
        StopAnimatedImage();
        // Keep a copy so the image can be re-rendered when the zoom changes.
        SetStillSource(source.Copy(), quality);
        RenderStill();
    }

    // -- Display ---------------------------------------------------------------

    /// <summary>The CCU part number from the last handshake (null before one, or if it couldn't be read).</summary>
    public string? PartNumber { get; private set; }

    private NavDisplaySize _displaySize = NavDisplaySize.Default;

    /// <summary>
    /// Size images must have for this CCU, derived from its part number at each handshake. Until the first
    /// handshake it is <see cref="NavDisplaySize.Default"/>.
    /// </summary>
    public NavDisplaySize DisplaySize
    {
        get { lock (_zoomLock) return _displaySize; }
        private set { lock (_zoomLock) _displaySize = value; }
    }

    private volatile NavDayNightMode _dayNightMode = NavDayNightMode.Day;

    /// <summary>Current day/night mode. Sent to the CCU in the setup burst, and again by <see cref="SetDayNightMode"/>.</summary>
    public NavDayNightMode DayNightMode => _dayNightMode;

    /// <summary>Remembers the mode and, if the link is active, tells the CCU (DAY_NIGHT_MODE_UPDATE).</summary>
    public void SetDayNightMode(NavDayNightMode mode)
    {
        if (_dayNightMode == mode) return;
        _dayNightMode = mode;
        _log.Debug($"dayNight: {mode}");
        _engine?.SendDayNight((byte)mode);
    }

    // -- Zoom ------------------------------------------------------------------

    private const float ZoomStep = 1.5f;
    private const float MinZoom = 0.4f;
    private const float MaxZoom = 6f;

    private readonly object _zoomLock = new();
    private float _zoom = 1f;
    private SKBitmap? _stillSource;
    private int _stillQuality;

    /// <summary>Current map zoom (1 = fitted to the screen). Applied to every image pushed through the decoding overloads and to animations.</summary>
    public float Zoom
    {
        get
        {
            lock (_zoomLock) return _zoom;
        }
    }

    /// <summary>Magnifies the displayed image one step (1.5x, up to 6x) and re-sends it. Called for the CCU's zoom-in button.</summary>
    public void ZoomIn() => SetZoom(Zoom * ZoomStep);

    /// <summary>Shrinks the displayed image one step (down to 0.4x) and re-sends it. Called for the CCU's zoom-out button.</summary>
    public void ZoomOut() => SetZoom(Zoom / ZoomStep);

    /// <summary>Returns to the fitted view.</summary>
    public void ResetZoom() => SetZoom(1f);

    /// <summary>Re-sends the current zoom level without changing it. The CCU greys out its zoom buttons until it hears one.</summary>
    public void AcknowledgeZoom() => _engine?.SendZoomLevel(NaviLiteZoom.Payload(Zoom));

    private void SetZoom(float zoom)
    {
        lock (_zoomLock) _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _log.Debug($"zoom: {Zoom:0.##}x");
        // The CCU disables its zoom buttons until told the new level.
        _engine?.SendZoomLevel(NaviLiteZoom.Payload(Zoom));
        // Animations pick the new zoom up on their next frame; a still has to be re-rendered.
        RenderStill();
    }

    private void SetStillSource(SKBitmap? source, int quality)
    {
        SKBitmap? old;
        lock (_zoomLock)
        {
            old = _stillSource;
            _stillSource = source;
            _stillQuality = quality;
        }
        old?.Dispose();
    }

    private void RenderStill()
    {
        byte[]? jpeg = null;
        lock (_zoomLock)
        {
            if (_stillSource != null)
            {
                using var canvasBitmap = YmcImaging.Letterbox(_stillSource, DisplaySize.Width, DisplaySize.Height, _zoom);
                jpeg = YmcImaging.EncodeJpeg(canvasBitmap, _stillQuality);
            }
        }
        if (jpeg != null) PushFrame(jpeg);
    }

    /// <summary>
    /// Decodes <paramref name="encodedAnimation"/> as an animated GIF or WebP and continuously pushes its frames,
    /// timed by the delays baked into the source file, looping forever until replaced by another
    /// <see cref="PushImage(byte[])"/>/<see cref="PushAnimatedImage"/> call or <see cref="Deactivate"/>. Each frame is
    /// letterboxed onto the navigation canvas and JPEG-compressed at <paramref name="quality"/> the same way as a still image. The
    /// underlying send loop already paces frames to maxFps and conflates a fast-arriving
    /// backlog to just the latest, so a source with faster-than-maxFps timing is naturally
    /// throttled without dropping the animation's own pacing.
    /// If the image isn't actually animated, a single frame is pushed instead.
    /// </summary>
    public void PushAnimatedImage(byte[] encodedAnimation, int quality = 90)
    {
        StopAnimatedImage();
        SetStillSource(null, 0);
        var player = new NavigationAnimatedImagePlayer(_log, LetterboxToNavigationCanvas, PushFrame);
        _animatedImagePlayer = player;
        player.Start(encodedAnimation, quality);
    }

    /// <summary>Stops any animation started via <see cref="PushAnimatedImage"/>. No-op if none is playing.</summary>
    private void StopAnimatedImage()
    {
        _animatedImagePlayer?.Stop();
        _animatedImagePlayer = null;
    }

    private SKBitmap LetterboxToNavigationCanvas(SKBitmap source) =>
        YmcImaging.Letterbox(source, DisplaySize.Width, DisplaySize.Height, Zoom);


    // Set while an animated image (PushAnimatedImage) is playing; stopped whenever a static
    // frame is pushed or the connection is deactivated.
    private NavigationAnimatedImagePlayer? _animatedImagePlayer;

    /// <summary>Same as <see cref="PushImage(byte[])"/> but doesn't touch the animation player - used by its own frames.</summary>
    private long _lastPushTicks;

    private void PushFrame(byte[] image)
    {
        _lastImageBytes = image;
        Volatile.Write(ref _lastPushTicks, System.Environment.TickCount64);
        var currentEngine = _engine;
        if (currentEngine == null)
        {
            _log.Debug("pushImage: not active, dropping");
            return;
        }

        // The one gate for every mode (screens, reels, animations): the picture is remembered above and goes out when the bike asks.
        if (!_imageGate.ImagesWanted) return;
        currentEngine.EnqueueImage(image);
    }

    /// <summary>The gate changed: say so on the bike, or (when it opens) send the last picture again so it isn't left on the notice.</summary>
    private void OnImageDemandChanged(NavImageDemand demand)
    {
        var currentEngine = _engine;
        if (currentEngine != null)
        {
            if (demand == NavImageDemand.Wanted)
            {
                var last = _lastImageBytes;
                if (last != null) currentEngine.EnqueueImage(last);
            }
            else
            {
                SendGateNotice(currentEngine);
            }
        }
        _log.Debug($"navigation picture: {demand}");
        ImageDemandChanged?.Invoke(demand);
    }

    /// <summary>
    /// Queues the "Rendering paused" picture for the current state, around the gate and without remembering it as content. It
    /// goes out once per state change, so if the bike's START/STOP signals aren't what they seem the notice shows it.
    /// </summary>
    private void SendGateNotice(NavigationEngine engine)
    {
        var demand = _imageGate.Demand;
        var text = NavImageGate.StatusText(demand);
        if (text == null) return;
        engine.EnqueueImage(NavigationStatusImage.Render(text, DisplaySize));
    }
}
