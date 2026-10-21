using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Packets;

namespace CNCO.YccuSdk.Can;

/// <summary>
/// One CAN ID's latest reading, for the CAN page's raw table.
/// </summary>
/// <param name="Data">Latest bytes.</param>
/// <param name="ReceivedAt">When the latest reading arrived.</param>
/// <param name="ChangedAt">When the bytes last differed from the previous reading.</param>
public sealed record CanRawReading(int Id, byte[] Data, DateTime ReceivedAt, DateTime ChangedAt);

/// <summary>
/// Listens on the bike-to-phone DDT v3 characteristic for CAN responses and keeps the latest reading per CAN ID.
/// It only ever sees the IDs that were requested (<see cref="CanSignals.KnownIds"/>).
/// </summary>
[SuppressUiLog(YmcCommandCode.CanResponse)]
public sealed class CanTelemetry
{
    private readonly Logger _log;
    private readonly object _gate = new();
    private readonly Dictionary<int, CanRawReading> _latest = new();
    private readonly Dictionary<Guid, CanDdtV3Assembler> _assemblers = new();
    private bool _loggedFirst;

    public CanTelemetry(Logger logger) => _log = logger.Section("Can");

    /// <summary>
    /// Raised when a response was received. May fire on any thread, up to every poll interval.
    /// </summary>
    public event Action? Updated;

    /// <summary>
    /// Starts following the notification bus until <paramref name="cancellation"/> is cancelled.
    /// </summary>
    public void Start(YmcNotificationBus notifications, CancellationToken cancellation)
    {
        var subscription = notifications.Subscribe();
        _ = Task.Run(async () =>
        {
            using (subscription)
            {
                try
                {
                    await foreach (var notification in subscription.Reader.ReadAllAsync(cancellation).ConfigureAwait(false))
                    {
                        if (notification.Uuid == YmcBikeProtocol.DdtV2Uuid || notification.Uuid == YmcBikeProtocol.DdtV3Uuid)
                            Feed(notification.Uuid, notification.Value, DateTime.UtcNow);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Stopped.
                }
            }
        }, cancellation);
    }

    /// <summary>
    /// Handles one DDT v2/v3 indication (public so it can be tested without Bluetooth).
    /// </summary>
    public void Feed(Guid characteristic, byte[] indication, DateTime now)
    {
        if (!_loggedFirst)
        {
            _loggedFirst = true;
            _log.Info($"First DDT indication ({characteristic}): {Convert.ToHexString(indication)}");
        }

        byte[]? payload;
        lock (_gate)
        {
            if (!_assemblers.TryGetValue(characteristic, out var assembler)) _assemblers[characteristic] = assembler = new CanDdtV3Assembler();
            payload = assembler.Feed(indication, now);
        }
        if (payload == null) return;

        var readings = CanResponseParser.Parse(payload);
        if (readings == null)
        {
            _log.Debug($"Ignored a DDT session that isn't a CAN response ({payload.Length} bytes)");
            return;
        }

        lock (_gate)
        {
            foreach (var reading in readings)
            {
                var changedAt = _latest.TryGetValue(reading.Id, out var previous) && previous.Data.AsSpan().SequenceEqual(reading.Data)
                    ? previous.ChangedAt
                    : now;
                _latest[reading.Id] = new CanRawReading(reading.Id, reading.Data, now, changedAt);
            }
        }
        Updated?.Invoke();
    }

    /// <summary>
    /// Forgets everything (e.g. on a new connection).
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _latest.Clear();
            foreach (var assembler in _assemblers.Values) assembler.Clear();
        }
        Updated?.Invoke();
    }

    /// <summary>
    /// The raw readings, ordered by CAN ID.
    /// </summary>
    public IReadOnlyList<CanRawReading> Raw()
    {
        lock (_gate) return _latest.Values.OrderBy(r => r.Id).ToList();
    }

    /// <summary>
    /// Decoded values of the fresh readings.
    /// </summary>
    public CanValues Values(DateTime now)
    {
        Dictionary<int, (byte[], DateTime)> snapshot;
        lock (_gate) snapshot = _latest.ToDictionary(p => p.Key, p => (p.Value.Data, p.Value.ReceivedAt));
        return CanSignals.Decode(snapshot.ToDictionary(p => p.Key, p => p.Value), now);
    }
}
