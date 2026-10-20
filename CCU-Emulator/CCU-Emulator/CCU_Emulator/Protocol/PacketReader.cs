using System.Text;

namespace CCU_Emulator.Protocol;

/// <summary>Bounds-checked big-endian reader. Never reads past the end; throws <see cref="MalformedPacketException"/>.</summary>
public sealed class PacketReader(byte[] data)
{
    private int _pos;

    public int Position => _pos;
    public int Remaining => data.Length - _pos;

    public byte U8()
    {
        Need(1);
        return data[_pos++];
    }

    public int U16() => (U8() << 8) | U8();

    public int U24() => (U8() << 16) | (U8() << 8) | U8();

    public uint U32() => ((uint)U8() << 24) | ((uint)U8() << 16) | ((uint)U8() << 8) | U8();

    public int I32() => unchecked((int)U32());

    public short I16() => unchecked((short)U16());

    public long I64()
    {
        long v = 0;
        for (var i = 0; i < 8; i++) v = (v << 8) | U8();
        return v;
    }

    /// <summary>Protobuf base-128 varint.</summary>
    public ulong Varint()
    {
        ulong result = 0;
        var shift = 0;
        while (true)
        {
            var b = U8();
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
            if (shift > 63) throw new MalformedPacketException("varint too long");
        }
    }

    public byte[] Bytes(int n)
    {
        if (n < 0) throw new MalformedPacketException($"negative length {n}");
        Need(n);
        var result = data.AsSpan(_pos, n).ToArray();
        _pos += n;
        return result;
    }

    public string Utf8(int n) => Encoding.UTF8.GetString(Bytes(n));

    private void Need(int n)
    {
        if (n > Remaining)
            throw new MalformedPacketException($"need {n} byte(s) at offset {_pos}, only {Remaining} left");
    }
}
