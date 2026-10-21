using System.Diagnostics;
using System.Threading.Channels;
using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Navigation.Protocol;

namespace CNCO.YccuSdk.Navigation;

/// <summary>
/// Owns the IMAGE-frame send loop for one active navigation connection.
///
/// Fed via <see cref="EnqueueImage"/> - a conflated queue (capacity 1, oldest dropped), so a burst of pushes only keeps the
/// latest. The sender loop throttles to maxFps and gates outstanding sends behind a
/// sliding-window semaphore (<c>MaxInFlight</c> permits); a separate loop drains IMAGE_ACK frames
/// off the reader and releases the window. Does not know how frames are produced or how the
/// underlying socket works - depends only on <see cref="INavByteChannel"/>/<see cref="NavFrameReader"/>.
/// </summary>
public class NavigationEngine
{
    private const int MaxInFlight = 2;
    private const int AckTimeoutMs = 1000;

    // CUR_ROAD_NAME_UPDATE payload is a raw string with no length prefix, max 64 bytes
    // per Pillion's PROTOCOL.md.
    private const int MaxRoadNameBytes = 64;

    // Per Pillion's MirrorEngine: imageType 3 = "NAVIGATION_EXPANDED". The CCU keys its
    // rendering mode off this byte - sending 0 here is a different (and wrong) image type.
    private const byte ImageTypeNavigation = 3;


    private readonly INavByteChannel _channel;
    private readonly NavFrameReader _reader;
    private int _maxFps;
    private readonly CancellationToken _lifetime;
    private readonly Logger _log;
    private readonly Action<int, int> _onFrameSent;
    private readonly Action _onGoHomeRequested;
    private readonly Action _onGoOfficeRequested;
    private readonly Action _onZoomInRequested;
    private readonly Action _onZoomOutRequested;
    private readonly Func<NavPoiList, IReadOnlyList<NavPoi>> _poiProvider;
    private readonly Action<NavPoiSelection> _onPoiSelected;
    private readonly Func<IReadOnlyList<(int ServiceType, int PayloadDataType, byte[] Payload)>> _statusRefresh;
    private readonly Action<int, bool> _onContentRequest;

    private readonly Channel<byte[]> _frameQueue = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly SemaphoreSlim _inFlight = new(MaxInFlight, MaxInFlight);

    // Guards channel.Write() so the image sender loop and one-off status sends (e.g.
    // SendRoadName) - which run on different tasks - can never interleave bytes on the
    // same socket.
    private readonly SemaphoreSlim _writeMutex = new(1, 1);

    private int _sequenceNumber = 1; // matches Pillion's MirrorEngine, which starts seq at 1
    private CancellationTokenSource? _loopsCancellation;

    public NavigationEngine(
        INavByteChannel channel,
        NavFrameReader reader,
        int maxFps,
        CancellationToken lifetime,
        Logger logger,
        Action<int, int>? onFrameSent = null,
        Action? onGoHomeRequested = null,
        Action? onGoOfficeRequested = null,
        Action? onZoomInRequested = null,
        Action? onZoomOutRequested = null,
        Func<NavPoiList, IReadOnlyList<NavPoi>>? poiProvider = null,
        Action<NavPoiSelection>? onPoiSelected = null,
        Func<IReadOnlyList<(int ServiceType, int PayloadDataType, byte[] Payload)>>? statusRefresh = null,
        Action<int, bool>? onContentRequest = null)
    {
        _channel = channel;
        _reader = reader;
        _maxFps = Math.Max(maxFps, 1);
        _lifetime = lifetime;
        _log = logger.Section("NavigationEngine");
        _onFrameSent = onFrameSent ?? ((_, _) => { });
        _onGoHomeRequested = onGoHomeRequested ?? (() => { });
        _onGoOfficeRequested = onGoOfficeRequested ?? (() => { });
        _onZoomInRequested = onZoomInRequested ?? (() => { });
        _onZoomOutRequested = onZoomOutRequested ?? (() => { });
        _poiProvider = poiProvider ?? (_ => Array.Empty<NavPoi>());
        _onPoiSelected = onPoiSelected ?? (_ => { });
        _statusRefresh = statusRefresh ?? (() => Array.Empty<(int, int, byte[])>());
        _onContentRequest = onContentRequest ?? ((_, _) => { });
    }

