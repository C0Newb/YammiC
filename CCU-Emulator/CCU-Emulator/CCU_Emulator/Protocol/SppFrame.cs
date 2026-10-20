namespace CCU_Emulator.Protocol;

/// <summary>SPP framing: <c>[0x01][length 4B BE][payload][0x04]</c>.</summary>
public static class SppFrame
{
    public static byte[] Encode(byte[] payload)
        => new PacketWriter().U8(0x01).I32(payload.Length).Bytes(payload).U8(0x04).ToArray();
}

/// <summary>Incremental SPP frame parser. Resyncs on garbage and implausible lengths.</summary>
public sealed class SppFrameReader
{
    /// <summary>Largest payload accepted; anything bigger is treated as a corrupt header.</summary>
    public const int MaxPayload = 1_000_000;

    private readonly List<byte> _buf = [];

    /// <summary>Discards any partial frame (link dropped).</summary>
    public void Reset() => _buf.Clear();

    public List<byte[]> Feed(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes) _buf.Add(b);
        var frames = new List<byte[]>();
        while (true)
        {
            var start = _buf.IndexOf(0x01);
            if (start < 0) { _buf.Clear(); break; }
            if (start > 0) _buf.RemoveRange(0, start);
            if (_buf.Count < 5) break;

            var len = ((long)_buf[1] << 24) | ((long)_buf[2] << 16) | ((long)_buf[3] << 8) | _buf[4];
            if (len > MaxPayload) { _buf.RemoveAt(0); continue; } // corrupt header: skip this start byte

            var total = 5 + (int)len;
            if (_buf.Count < total + 1) break; // need payload and a look at the terminator byte

            frames.Add(_buf.GetRange(5, (int)len).ToArray());
            var terminated = _buf[total] == 0x04;
            _buf.RemoveRange(0, total + (terminated ? 1 : 0));
        }
        return frames;
    }
}
