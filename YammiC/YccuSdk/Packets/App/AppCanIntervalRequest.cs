namespace CNCO.YccuSdk.Packets.App;

using CNCO.YccuSdk.Logging;

/// <summary>
/// Asks the CCU to report CAN signals periodically. The answers arrive as <see cref="YmcCommandCode.CanResponse"/>
/// sessions on the bike-to-phone DDT v3 characteristic.
///
/// Wire (DDT): `[cmd 2B: 0x013B][payloadLen 3B][protobuf]`, where the protobuf is
/// - field 3  id_list  (repeated int32, packed: tag 0x1A, length, varints)
/// - field 4  interval (int32 milliseconds; omitted when 0)
/// </summary>
[SuppressUiLog(YmcCommandCode.CanIntervalRequest)]
public class AppCanIntervalRequest : AppOutgoingRequest
{
    private readonly IReadOnlyList<int> _ids;
    private readonly int _intervalMs;

    public AppCanIntervalRequest(IYmcPacketHandler packetHandler, IReadOnlyList<int> ids, int intervalMs)
        : base(packetHandler, YmcCommandCode.CanIntervalRequest)
    {
        _ids = ids;
        _intervalMs = intervalMs;
    }

    public override Transport PacketTransport => Transport.Ddt;

    public override byte[] Serialize(byte requestId)
    {
        var proto = BuildProto();
        return Concat(Preamble, Int24(proto.Length), proto);
    }

    private byte[] BuildProto()
    {
        var packed = new List<byte>();
        foreach (var id in _ids) packed.AddRange(Varint(id));

        var result = new List<byte>();
        if (packed.Count > 0)
        {
            result.AddRange(Varint((3 << 3) | 2));
            result.AddRange(Varint(packed.Count));
            result.AddRange(packed);
        }
        if (_intervalMs != 0)
        {
            result.AddRange(Varint((4 << 3) | 0));
            result.AddRange(Varint(_intervalMs));
        }
        return result.ToArray();
    }

    private static byte[] Varint(int value)
    {
        var v = (uint)value;
        var bytes = new List<byte>(5);
        do
        {
            var b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0) b |= 0x80;
            bytes.Add(b);
        } while (v != 0);
        return bytes.ToArray();
    }
}
