using CNCO.YccuSdk.Can;
using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.App;

namespace CNCO.YccuSdk.Yrc;

/// <summary>
/// Reads the bike's ride-mode settings (Custom, Track 1-4, ...) and sends settings back. Reading follows the same
/// two steps as the official app: first the common information (which tells us the modes), then the names, ranges
/// and current values of those modes.
/// </summary>
public sealed class YrcSettingsClient
{
    private static readonly Guid YrcRxUuid = Guid.Parse("AB8D233A-CC58-4932-B397-A3751911E021");

    private readonly Logger _log;
    private readonly YmcConnectionBroker _broker;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, CanDdtV3Assembler> _assemblers = new();
    private readonly SemaphoreSlim _busy = new(1, 1);

    private List<YrcProfile> _profiles = new();
    private YrcInfo? _common;
    private int _responses;
    private int? _lastResult;
    private int _loadResults;
    private DateTime? _readAt;

    public YrcSettingsClient(YmcConnectionBroker broker, Logger logger)
    {
        _broker = broker;
        _log = logger.Section("Ride modes");
    }

    /// <summary>Raised when anything below changed.</summary>
    public event Action? Updated;

    /// <summary>The profiles read from the bike so far.</summary>
    public IReadOnlyList<YrcProfile> Profiles
    {
        get
        {
            lock (_gate) return _profiles.ToList();
        }
    }

    /// <summary>The common information (current mode, flags) from the last read.</summary>
    public YrcInfo? Common
    {
        get
        {
            lock (_gate) return _common;
        }
    }

    public DateTime? ReadAt
    {
        get
        {
            lock (_gate) return _readAt;
        }
    }

