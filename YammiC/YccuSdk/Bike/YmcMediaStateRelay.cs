using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Models;

namespace CNCO.YccuSdk.Bike;

/// <summary>
/// Forwards media-session changes from an <see cref="IYmcMediaSource"/> to the CCU
/// (the logic of the Kotlin MediaStateMonitor, minus the Android MediaSession plumbing).
/// </summary>
public class YmcMediaStateRelay
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(200);

    private readonly IYmcMediaSource _source;
    private readonly Func<YmcMessageBroker?> _getBroker;
    private readonly Logger _log;
    private CancellationTokenSource? _pendingPush;
    private YmcMediaMetaData? _latestMetadata;

    public YmcMediaStateRelay(IYmcMediaSource source, Func<YmcMessageBroker?> getBroker, Logger logger)
    {
        _source = source;
        _getBroker = getBroker;
        _log = logger.Section("MediaStateMonitor");
        _source.Changed += PushMetadata;
    }

    /// <summary>The most recent metadata snapshot (for UIs), or null before the first one.</summary>
    public YmcMediaMetaData? LatestMetadata => _latestMetadata;

    /// <summary>Raised whenever <see cref="LatestMetadata"/> changes.</summary>
    public event Action<YmcMediaMetaData?>? LatestMetadataChanged;

    public void Start()
    {
        _source.Start();
        PushMetadata();
    }

    public void Stop()
    {
        _source.Stop();
        _pendingPush?.Cancel();
        _pendingPush = null;
    }

    /// <summary>Sends the current media metadata (debounced), e.g. right after connecting.</summary>
    public void PushMetadata()
    {
        // Update the UI-facing snapshot immediately; the BLE send below is separately
        // debounced and re-reads metadata after the delay so its own behavior is unchanged.
        _latestMetadata = _source.GetMetadata();
        LatestMetadataChanged?.Invoke(_latestMetadata);

        // Media callbacks can fire several times in quick succession for a single
        // logical state change (e.g. buffering -> paused -> playing). Cancel any not-yet-sent
        // push and debounce so only the settled state goes out, instead of flooding the shared
        // BLE write queue with stale, rapidly-flapping states.
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref _pendingPush, cancellation)?.Cancel();
        var token = cancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelay, token).ConfigureAwait(false);
                var broker = _getBroker();
                if (broker == null) return;
                await broker.SendMusicMetaDataResponseAsync(_source.GetMetadata()).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer push.
            }
            catch (Exception e)
            {
                // Transient BLE errors are ignored; the next state change will retry.
                _log.Debug($"push failed: {e.Message}");
            }
        });
    }

    /// <summary>Awaitable variant for the on-connect job, which needs the push to actually be sent.</summary>
    public Task PushMetadataNowAsync()
    {
        var broker = _getBroker();
        _latestMetadata = _source.GetMetadata();
        LatestMetadataChanged?.Invoke(_latestMetadata);
        return broker == null ? Task.CompletedTask : broker.SendMusicMetaDataResponseAsync(_latestMetadata);
    }
}
