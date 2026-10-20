namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Sends a notification to the CCU display.
///
/// Wire: `[cmd 2B: 0x012F][payloadLen 3B][protobuf payload]`
///
/// The payload is a hand-encoded Protocol Buffers message (McNotificationDataV2):
/// - field 3  notificationId (varint)
/// - field 4  category (varint: 0=MESSAGE, 1=CALL)
/// - field 5-10  year/month/day/hour/minutes/second (varint each)
/// - field 11 title (len-delimited string, max 64 UTF-8 bytes)
/// - field 12 subTitle (len-delimited string, max 64 UTF-8 bytes)
/// - field 13 detail (len-delimited string, max 512 UTF-8 bytes)
/// </summary>
public class AppNotificationDataV2 : AppOutgoingRequest
{
    private readonly int _notificationId;
    private readonly Category _category;
    private readonly string _title;
    private readonly string _subTitle;
    private readonly string _detail;
    private readonly DateTime _timestamp;

    public AppNotificationDataV2(
        IYmcPacketHandler packetHandler,
        int notificationId,
        Category category,
        string title,
        string subTitle = "",
        string detail = "",
        DateTime? timestamp = null)
        : base(packetHandler, YmcCommandCode.NotificationDataV2)
    {
        _notificationId = notificationId;
        _category = category;
        _title = title;
        _subTitle = subTitle;
        _detail = detail;
        _timestamp = timestamp ?? DateTime.Now;
    }

    public enum Category
    {
        Message = 0,
        Call = 1,
    }

    public override Transport PacketTransport => Transport.Ddt;

    public override byte[] Serialize(byte requestId)
    {
        var proto = BuildProto();
        return Concat(Preamble, Int24(proto.Length), proto);
    }

    private byte[] BuildProto()
    {
        var titleBytes = StringBytes(_title, maxBytes: 64);
        var subTitleBytes = StringBytes(_subTitle, maxBytes: 64);
        var detailBytes = StringBytes(_detail, maxBytes: 512);

        return Concat(
            ProtoVarint(3, _notificationId),
            ProtoVarint(4, (int)_category),
            ProtoVarint(5, _timestamp.Year),
            ProtoVarint(6, _timestamp.Month),
            ProtoVarint(7, _timestamp.Day),
            ProtoVarint(8, _timestamp.Hour),
            ProtoVarint(9, _timestamp.Minute),
            ProtoVarint(10, _timestamp.Second),
            titleBytes.Length > 0 ? ProtoString(11, titleBytes) : Array.Empty<byte>(),
            subTitleBytes.Length > 0 ? ProtoString(12, subTitleBytes) : Array.Empty<byte>(),
            detailBytes.Length > 0 ? ProtoString(13, detailBytes) : Array.Empty<byte>());
    }

    private static byte[] ProtoVarint(int fieldNumber, int value)
    {
        var tag = (fieldNumber << 3) | 0;
        return Concat(Varint(tag), Varint(value));
    }

    private static byte[] ProtoString(int fieldNumber, byte[] utf8Bytes)
    {
        var tag = (fieldNumber << 3) | 2;
        return Concat(Varint(tag), Varint(utf8Bytes.Length), utf8Bytes);
    }

    private static byte[] Varint(int value)
    {
        var v = (uint)value;
        var buf = new byte[5];
        var pos = 0;
        do
        {
            var b = (int)(v & 0x7F);
            v >>= 7;
            if (v != 0) b |= 0x80;
            buf[pos++] = (byte)b;
        } while (v != 0);
        return buf[..pos];
    }
}
