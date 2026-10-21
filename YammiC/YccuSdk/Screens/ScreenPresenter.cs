using CNCO.YccuSdk.Imaging;
using CNCO.YccuSdk.Logging;

namespace CNCO.YccuSdk.Screens;

/// <summary>
/// Keeps one of the bike's displays showing a <see cref="ScreenLayout"/>: on a timer it works out what the screen would
/// look like now and, only if that differs from what was last sent (and <c>minInterval</c> has passed), renders it,
/// encodes it as JPEG and hands it to <c>send</c>. A failed send is retried on a later tick.
/// </summary>
public sealed class ScreenPresenter
{
    private readonly ScreenTarget _target;
    private readonly IScreenRenderer? _renderer;
    private readonly Func<ScreenPage> _page;
    private readonly Func<ScreenSnapshot> _snapshot;
    private readonly Func<(int Width, int Height)> _size;
    private readonly Func<bool> _canSend;
    private readonly Func<byte[], Task<bool>> _send;
    private readonly Func<TimeSpan> _tick;
    private readonly TimeSpan _minInterval;
    private readonly Func<int> _quality;
    private readonly Func<bool>? _wantsFastTick;
    private readonly Func<TimeSpan> _fastTick;
    private readonly Logger _log;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly object _gate = new();

    private CancellationTokenSource? _loop;
    private string? _lastSignature;
    private DateTime _lastAttempt = DateTime.MinValue;

    /// <param name="name">For the log.</param>
    /// <param name="target">Which display this is (sets the design size the pages are drawn at).</param>
    /// <param name="renderer">Draws the page; null where there isn't one (nothing is sent).</param>
    /// <param name="page">The page to show (read each tick, so a page change or an edited page is picked up).</param>
    /// <param name="snapshot">The data to show (time, music, theme, page position).</param>
    /// <param name="size">Pixel size to render at.</param>
    /// <param name="canSend">False while the display can't take a picture (not connected, disabled): the tick does nothing.</param>
    /// <param name="send">Delivers the JPEG; returns false if it didn't go through.</param>
    /// <param name="tick">How often to check for changes.</param>
    /// <param name="minInterval">Never send more often than this.</param>
    /// <param name="wantsFastTick">When it returns true (e.g. the layout shows live bike data) the loop checks every <c>fastTick</c> instead of <c>tick</c>.</param>
    public ScreenPresenter(
        string name,
        ScreenTarget target,
        IScreenRenderer? renderer,
        Func<ScreenPage> page,
        Func<ScreenSnapshot> snapshot,
        Func<(int Width, int Height)> size,
        Func<bool> canSend,
        Func<byte[], Task<bool>> send,
        Func<TimeSpan> tick,
        TimeSpan minInterval,
        Func<int> quality,
        Logger logger,
        Func<bool>? wantsFastTick = null,
        Func<TimeSpan>? fastTick = null)
    {
        _wantsFastTick = wantsFastTick;
        _fastTick = fastTick ?? tick;
        _target = target;
        _renderer = renderer;
        _page = page;
        _snapshot = snapshot;
        _size = size;
        _canSend = canSend;
        _send = send;
        _tick = tick;
        _minInterval = minInterval;
        _quality = quality;
        _log = logger.Section(name);
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _loop != null;
        }
    }

    /// <summary>Starts (or restarts) the loop and draws immediately.</summary>
    public void Start()
    {
        Stop();
        var cancellation = new CancellationTokenSource();
        lock (_gate)
        {
            _loop = cancellation;
            _lastSignature = null;
            _lastAttempt = DateTime.MinValue;
        }
        Refresh();
        var token = cancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await Task.Delay(NextDelay(), token).ConfigureAwait(false);
                    await RefreshAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped.
            }
        });
    }

    private TimeSpan NextDelay()
    {
        var tick = _tick();
        var fast = _fastTick();
        return _wantsFastTick?.Invoke() == true && fast < tick ? fast : tick;
    }

    public void Stop()
    {
        CancellationTokenSource? loop;
        lock (_gate)
        {
            loop = _loop;
            _loop = null;
        }
        loop?.Cancel();
    }

    /// <summary>Forgets what was last sent so the screen is redrawn (e.g. after its layout was edited).</summary>
    public void Invalidate()
    {
        lock (_gate) _lastSignature = null;
        Refresh();
    }

    /// <summary>Checks now instead of waiting for the next tick.</summary>
    public void Refresh() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (!IsRunning || !_canSend()) return;
        if (_renderer == null)
        {
            LogOnce("there is no screen renderer on this platform");
            return;
        }
        if (!await _busy.WaitAsync(0).ConfigureAwait(false)) return; // a send is still in flight
        try
        {
            var page = _page();
            var snapshot = _snapshot();
            var (width, height) = _size();
            var signature = $"{width}x{height}|{page.Id}|{page.Xaml.GetHashCode()}|{snapshot.Signature(page.UsesSeconds)}";

            lock (_gate)
            {
                if (signature == _lastSignature) return;
                if (DateTime.UtcNow - _lastAttempt < _minInterval) return;
                _lastAttempt = DateTime.UtcNow;
            }

            using var bitmap = await _renderer.RenderAsync(_target, page, snapshot, width, height).ConfigureAwait(false);
            if (bitmap == null)
            {
                // Keep showing the last good picture; don't retry until the page or the data changes.
                LogOnce($"page \"{page.Id}\" can't be drawn: {_renderer.LastError}");
                lock (_gate) _lastSignature = signature;
                return;
            }

            var jpeg = YmcImaging.EncodeJpeg(bitmap, _quality());
            var sent = await _send(jpeg).ConfigureAwait(false);
            if (sent)
            {
                lock (_gate) _lastSignature = signature;
            }
            else
            {
                _log.Debug("send failed, will retry");
            }
        }
        catch (Exception t)
        {
            _log.Debug($"refresh failed: {t.Message}");
        }
        finally
        {
            _busy.Release();
        }
    }

    private string? _lastLogged;

    private void LogOnce(string message)
    {
        if (message == _lastLogged) return;
        _lastLogged = message;
        _log.Debug(message);
    }
}
