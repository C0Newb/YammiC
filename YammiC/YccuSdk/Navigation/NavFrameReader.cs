using CNCO.YccuSdk.Navigation.Protocol;

namespace CNCO.YccuSdk.Navigation;

/// <summary>
/// A frame parsed off the wire.
/// </summary>
public record NavFrameView(int ServiceType, byte[] Payload);

/// <summary>
/// Reads complete NaviLite frames from a <see cref="INavByteChannel"/>.
/// Single responsibility: framing/resync. It owns no transport and no protocol semantics
/// beyond locating frame boundaries.
///
/// Ported from Pillion's FrameReader.
/// </summary>
public class NavFrameReader
{
    private readonly INavByteChannel _channel;
    private byte[] _buf = Array.Empty<byte>();
    private readonly byte[] _tmp = new byte[8192];

    public NavFrameReader(INavByteChannel channel)
    {
        _channel = channel;
    }

    /// <summary>
    /// Reads and returns the next complete frame from the channel.
    /// May skip garbage bytes to resynchronize on frame magic.
    /// Throws if the channel is closed.
    /// </summary>
    public NavFrameView Next()
    {
        Fill(NaviLiteCodec.HeaderSize + NaviLiteCodec.CrcSize);
        while (!NaviLiteCodec.HasMagicAt(_buf, 0)) DropOne();
        var len = NaviLiteCodec.FrameLengthAt(_buf, 0);
        if (len < NaviLiteCodec.HeaderSize + NaviLiteCodec.CrcSize) throw new InvalidOperationException($"bad frame length {len}");
        Fill(len);
        var view = new NavFrameView(NaviLiteCodec.ServiceTypeAt(_buf, 0), NaviLiteCodec.PayloadAt(_buf, 0));
        _buf = _buf[len..];
        return view;
    }

    private void Fill(int n)
    {
        while (_buf.Length < n)
        {
            var r = _channel.Read(_tmp);
            if (r < 0) throw new InvalidOperationException("channel closed");
            if (r > 0) _buf = YmcBikeProtocol.ConcatPackets(_buf, _tmp[..r]);
        }
    }

    private void DropOne()
    {
        Fill(4);
        _buf = _buf[1..];
    }
}
