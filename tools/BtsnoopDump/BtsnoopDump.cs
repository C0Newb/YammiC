// Usage: dotnet run tools/BtsnoopDump/BtsnoopDump.cs -- "<path>/btsnoop_hci.log" > capture.txt
// Prints ATT writes, notifications and indications, with UUIDs resolved from characteristic discovery.
// Output contains device traffic: never commit it.

using System.Buffers.Binary;
using System.Text;

if (args.Length < 1) { Console.Error.WriteLine("usage: BtsnoopDump <btsnoop_hci.log>"); return 1; }
var data = File.ReadAllBytes(args[0]);
if (data.Length < 16 || Encoding.ASCII.GetString(data, 0, 7) != "btsnoop") { Console.Error.WriteLine("not a btsnoop file"); return 1; }
var datalink = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12));
if (datalink != 1002) { Console.Error.WriteLine($"datalink {datalink} not supported (need 1002, HCI UART/H4)"); return 1; }

const long UnixEpochMicros = 0x00dcddb30f2f8000L;
var uuidByHandle = new Dictionary<int, (string Uuid, int Props)>();
var pos = 16;
while (pos + 24 <= data.Length)
{
    var inclLen = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4));
    var flags = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 8));
    var micros = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(pos + 16));
    pos += 24;
    if (inclLen < 0 || pos + inclLen > data.Length) break;
    var packet = data.AsSpan(pos, inclLen);
    pos += inclLen;

    var att = AttPayload(packet);
    if (att.IsEmpty) continue;

    var time = DateTime.UnixEpoch.AddTicks((micros - UnixEpochMicros) * 10).ToLocalTime();
    var received = (flags & 1) != 0;                      // received = CCU → phone, sent = phone → CCU
    var opcode = att[0];

    if (opcode == 0x09 && att.Length > 2 && att[1] == 21)  // Read By Type response, 128-bit characteristic declarations
    {
        for (var i = 2; i + 21 <= att.Length; i += 21)
        {
            var props = att[i + 2];
            var valueHandle = BinaryPrimitives.ReadUInt16LittleEndian(att[(i + 3)..]);
            uuidByHandle[valueHandle] = (Uuid(att.Slice(i + 5, 16)), props);
        }
        continue;
    }

    string? kind = opcode switch { 0x12 => "write-req", 0x52 => "write-cmd", 0x1B => "notify", 0x1D => "indicate", 0x0B => "read-rsp", _ => null };
    if (kind is null || att.Length < 3) continue;
    var handle = BinaryPrimitives.ReadUInt16LittleEndian(att[1..]);
    var value = opcode == 0x0B ? att[1..] : att[3..];
    var name = uuidByHandle.TryGetValue(handle, out var info) ? info.Uuid : $"handle 0x{handle:X4}";
    Console.WriteLine($"{time:HH:mm:ss.fff} {(received ? "CCU→phone" : "phone→CCU")} {kind,-9} {name} {Convert.ToHexString(value)}");
}

Console.WriteLine();
Console.WriteLine("# Characteristics seen during discovery (properties bit field: 0x02 read, 0x04 write-no-rsp, 0x08 write, 0x10 notify, 0x20 indicate)");
foreach (var (handle, (uuid, props)) in uuidByHandle.OrderBy(kv => kv.Key))
    Console.WriteLine($"# 0x{handle:X4} {uuid} props=0x{props:X2}");
return 0;

static ReadOnlySpan<byte> AttPayload(ReadOnlySpan<byte> packet)
{
    if (packet.Length < 1 + 4 + 4 || packet[0] != 0x02) return default;          // H4 ACL data only
    var acl = packet[1..];
    var handleFlags = BinaryPrimitives.ReadUInt16LittleEndian(acl);
    if (((handleFlags >> 12) & 0x3) == 0x1) return default;                      // continuation fragment
    var l2cap = acl[4..];
    if (l2cap.Length < 4 || BinaryPrimitives.ReadUInt16LittleEndian(l2cap[2..]) != 0x0004) return default;   // ATT channel
    var length = BinaryPrimitives.ReadUInt16LittleEndian(l2cap);
    var payload = l2cap[4..];
    return payload.Length >= length && length > 0 ? payload[..length] : default;
}

static string Uuid(ReadOnlySpan<byte> littleEndian)
{
    var b = littleEndian.ToArray();
    Array.Reverse(b);
    return new Guid(Convert.ToHexString(b)[..8] + "-" + Convert.ToHexString(b)[8..12] + "-" + Convert.ToHexString(b)[12..16]
        + "-" + Convert.ToHexString(b)[16..20] + "-" + Convert.ToHexString(b)[20..]).ToString().ToUpperInvariant();
}
