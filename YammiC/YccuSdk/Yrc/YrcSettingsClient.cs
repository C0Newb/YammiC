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

    /// <summary>The saved ride modes on the phone: what the bike's own export stores into, and its import takes from.</summary>
    public ProfileLibrary? Library { get; set; }

    public YrcSettingsClient(YmcConnectionBroker broker, Logger logger)
    {
        _broker = broker;
        _log = logger.Section("Ride modes");
    }

    /// <summary>
    /// Raised when anything below changed.
    /// </summary>
    public event Action? Updated;

    /// <summary>
    /// The profiles read from the bike so far.
    /// </summary>
    public IReadOnlyList<YrcProfile> Profiles
    {
        get
        {
            lock (_gate) return _profiles.ToList();
        }
    }

    /// <summary>
    /// The common information (current mode, flags) from the last read.
    /// </summary>
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
                        else if (n.Uuid == YrcRxUuid) HandleMessage(n.Value);
                        else if (n.Uuid == YmcBikeProtocol.DdtV2Uuid || n.Uuid == YmcBikeProtocol.DdtV3Uuid)
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

    /// <summary>
    /// Forgets everything read (a new connection).
    /// </summary>
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

    /// <summary>
    /// One complete message: <c>[cmd 2B]...</c>.
    /// </summary>
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
            case (int)YmcCommandCode.RequestSavedYrcSettingsData:
            case (int)YmcCommandCode.RequestLoadYrcSettings:
                HandleBikeRequest(command, message);
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
        // One line per mode (type, saved number, name, values) so what the bike calls each mode can be checked in the log.
        foreach (var p in info.Profiles)
            _log.Info($"  {YrcNames.Mode(p.Mode)}: type {p.ModeType} ({YrcNames.ModeType(p.ModeType)}), saved no {p.SavedNo}, name '{p.Name.Trim()}', " +
                      $"{(p.Values.Count == 0 ? "no values" : ProfileText(p.Values))}");

        if (command == (int)YmcCommandCode.SaveYrcSettingsData) HandleExport(message[5], info);

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

    // -- The bike's own menu: export (save to phone) and import (load from phone) ---------------

    private ProfileLibrary? LibraryOrLog(string what)
    {
        if (Library != null) return Library;
        _log.Info(what + ": no ride mode library on this build");
        return null;
    }

    /// <summary>The number the bike uses for a saved entry: its place in the library, counted from 1.</summary>
    private static int NumberOf(IReadOnlyList<SavedRideMode> items, SavedRideMode item) => items.ToList().IndexOf(item) + 1;

    private static bool SameFamily(int a, int b) => a == b || (a is >= 28 and <= 31 && b is >= 28 and <= 31);

    private static bool Eligible(SavedRideMode item, YrcRequest request)
    {
        if (request.ModeType != 0 && item.ModeType != request.ModeType) return false;
        return request.Modes.Count == 0 || request.Modes.Any(m => m == YrcNames.ModeAll || SameFamily(item.Mode, m));
    }

    /// <summary>
    /// The bike saved one of its modes to the phone (its "export"): keep it in the library, then say so. The bike sends only the
    /// current values for this, so the ranges are filled in from what was read from the bike.
    /// </summary>
    private void HandleExport(byte bikeRequestId, YrcInfo info)
    {
        var library = LibraryOrLog("Export from the bike");
        if (library == null || info.Profiles.Count == 0)
        {
            _ = ReplySaveResultAsync(bikeRequestId, library == null ? YrcCodec.ResultIdNotFound : 18, "");
            return;
        }

        var name = "";
        foreach (var profile in info.Profiles)
        {
            List<YrcProfile> known;
            lock (_gate) known = _profiles.ToList();
            var reference = known.FirstOrDefault(p => p.Mode == profile.Mode);
            var values = profile.Values.Select(v =>
            {
                var r = reference?.Values.FirstOrDefault(x => x.Kind == v.Kind);
                return r != null ? v with { Min = r.Min, Max = r.Max } : v;
            }).ToList();
            var complete = profile with { Values = values };
            name = complete.Name.Trim();

            var duplicate = library.Items.Any(i =>
                i.Mode == complete.Mode && i.Name.Trim() == name &&
                i.Values.Select(v => (v.Kind, v.Current)).OrderBy(v => v.Kind).SequenceEqual(values.Select(v => (v.Kind, v.Current)).OrderBy(v => v.Kind)));
            if (duplicate)
            {
                _log.Info($"Export from the bike: {YrcNames.Mode(complete.Mode)} '{name}' is already in the library");
            }
            else
            {
                library.Add(complete, $"{(name.Length > 0 ? name : YrcNames.Mode(complete.Mode))} from bike {DateTime.Now:d MMM HH:mm}");
                _log.Info($"Export from the bike: saved {YrcNames.Mode(complete.Mode)} '{name}' to the library");
            }
        }
        _ = ReplySaveResultAsync(bikeRequestId, 0, name);
    }

    private async Task ReplySaveResultAsync(byte bikeRequestId, int result, string name)
    {
        try
        {
            var mb = _broker.MessageBroker;
            if (mb == null) return;
            await new AppNotifySaveYrcSettingsResult(mb.PacketHandler, bikeRequestId, result, name).SendAsync(mb.ConnectionContext).ConfigureAwait(false);
            _log.Info($"Answered the export (request {bikeRequestId}) with result {result}");
        }
        catch (Exception e)
        {
            _log.Info($"Could not answer the export: {e.Message}");
        }
    }

    /// <summary>
    /// The bike's menu asked for the saved modes (0x0205, to list them) or to have one loaded (0x0207).
    /// <c>[cmd 2B][length 3B][request id 1B][protobuf]</c>, sent by the bike on the ride-mode characteristic.
    /// </summary>
    private void HandleBikeRequest(int command, byte[] message)
    {
        _log.Info($"0x{command:X4} from the bike: {Convert.ToHexString(message)}");
        if (message.Length < 6) return;
        var bikeRequestId = message[5];

        YrcRequest request;
        try
        {
            request = YrcCodec.ParseRequest(message.AsSpan(6));
        }
        catch (FormatException e)
        {
            _log.Info($"Could not read the request ({e.Message})");
            return;
        }
        _log.Info($"  kinds [{string.Join(",", request.DataKinds)}], modes [{string.Join(",", request.Modes.Select(YrcNames.Mode))}], " +
                  $"type {request.ModeType} ({YrcNames.ModeType(request.ModeType)}), saved numbers [{string.Join(",", request.SavedNumbers)}]");

        _ = command == (int)YmcCommandCode.RequestSavedYrcSettingsData
            ? AnswerListAsync(bikeRequestId, request)
            : AnswerLoadAsync(bikeRequestId, request);
    }

    private async Task AnswerListAsync(byte bikeRequestId, YrcRequest request)
    {
        try
        {
            var mb = _broker.MessageBroker;
            var library = LibraryOrLog("Import list");
            if (mb == null || library == null) return;

            var items = library.Items;
            var offered = items.Where(i => Eligible(i, request)).ToList();
            var target = request.Modes.FirstOrDefault(m => m != YrcNames.ModeAll);
            var profiles = offered.Select(i => i.ToProfile() with { SavedNo = NumberOf(items, i), Mode = target != 0 ? target : i.Mode }).ToList();
            var proto = profiles.Count == 0
                ? YrcCodec.BuildSavedList(request.DataKinds, profiles, YrcCodec.ResultNoDataForMode)
                : YrcCodec.BuildSavedList(request.DataKinds, profiles);

            await new AppResponseSavedYrcSettings(mb.PacketHandler, bikeRequestId, proto).SendAsync(mb.ConnectionContext).ConfigureAwait(false);
            _log.Info($"Answered the import list (request {bikeRequestId}) with {profiles.Count} saved mode(s): " +
                      string.Join(", ", profiles.Select(p => $"#{p.SavedNo} '{p.Name.Trim()}'")));
        }
        catch (Exception e)
        {
            _log.Info($"Could not answer the import list: {e.Message}");
        }
    }

    private async Task AnswerLoadAsync(byte bikeRequestId, YrcRequest request)
    {
        try
        {
            var mb = _broker.MessageBroker;
            var library = LibraryOrLog("Import");
            if (mb == null || library == null) return;

            var items = library.Items;
            var target = request.Modes.FirstOrDefault(m => m != YrcNames.ModeAll);
            var chosen = request.SavedNumbers
                .Select(n => n >= 1 && n <= items.Count ? items[n - 1] : null)
                .OfType<SavedRideMode>()
                .Select(i => i.ToProfile() with { SavedNo = 0, Mode = target != 0 ? target : i.Mode })
                .ToList();

            var proto = chosen.Count == 0
                ? YrcCodec.BuildResultOnly(YrcCodec.ResultIdNotFound)
                : YrcCodec.BuildLoad(chosen, YrcCodec.StartedByBike, new[] { YrcInfoKind.Current, YrcInfoKind.Names });
            _log.Info("Load body: " + Convert.ToHexString(proto));
            await new AppLoadYrcSettingsData(mb.PacketHandler, bikeRequestId, proto).SendAsync(mb.ConnectionContext).ConfigureAwait(false);
            _log.Info(chosen.Count == 0
                ? $"Import (request {bikeRequestId}): none of the saved numbers [{string.Join(",", request.SavedNumbers)}] exists, said so"
                : $"Import (request {bikeRequestId}): sent {string.Join(", ", chosen.Select(p => $"'{p.Name.Trim()}' as {YrcNames.Mode(p.Mode)}"))}");
        }
        catch (Exception e)
        {
            _log.Info($"Could not answer the import: {e.Message}");
        }
    }

    /// <summary>
    /// Reads everything from the bike. Returns a one-line outcome for display.
    /// </summary>
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

    /// <summary>
    /// Sends profiles to the bike and waits for its verdict. Returns a line for display.
    /// </summary>
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

    private static string ProfileText(IEnumerable<YrcValue> values) =>
        string.Join(", ", values.Select(v => $"{YrcNames.Parameter(v.Kind)} {v.Current} ({v.Min}-{v.Max})"));

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
