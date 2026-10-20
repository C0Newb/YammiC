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
    private readonly Func<ScreenLayout> _layout;
    private readonly Func<ScreenContext> _context;
    private readonly Func<(int Width, int Height)> _size;
    private readonly Func<bool> _canSend;
    private readonly Func<byte[], Task<bool>> _send;
    private readonly TimeSpan _tick;
    private readonly TimeSpan _minInterval;
    private readonly int _quality;
    private readonly Func<bool>? _wantsFastTick;
    private readonly TimeSpan _fastTick;
    private readonly Logger _log;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly object _gate = new();

    private CancellationTokenSource? _loop;
    private string? _lastSignature;
    private DateTime _lastAttempt = DateTime.MinValue;

    /// <param name="name">For the log.</param>
    /// <param name="layout">The layout to show (read each tick, so a replaced layout is picked up).</param>
    /// <param name="context">The data to show (time, music, theme).</param>
    /// <param name="size">Pixel size to render at.</param>
    /// <param name="canSend">False while the display can't take a picture (not connected, disabled): the tick does nothing.</param>
    /// <param name="send">Delivers the JPEG; returns false if it didn't go through.</param>
    /// <param name="tick">How often to check for changes.</param>
    /// <param name="minInterval">Never send more often than this.</param>
    /// <param name="wantsFastTick">When it returns true (e.g. the layout shows live bike data) the loop checks every <c>fastTick</c> instead of <c>tick</c>.</param>
    public ScreenPresenter(
        string name,
        Func<ScreenLayout> layout,
        Func<ScreenContext> context,
        Func<(int Width, int Height)> size,
        Func<bool> canSend,
        Func<byte[], Task<bool>> send,
        TimeSpan tick,
        TimeSpan minInterval,
        int quality,
        Logger logger,
        Func<bool>? wantsFastTick = null,
        TimeSpan? fastTick = null)
    {
        _wantsFastTick = wantsFastTick;
        _fastTick = fastTick ?? tick;
        _layout = layout;
        _context = context;
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
                    await Task.Delay(_wantsFastTick?.Invoke() == true && _fastTick < _tick ? _fastTick : _tick, token).ConfigureAwait(false);
                    await RefreshAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped.
            }
        });
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
        if (!await _busy.WaitAsync(0).ConfigureAwait(false)) return; // a send is still in flight
        try
        {
            var layout = _layout();
            var context = _context();
            var (width, height) = _size();
            var signature = $"{width}x{height}|{ScreenRenderer.Signature(layout, context)}";

            lock (_gate)
            {
                if (signature == _lastSignature) return;
                if (DateTime.UtcNow - _lastAttempt < _minInterval) return;
                _lastAttempt = DateTime.UtcNow;
            }

            byte[] jpeg;
            using (var bitmap = ScreenRenderer.Render(layout, context, width, height))
            {
                jpeg = YmcImaging.EncodeJpeg(bitmap, _quality);
            }

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
}
