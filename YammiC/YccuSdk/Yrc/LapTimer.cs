using System.Diagnostics;
using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.App;

namespace CNCO.YccuSdk.Yrc;

/// <summary>
/// One finished lap and what became of sending it to the bike.
/// </summary>
public sealed record LapRecord(int Number, TimeSpan Time, string Status);

/// <summary>
/// A stopwatch that reports finished laps to the bike. For trying the lap-time message out: start it, press Lap
/// at each line, and the bike should show the lap (against the best lap so far).
/// </summary>
public sealed class LapTimer
{
    private readonly Logger _log;
    private readonly YmcConnectionBroker _broker;
    private readonly object _gate = new();
    private readonly Stopwatch _clock = new();
    private readonly List<LapRecord> _laps = new();
    private TimeSpan _lapStart;
    private bool _running;
    private string _lastAck = "";

    public LapTimer(YmcConnectionBroker broker, Logger logger)
    {
        _broker = broker;
        _log = logger.Section("Lap timer");
    }

    public event Action? Updated;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _running;
        }
    }

    public TimeSpan Elapsed => _clock.Elapsed;

    public TimeSpan CurrentLap
    {
        get
        {
            lock (_gate)
                return _clock.Elapsed - _lapStart;
        }
    }

    public IReadOnlyList<LapRecord> Laps
    {
        get
        {
            lock (_gate)
                return _laps.ToList();
        }
    }

    public string LastAck
    {
        get
        {
            lock (_gate)
                return _lastAck;
        }
    }

    public void Start(YmcNotificationBus notifications, CancellationToken cancellation)
    {
        var subscription = notifications.Subscribe();
        _ = Task.Run(
            async () =>
            {
                using (subscription)
                {
                    try
                    {
                        await foreach (
                            var n in subscription
                                .Reader.ReadAllAsync(cancellation)
                                .ConfigureAwait(false)
                        )
                        {
                            if (n.Uuid == YmcBikeProtocol.MainRxUuid)
                                HandleMessage(n.Value);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Stopped.
                    }
                }
            },
            cancellation
        );
    }

    private void HandleMessage(byte[] message)
    {
        // [cmd 2B: 0x0252][requestId][1 = OK, 0 = NG]
        if (
            message.Length < 4
            || ((message[0] << 8) | message[1]) != (int)YmcCommandCode.SendLapSectorTimeResponse
        )
            return;
        var text = message[3] == 1 ? "OK" : "NG";
        _log.Info($"Bike answered request {message[2]}: {text}");
        lock (_gate)
        {
            _lastAck = text;
            if (_laps.Count > 0)
                _laps[^1] = _laps[^1] with { Status = text };
        }
        Updated?.Invoke();
    }

    /// <summary>
    /// Starts from zero.
    /// </summary>
    public void StartTimer()
    {
        lock (_gate)
        {
            _laps.Clear();
            _lastAck = "";
            _clock.Restart();
            _lapStart = TimeSpan.Zero;
            _running = true;
        }
        _log.Info("Started");
        Updated?.Invoke();
    }

    /// <summary>
    /// Stops the clock; the lap in progress is reported as the last lap.
    /// </summary>
    public async Task StopTimerAsync()
    {
        bool wasRunning;
        lock (_gate)
        {
            wasRunning = _running;
            _clock.Stop();
            _running = false;
        }
        if (!wasRunning)
            return;
        _log.Info("Stopped");
        await FinishLapAsync();
    }

    /// <summary>
    /// Ends the current lap and starts the next one.
    /// </summary>
    public Task LapAsync() => IsRunning ? FinishLapAsync() : Task.CompletedTask;

    private async Task FinishLapAsync()
    {
        LapRecord lap;
        int reference;
        lock (_gate)
        {
            var now = _clock.Elapsed;
            var time = now - _lapStart;
            _lapStart = now;
            var best = _laps
                .Where(l => l.Time > TimeSpan.Zero)
                .Select(l => l.Time)
                .DefaultIfEmpty(TimeSpan.Zero)
                .Min();
            reference = best > TimeSpan.Zero ? ToCentiseconds(best) : AppSendLapSectorTime.NoTime;
            lap = new LapRecord(_laps.Count + 1, time, "Sending");
            _laps.Add(lap);
        }
        Updated?.Invoke();

        var lapCs = Math.Min(ToCentiseconds(lap.Time), 359999);
        var gaps = new[]
        {
            AppSendLapSectorTime.GapNotMeasured,
            AppSendLapSectorTime.GapNotMeasured,
            AppSendLapSectorTime.GapNotMeasured,
            AppSendLapSectorTime.GapNotMeasured,
        };
        var problem = AppSendLapSectorTime.Validate(lap.Number, lapCs, reference, 0, gaps);
        var mb = _broker.MessageBroker;
        string status;
        if (problem != null)
        {
            status = problem;
        }
        else if (!_broker.IsBikeConnected || mb == null)
        {
            status = "Not connected";
        }
        else
        {
            _log.Info(
                $"Lap {lap.Number}: {lapCs} cs (reference {(reference == AppSendLapSectorTime.NoTime ? "none" : reference.ToString())})"
            );
            await new AppSendLapSectorTime(
                mb.PacketHandler,
                lap.Number,
                lapCs,
                reference,
                0,
                gaps
            ).SendAsync(mb.ConnectionContext);
            status = "Sent";
        }

        lock (_gate)
        {
            var index = _laps.FindIndex(l => l.Number == lap.Number);
            // The bike's answer may already have replaced the status; keep it.
            if (index >= 0 && _laps[index].Status == "Sending")
                _laps[index] = _laps[index] with { Status = status };
        }
        Updated?.Invoke();
    }

    private static int ToCentiseconds(TimeSpan t) => (int)(t.TotalMilliseconds / 10);
}
