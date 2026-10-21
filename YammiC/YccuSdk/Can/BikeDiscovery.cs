using System.Text;
using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Packets;

namespace CNCO.YccuSdk.Can;

/// <summary>
/// Finds out which data the bike can report: the list of ECU "servers", and for each the IDs it supports. For exploring
/// what exists (for example looking for a fuel level) rather than for everyday use.
/// </summary>
public sealed class BikeDiscovery
{
    private readonly Logger _log;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, CanDdtV3Assembler> _assemblers = new();
    private List<int>? _servers;
    private readonly SortedDictionary<int, List<int>> _supported = new();
    private readonly List<string> _unparsed = new();

    public BikeDiscovery(Logger logger) => _log = logger.Section("Discovery");

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
                        if (n.Uuid == YmcBikeProtocol.MainRxUuid) Handle(n.Value);
                        else if (n.Uuid == YmcBikeProtocol.DdtV2Uuid || n.Uuid == YmcBikeProtocol.DdtV3Uuid) FeedDdt(n.Uuid, n.Value);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Stopped.
                }
            }
        }, cancellation);
    }

    private void FeedDdt(Guid characteristic, byte[] indication)
    {
        byte[]? payload;
        lock (_gate)
        {
            if (!_assemblers.TryGetValue(characteristic, out var assembler)) _assemblers[characteristic] = assembler = new CanDdtV3Assembler();
            payload = assembler.Feed(indication, DateTime.UtcNow);
        }
        if (payload != null) Handle(payload);
    }

    /// <summary>
    /// Handles one complete message: <c>[cmd 2B][len 3B][protobuf]</c>.
    /// </summary>
    private void Handle(byte[] message)
    {
        if (message.Length < 2) return;
        var command = (message[0] << 8) | message[1];
        if (command != (int)YmcCommandCode.ConnectServerListResponse && command != (int)YmcCommandCode.VehicleInformationSupportIdListResponse)
            return;

        var fields = message.Length >= 5 ? ReadFields(message, 5) : null;
        if (fields == null)
        {
            lock (_gate) _unparsed.Add($"0x{command:X4}: {Convert.ToHexString(message)}");
            _log.Info($"Could not read response 0x{command:X4}: {Convert.ToHexString(message)}");
            return;
        }

        lock (_gate)
        {
            if (command == (int)YmcCommandCode.ConnectServerListResponse)
            {
                _servers = fields.TryGetValue(3, out var servers) ? servers : new List<int>();
                _log.Info($"Server list: {string.Join(", ", _servers)}");
            }
            else
            {
                var server = fields.TryGetValue(3, out var s) && s.Count > 0 ? s[0] : -1;
                _supported[server] = fields.TryGetValue(4, out var ids) ? ids : new List<int>();
                _log.Info($"Server {server} supports {_supported[server].Count} IDs");
            }
        }
    }

    /// <summary>
    /// Reads the integer fields of a protobuf message; packed repeated fields are expanded.
    /// </summary>
    private static Dictionary<int, List<int>>? ReadFields(byte[] buffer, int start)
    {
        var result = new Dictionary<int, List<int>>();
        var pos = start;
        try
        {
            while (pos < buffer.Length)
            {
                var tag = (int)Varint(buffer, ref pos);
                var field = tag >> 3;
                var wire = tag & 7;
                if (!result.TryGetValue(field, out var list)) result[field] = list = new List<int>();
                if (wire == 0)
                {
                    list.Add((int)Varint(buffer, ref pos));
                }
                else if (wire == 2)
                {
                    var length = (int)Varint(buffer, ref pos);
                    var end = pos + length;
                    while (pos < end) list.Add((int)Varint(buffer, ref pos));
                    pos = end;
                }
                else
                {
                    return null;
                }
            }
        }
        catch (Exception e) when (e is IndexOutOfRangeException or FormatException)
        {
            return null;
        }
        return result;
    }

    private static ulong Varint(byte[] buffer, ref int pos)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var b = buffer[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
        }
        throw new FormatException("Varint too long");
    }

    public void Reset()
    {
        lock (_gate)
        {
            _servers = null;
            _supported.Clear();
            _unparsed.Clear();
            foreach (var assembler in _assemblers.Values) assembler.Clear();
        }
    }

    public IReadOnlyList<int>? Servers
    {
        get
        {
            lock (_gate) return _servers?.ToList();
        }
    }

    public bool HasSupportFor(int server)
    {
        lock (_gate) return _supported.ContainsKey(server);
    }

    /// <summary>
    /// Waits until <paramref name="done"/> holds or the time is up.
    /// </summary>
    public static async Task<bool> WaitAsync(Func<bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (done()) return true;
            await Task.Delay(100).ConfigureAwait(false);
        }
        return done();
    }

    /// <summary>
    /// What was found, as text for the explorer page.
    /// </summary>
    public string Report()
    {
        lock (_gate)
        {
            var text = new StringBuilder();
            if (_servers == null) text.AppendLine("No server list received.");
            else text.AppendLine($"Servers: {(_servers.Count == 0 ? "(none)" : string.Join(", ", _servers))}");
            foreach (var (server, ids) in _supported)
            {
                text.AppendLine();
                text.AppendLine($"Server {server}: {ids.Count} IDs");
                text.AppendLine(string.Join(" ", ids.Select(id => $"0x{id:X4}")));
            }
            foreach (var line in _unparsed) text.AppendLine().AppendLine("Unreadable: " + line);
            return text.ToString().TrimEnd();
        }
    }
}
