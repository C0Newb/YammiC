namespace CNCO.YccuSdk.Navigation.Protocol;

/// <summary>
/// Encodes/decodes NaviLite frames.
///
/// Frame layout: magic "nAl@" | version(1) | frameType(1) | serviceType(1) |
///               payloadSize(4 LE) | payloadDataType(1) | CRC(4 LE) | payload[payloadSize]
///
/// Ported from the Pillion reference app's NaviLiteCodec. Pure byte&lt;-&gt;frame translation,
/// no transport or I/O.
/// </summary>
public static class NaviLiteCodec
{
    public const int HeaderSize = 12;
    public const int CrcSize = 4;
    public static readonly byte[] Magic = { 0x6e, 0x41, 0x6c, 0x40 }; // "nAl@"

    /// <summary>Build a full on-wire frame. CRC covers header+payload, not itself.</summary>
    public static byte[] Build(int frameType, int serviceType, int payloadDataType, byte[] payload)
    {
        var size = payload.Length;
        var header = new byte[]
        {
            Magic[0], Magic[1], Magic[2], Magic[3],
            1, (byte)frameType, (byte)serviceType,
            (byte)(size & 0xff),
            (byte)((size >> 8) & 0xff),
            (byte)((size >> 16) & 0xff),
            (byte)((size >> 24) & 0xff),
            (byte)payloadDataType,
        };
        var crc = NaviLiteCrc32.Compute(YmcBikeProtocol.ConcatPackets(header, payload));
        var output = new byte[HeaderSize + CrcSize + size];
        header.CopyTo(output, 0);
        output[12] = (byte)(crc & 0xff);
        output[13] = (byte)((crc >> 8) & 0xff);
        output[14] = (byte)((crc >> 16) & 0xff);
        output[15] = (byte)((crc >> 24) & 0xff);
        payload.CopyTo(output, 16);
        return output;
    }

    /// <summary>The total wire length of a frame whose header begins at <paramref name="data"/>[<paramref name="off"/>], or -1 if incomplete.</summary>
    public static int FrameLengthAt(byte[] data, int off)
    {
        if (off + HeaderSize + CrcSize > data.Length) return -1;
        var size = data[off + 7] |
            (data[off + 8] << 8) |
            (data[off + 9] << 16) |
            (data[off + 10] << 24);
        return HeaderSize + CrcSize + size;
    }

    public static int ServiceTypeAt(byte[] data, int off) => data[off + 6];

    public static byte[] PayloadAt(byte[] data, int off)
    {
        var size = data[off + 7] |
            (data[off + 8] << 8) |
            (data[off + 9] << 16) |
            (data[off + 10] << 24);
        return data[(off + 16)..(off + 16 + size)];
    }

    public static bool HasMagicAt(byte[] data, int off) =>
        off + 4 <= data.Length &&
        data[off] == Magic[0] && data[off + 1] == Magic[1] &&
        data[off + 2] == Magic[2] && data[off + 3] == Magic[3];
}
