using System.Text;

namespace CNCO.YccuSdk.Packets;

public abstract class YmcPacket
{
    protected YmcPacket(YmcCommandCode commandCode, IYmcPacketHandler packetHandler)
    {
        PacketHandler = packetHandler;
        CommandCode = (int)commandCode;
    }

    protected IYmcPacketHandler PacketHandler { get; set; }

    /// <summary>
    /// Packet id.
    /// </summary>
    public int CommandCode { get; }

    // -- Helpers --

    protected static byte[] StringBytes(string value, int? maxBytes = null)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return maxBytes == null || bytes.Length <= maxBytes ? bytes : bytes[..maxBytes.Value];
    }

    protected static byte[] Int24(int value) =>
        new[]
        {
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF),
        };

    protected static byte[] ShortBytes(int value)
    {
        var safe = value & 0xFFFF;
        return new[] { (byte)((safe >> 8) & 0xFF), (byte)(safe & 0xFF) };
    }

    protected static byte[] IntBytes(int value) =>
        new[]
        {
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF),
        };

    protected static byte[] Concat(params byte[][] arrays) => YmcBikeProtocol.ConcatPackets(arrays);

    /// <summary>
    /// First <paramref name="count"/> characters of <paramref name="value"/> (or all of it if shorter).
    /// </summary>
    protected static string Take(string value, int count) => value.Length <= count ? value : value[..count];
}