    /// <summary>Starts the sender loop and the frame-receive loop, both on the thread pool (blocking I/O).</summary>
    public void Start()
    {
        _log.Debug("starting sender + receive loops");
        _loopsCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        var token = _loopsCancellation.Token;
        _ = Task.Run(() => ReceiveLoop(token));
        _ = Task.Run(() => SenderLoopAsync(token));
    }

    /// <summary>Cancels both loops and closes the frame queue. Does not close the underlying channel.</summary>
    public void Stop()
    {
        _log.Debug("stopping sender + receive loops");
        _loopsCancellation?.Cancel();
        _frameQueue.Writer.TryComplete();
    }

    /// <summary>The cap on pictures sent per second. Can be changed while running; the sender loop picks it up on the next picture.</summary>
    public int MaxFps
    {
        get => Volatile.Read(ref _maxFps);
        set => Volatile.Write(ref _maxFps, Math.Max(value, 1));
    }

    /// <summary>Enqueues raw image bytes (e.g. JPEG) as the next frame to send.</summary>
    public bool EnqueueImage(byte[] image) => _frameQueue.Writer.TryWrite(BuildNextPayload(image));

    /// <summary>
    /// Sends the current road name (CUR_ROAD_NAME_UPDATE, service 3) - a raw UTF-8 string with
    /// no length prefix, truncated to MaxRoadNameBytes per the reference SDK. Unlike IMAGE
    /// frames this isn't ACK'd or window-gated; it's a fire-and-forget status update, same as the
    /// handshake's setup burst.
    /// </summary>
    public void SendRoadName(string roadName)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(roadName);
        if (payload.Length > MaxRoadNameBytes) payload = payload[..MaxRoadNameBytes];
        var frame = NaviLiteCodec.Build(
            frameType: NaviLiteConstants.FrameTypePhone,
            serviceType: NaviLiteServiceType.Road,
            payloadDataType: NaviLiteConstants.PdtPointer,
            payload: payload);
        var sentPayload = payload;
        _ = Task.Run(async () =>
        {
            try
            {
                await WriteAsync(frame).ConfigureAwait(false);
                _log.Debug($"sent ROAD update: \"{roadName}\" ({sentPayload.Length}B)");
            }
            catch (Exception t)
            {
                _log.Debug($"sendRoadName: failed to send: {t.Message}");
            }
        });
    }

    /// <summary>Sends MAP_ZOOM_LEVEL_UPDATE (service 14, see <see cref="NaviLiteZoom"/>). Fire-and-forget.</summary>
    public void SendZoomLevel(byte[] payload)
    {
        var frame = NaviLiteCodec.Build(
            frameType: NaviLiteConstants.FrameTypePhone,
            serviceType: NaviLiteServiceType.Zoom,
            payloadDataType: NaviLiteConstants.PdtPointer,
            payload: payload);
        _ = Task.Run(async () =>
        {
            try
            {
                await WriteAsync(frame).ConfigureAwait(false);
                _log.Debug($"sent ZOOM_LEVEL update: {ToHexPreview(payload)}");
            }
            catch (Exception t)
            {
                _log.Debug($"sendZoomLevel: failed to send: {t.Message}");
            }
        });
    }

    /// <summary>Sends DAY_NIGHT_MODE_UPDATE (service 31): `[mode][0x00]`, same shape as the setup burst's. Fire-and-forget.</summary>
    public void SendDayNight(byte mode)
    {
        var frame = NaviLiteCodec.Build(
            frameType: NaviLiteConstants.FrameTypePhone,
            serviceType: NaviLiteServiceType.DayNight,
            payloadDataType: NaviLiteConstants.PdtValue,
            payload: new byte[] { mode, 0 });
        _ = Task.Run(async () =>
        {
            try
            {
                await WriteAsync(frame).ConfigureAwait(false);
                _log.Debug($"sent DAY_NIGHT update: {mode}");
            }
            catch (Exception t)
            {
                _log.Debug($"sendDayNight: failed to send: {t.Message}");
            }
        });
    }

    private byte[] BuildNextPayload(byte[] image)
    {
        var seq = (short)((Interlocked.Increment(ref _sequenceNumber) - 1) & 0xFFFF);
        return BuildImagePayload(ImageTypeNavigation, seq, image);
    }

    private async Task WriteAsync(byte[] frame)
    {
        await _writeMutex.WaitAsync().ConfigureAwait(false);
        try
        {
            _channel.Write(frame);
        }
        finally
        {
            _writeMutex.Release();
        }
    }

    // Send time of each frame still waiting for its ack, so the ack round trip can be measured.
    private readonly System.Collections.Concurrent.ConcurrentQueue<long> _sentAt = new();
    private readonly object _rttLock = new();
    private double _rttSum;
    private int _rttCount;
    private double _rttMax;

    private void RecordAck()
    {
        if (!_sentAt.TryDequeue(out var sent)) return;
        var rtt = NowMs() - sent;
        lock (_rttLock)
        {
            _rttSum += rtt;
            _rttCount++;
            _rttMax = Math.Max(_rttMax, rtt);
        }
    }

    private double AckRttAverageMs()
    {
        lock (_rttLock) return _rttCount == 0 ? 0 : _rttSum / _rttCount;
    }

    private double AckRttMaxMs()
    {
        lock (_rttLock)
        {
            var max = _rttMax;
            _rttSum = 0;
            _rttCount = 0;
            _rttMax = 0;
            return max;
        }
    }

    private async Task SenderLoopAsync(CancellationToken cancellationToken)
    {
        var lastSentAtMs = 0L;
        var statsSince = NowMs();
        var statFrames = 0;
        var statBytes = 0L;
        var statStalls = 0;
        try
        {
            while (true)
            {
                // Wait for the pacing interval and a free slot in the ack window FIRST, and only then take a frame off the queue:
                // the queue keeps just the newest picture, so what is sent is as fresh as possible. (Taking the frame first meant it
                // sat in hand, getting older, while the window was full.)
                var sinceLastMs = NowMs() - lastSentAtMs;
                var minIntervalMs = 1000L / MaxFps;
                if (lastSentAtMs != 0L && sinceLastMs < minIntervalMs)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(minIntervalMs - sinceLastMs), cancellationToken).ConfigureAwait(false);
                }

                var acquired = await _inFlight.WaitAsync(AckTimeoutMs, cancellationToken).ConfigureAwait(false);
                if (!acquired)
                {
                    statStalls++;
                    _log.Debug("window stalled (lost ack?) - sending anyway");
                }

                if (!await _frameQueue.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    _log.Debug("sender loop: frame queue closed, exiting");
                    return;
                }
                if (!_frameQueue.Reader.TryRead(out var payload))
                {
                    // Something else took it between the check and the read; give the slot back and go round again.
                    if (acquired && _inFlight.CurrentCount < MaxInFlight) _inFlight.Release();
                    continue;
                }

                var frame = NaviLiteCodec.Build(
                    frameType: NaviLiteConstants.FrameTypePhone,
                    serviceType: NaviLiteServiceType.Image,
                    payloadDataType: NaviLiteConstants.PdtPointer,
                    payload: payload);
                await WriteAsync(frame).ConfigureAwait(false);
                lastSentAtMs = NowMs();
                _sentAt.Enqueue(lastSentAtMs);
                while (_sentAt.Count > 8) _sentAt.TryDequeue(out _); // acks that never came
                _onFrameSent(MaxFps, frame.Length / 1024);

                statFrames++;
                statBytes += frame.Length;
                if (lastSentAtMs - statsSince >= 5000)
                {
                    var seconds = (lastSentAtMs - statsSince) / 1000.0;
                    _log.Debug(
                        $"image stats: {statFrames / seconds:0.0} fps, avg {statBytes / Math.Max(statFrames, 1) / 1024} KB, " +
                        $"{statBytes / seconds / 1024:0} KB/s, stalls={statStalls}, ack rtt avg={AckRttAverageMs():0} max={AckRttMaxMs():0} ms");
                    statsSince = lastSentAtMs;
                    statFrames = 0;
                    statBytes = 0;
                    statStalls = 0;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
        catch (Exception t)
        {
            _log.Debug($"sender loop ended: {t.Message}");
        }
    }

    /// <summary>
    /// Drains every incoming frame on the post-handshake link: IMAGE_ACK releases the sliding
    /// window, GO_HOME/GO_OFFICE requests are the CCU's "Go home"/"Go to work" button presses.
    /// Every frame is logged - including ones we don't otherwise act on - since several of the
    /// CCU's request service-type IDs are "inferred" per the reference protocol docs and may not
    /// match this exact hardware/firmware; this is the only way to find out what actually arrives.
    /// </summary>
    private void ReceiveLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = _reader.Next();
                switch (frame.ServiceType)
                {
                    case NaviLiteServiceType.ImageAck:
                        RecordAck();
                        if (_inFlight.CurrentCount < MaxInFlight) _inFlight.Release();
                        break;

                    case NaviLiteServiceType.ZoomInRequest:
                        _log.Debug("received MAP_ZOOM_IN_REQUEST");
                        _onZoomInRequested();
                        break;

                    case NaviLiteServiceType.ZoomOutRequest:
                        _log.Debug("received MAP_ZOOM_OUT_REQUEST");
                        _onZoomOutRequested();
                        break;

                    case NaviLiteServiceType.GoHomeRequest:
                        _log.Debug($"received GO_HOME_REQUEST payload={ToHexPreview(frame.Payload)}");
                        _onGoHomeRequested();
                        break;

                    case NaviLiteServiceType.GoOfficeRequest:
                        _log.Debug($"received GO_OFFICE_REQUEST payload={ToHexPreview(frame.Payload)}");
                        _onGoOfficeRequested();
                        break;

                    case NaviLiteServiceType.StartContentUpdateRequest:
                        {
                            var contentType = frame.Payload.Length > 0 ? frame.Payload[0] : -1;
                            _log.Debug($"received START_CONTENT_UPDATE_REQUEST contentType={contentType}");
                            _onContentRequest(contentType, true);
                            if (contentType == NaviLiteContentType.NaviImage) SendStatusRefresh();
                            if (contentType == NaviLiteContentType.FavoriteList)
                                SendPoiList(NavPoiList.Favorites, NaviLiteServiceType.FavPoiListUpdate, NaviLiteServiceType.FavPoiListDataUpdate);
                            else if (contentType == NaviLiteContentType.StationsList)
                                SendPoiList(NavPoiList.Stations, NaviLiteServiceType.GasPoiListUpdate, NaviLiteServiceType.GasPoiListDataUpdate);
                            break;
                        }

                    case NaviLiteServiceType.StartRouteRequest:
                        HandleStartRoute(frame.Payload);
                        break;

                    case NaviLiteServiceType.StopContentUpdateRequest:
                        {
                            var stoppedType = frame.Payload.Length > 0 ? frame.Payload[0] : -1;
                            _log.Debug($"received STOP_CONTENT_UPDATE_REQUEST contentType={stoppedType}");
                            _onContentRequest(stoppedType, false);
                        }
                        break;

                    default:
                        _log.Debug(
                            $"received unhandled frame: serviceType={frame.ServiceType} " +
                            $"payload={ToHexPreview(frame.Payload)}");
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
        catch (Exception t)
        {
            // reader.Next() throws once the channel is closed (Stop()/Deactivate()) - expected exit.
            _log.Debug($"receive loop ended: {t.Message}");
        }
    }

    /// <summary>Re-sends the status frames (see <see cref="NavigationHandshake.StatusRefresh"/>) when the rider opens the navigation screen.</summary>
    private void SendStatusRefresh()
    {
        var frames = _statusRefresh();
        if (frames.Count == 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var (serviceType, payloadDataType, payload) in frames)
                {
                    await WriteAsync(NaviLiteCodec.Build(NaviLiteConstants.FrameTypePhone, serviceType, payloadDataType, payload)).ConfigureAwait(false);
                    _log.Debug($"status refresh: sent serviceType={serviceType} payload={ToHexPreview(payload)}");
                }
            }
            catch (Exception t)
            {
                _log.Debug($"status refresh failed: {t.Message}");
            }
        });
    }

    /// <summary>
    /// Answers a Favorites / gas-station screen opening: a list-size message, then one data frame per entry. The reply
    /// has to be sent even when the list is empty, or the CCU sits on "loading" with no timeout. Runs off the
    /// receive loop so the socket writes can't stall frame reading.
    /// </summary>
    private void SendPoiList(NavPoiList list, int listServiceType, int dataServiceType)
    {
        var pois = _poiProvider(list);
        var count = Math.Min(pois.Count, 255);
        _ = Task.Run(async () =>
        {
            try
            {
                await WriteAsync(NaviLiteCodec.Build(
                    NaviLiteConstants.FrameTypePhone,
                    listServiceType,
                    NaviLiteConstants.PdtValue,
                    new byte[] { (byte)count, 0 })).ConfigureAwait(false);

                for (var i = 0; i < count; i++)
                {
                    var poi = pois[i];
                    var entry = new NaviLitePoiEntry(i, (byte)poi.Direction, poi.Distance, poi.Name, poi.Road);
                    await WriteAsync(NaviLiteCodec.Build(
                        NaviLiteConstants.FrameTypePhone,
                        dataServiceType,
                        NaviLiteConstants.PdtPointer,
                        entry.ToPayload())).ConfigureAwait(false);
                }
                _log.Debug($"sent {list} POI list ({count} entries)");
            }
            catch (Exception t)
            {
                _log.Debug($"failed to send {list} POI list: {t.Message}");
            }
        });
    }

    /// <summary>
    /// START_ROUTE_REQUEST: the rider picked a list entry. Field order is `itemIndex u16, listIndex u16, routeOption u8`
    /// per the protocol notes (StreetCross dispatch); the raw bytes are passed along because it hasn't been confirmed
    /// against this CCU.
    /// </summary>
    private void HandleStartRoute(byte[] payload)
    {
        _log.Debug($"received START_ROUTE_REQUEST payload={ToHexPreview(payload)}");
        if (payload.Length < 5) return;

        var itemIndex = payload[0] | (payload[1] << 8);
        var listIndex = payload[2] | (payload[3] << 8);
        var routeOption = payload[4];
        var list = listIndex == (int)NavPoiList.Stations ? NavPoiList.Stations : NavPoiList.Favorites;
        var pois = _poiProvider(list);
        var poi = itemIndex < pois.Count ? pois[itemIndex] : null;
        _onPoiSelected(new NavPoiSelection(list, itemIndex, routeOption, poi, payload));
    }

    /// <summary>Hex dump capped at 32 bytes so a large/malformed payload can't flood the log.</summary>
    private static string ToHexPreview(byte[] bytes)
    {
        var hex = string.Join(" ", bytes.Take(32).Select(b => b.ToString("x2")));
        return bytes.Length > 32 ? $"{hex} ... ({bytes.Length}B total)" : $"{hex} ({bytes.Length}B)";
    }

    private static long NowMs() => Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;

    /// <summary>
    /// Builds the payload for an image frame to send to the bike.
    /// Layout: [imageType][seqLo][seqHi][jpeg]
    /// </summary>
    public static byte[] BuildImagePayload(byte imageType, short seq, byte[] jpeg)
    {
        var payload = new byte[3 + jpeg.Length];
        payload[0] = imageType;
        payload[1] = (byte)(seq & 0xff);
        payload[2] = (byte)((seq >> 8) & 0xff);
        jpeg.CopyTo(payload, 3);
        return payload;
    }
}