    public void Start(YmcNotificationBus notifications, CancellationToken cancellation)
    {
        var subscription = notifications.Subscribe();
        _ = Task.Run(async () =>
        {
            using (subscription)
            {
                try
                {
                    await foreach (var n in subscription.Reader.ReadAllAsync(cancellation).ConfigureAwait(false))
                    {
                        if (n.Uuid == YmcBikeProtocol.MainRxUuid) HandleMessage(n.Value);
                        else if (n.Uuid == YmcBikeProtocol.DdtV2Uuid || n.Uuid == YmcBikeProtocol.DdtV3Uuid || n.Uuid == YrcRxUuid)
                            FeedDdt(n.Uuid, n.Value);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Stopped.
                }
            }
        }, cancellation);
    }

    /// <summary>Forgets everything read (a new connection).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _profiles = new List<YrcProfile>();
            _common = null;
            _readAt = null;
            foreach (var a in _assemblers.Values) a.Clear();
        }
        Updated?.Invoke();
    }

    private void FeedDdt(Guid characteristic, byte[] indication)
    {
        byte[]? payload;
        lock (_gate)
        {
            if (!_assemblers.TryGetValue(characteristic, out var assembler)) _assemblers[characteristic] = assembler = new CanDdtV3Assembler();
            payload = assembler.Feed(indication, DateTime.UtcNow);
        }
        if (payload != null) HandleMessage(payload);
    }

    /// <summary>One complete message: <c>[cmd 2B]...</c>.</summary>
    private void HandleMessage(byte[] message)
    {
        if (message.Length < 2) return;
        var command = (message[0] << 8) | message[1];
        switch (command)
        {
            case (int)YmcCommandCode.ResponseYrcSettingsInfo:
            case (int)YmcCommandCode.SaveYrcSettingsData:
                HandleInfo(command, message);
                break;
            case (int)YmcCommandCode.NotifyLoadYrcSettingsDataResult:
                if (message.Length < 7)
                {
                    _log.Info($"Load result too short: {Convert.ToHexString(message)}");
                    break;
                }
                lock (_gate)
                {
                    _lastResult = (message[3] << 24) | (message[4] << 16) | (message[5] << 8) | message[6];
                    _loadResults++;
                }
                _log.Info($"Load result {_lastResult} ({ResultText(_lastResult.Value)}) for request {message[2]}");
                Updated?.Invoke();
                break;
        }
    }

    private void HandleInfo(int command, byte[] message)
    {
        // [cmd 2B][len 3B][requestId 1B][protobuf]
        if (message.Length < 6)
        {
            _log.Info($"0x{command:X4} too short: {Convert.ToHexString(message)}");
            return;
        }

        YrcInfo info;
        try
        {
            info = YrcCodec.ParseInfo(message.AsSpan(6));
        }
        catch (FormatException e)
        {
            _log.Info($"Could not read 0x{command:X4} ({e.Message}): {Convert.ToHexString(message)}");
            lock (_gate) _responses++;
            return;
        }

        _log.Info($"0x{command:X4}: result {info.Result}, kinds [{string.Join(",", info.DataKinds)}], " +
                  $"{info.Profiles.Count} modes ({string.Join(", ", info.Profiles.Select(p => YrcNames.Mode(p.Mode)))}), " +
                  $"current mode {YrcNames.Mode(info.CurrentMode)}");

        lock (_gate)
        {
            if (info.DataKinds.Contains(YrcInfoKind.Common) || info.DataKinds.Count == 0 && info.Profiles.Count == 0) _common = info;
            if (info.Result == 0)
            {
                _profiles = YrcCodec.Merge(_profiles, info);
                _readAt = DateTime.Now;
            }
            _lastResult = info.Result;
            _responses++;
        }
        Updated?.Invoke();
    }

    /// <summary>Reads everything from the bike. Returns a one-line outcome for display.</summary>
    public async Task<string> RefreshAsync()
    {
        if (!_broker.IsBikeConnected) return "Not connected to the bike";
        if (!await _busy.WaitAsync(0).ConfigureAwait(false)) return "Another transfer is running";
        try
        {
            lock (_gate)
            {
                _profiles = new List<YrcProfile>();
                _common = null;
            }
            Updated?.Invoke();

            // Step 1: common information, which also lists the modes.
            if (!await RequestAsync(new[] { YrcInfoKind.Common }, Array.Empty<int>()).ConfigureAwait(false))
                return "The bike did not answer the first request (see the log)";

            List<int> modes;
            lock (_gate) modes = (_common?.Profiles.Select(p => p.Mode) ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (modes.Count == 0)
            {
                _log.Info("The common answer listed no modes; asking for all of them");
                modes.Add(YrcNames.ModeAll);
            }

            // Step 2: names, ranges and current values of those modes.
            if (!await RequestAsync(new[] { YrcInfoKind.Names, YrcInfoKind.MinMax, YrcInfoKind.Current }, modes).ConfigureAwait(false))
                return "The bike did not answer the settings request (see the log)";

            lock (_gate) return $"Read {_profiles.Count} ride modes";
        }
        finally
        {
            _busy.Release();
        }
    }

    private async Task<bool> RequestAsync(int[] kinds, IReadOnlyCollection<int> modes)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            int before;
            lock (_gate) before = _responses;
            _log.Info($"Request kinds [{string.Join(",", kinds)}] modes [{string.Join(",", modes.Select(YrcNames.Mode))}] (attempt {attempt})");
            var mb = _broker.MessageBroker;
            if (mb == null) return false;
            await new AppYrcSettingsInfoRequest(mb.PacketHandler, kinds, modes).SendAsync(mb.ConnectionContext).ConfigureAwait(false);
            if (await BikeDiscovery.WaitAsync(() => { lock (_gate) return _responses > before; }, TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                // More than one message can follow for a big answer; give stragglers a moment.
                await Task.Delay(300).ConfigureAwait(false);
                return true;
            }
        }
        return false;
    }

    /// <summary>Sends profiles to the bike and waits for its verdict. Returns a line for display.</summary>
    public async Task<string> SendAsync(IReadOnlyList<YrcProfile> profiles)
    {
        if (!_broker.IsBikeConnected) return "Not connected to the bike";
        if (profiles.Count == 0) return "Nothing to send";
        if (!await _busy.WaitAsync(0).ConfigureAwait(false)) return "Another transfer is running";
        try
        {
            int before;
            lock (_gate) before = _loadResults;
            _log.Info($"Sending {profiles.Count} ride modes: {string.Join(", ", profiles.Select(p => $"{YrcNames.Mode(p.Mode)} '{p.Name}'"))}");
            var mb = _broker.MessageBroker;
            if (mb == null) return "Not connected to the bike";
            await new AppLoadYrcSettingsData(mb.PacketHandler, profiles).SendAsync(mb.ConnectionContext).ConfigureAwait(false);
            if (!await BikeDiscovery.WaitAsync(() => { lock (_gate) return _loadResults > before; }, TimeSpan.FromSeconds(10)).ConfigureAwait(false))
                return "The bike did not confirm (see the log)";
            lock (_gate)
            {
                var result = _lastResult ?? -1;
                return result == 0 ? $"Sent {profiles.Count} ride modes" : $"The bike refused: {ResultText(result)} ({result})";
            }
        }
        finally
        {
            _busy.Release();
        }
    }

    private static string ResultText(int result) => result switch
    {
        0 => "OK",
        1 => "invalid command",
        2 => "parameter missing",
        3 => "parameter out of range",
        4 => "not supported",
        5 => "timeout",
        6 => "negative response",
        7 => "message format error",
        8 => "data length error",
        9 => "another command is running",
        17 => "could not parse",
        18 => "data could not be applied",
        255 => "unknown error",
        _ => "error",
    };
}
