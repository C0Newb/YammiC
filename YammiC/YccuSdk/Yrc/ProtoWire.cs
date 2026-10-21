using System.Text;

namespace CNCO.YccuSdk.Yrc;

/// <summary>
/// Writes the few protobuf shapes the ride-settings messages need (varints, strings, nested messages).
/// </summary>
internal sealed class ProtoWriter
{
    private readonly List<byte> _bytes = new();

    public ProtoWriter Varint(int field, long value)
    {
        Tag(field, 0);
        RawVarint(unchecked((ulong)value));
        return this;
    }

    public ProtoWriter Bool(int field, bool value) => value ? Varint(field, 1) : this;

    public ProtoWriter String(int field, string value)
    {
        if (value.Length == 0) return this;
        return Bytes(field, Encoding.UTF8.GetBytes(value));
    }

    public ProtoWriter Bytes(int field, byte[] value)
    {
        Tag(field, 2);
        RawVarint((ulong)value.Length);
        _bytes.AddRange(value);
        return this;
    }

    public ProtoWriter Packed(int field, IReadOnlyCollection<int> values)
    {
        if (values.Count == 0) return this;
        var inner = new ProtoWriter();
        foreach (var v in values) inner.RawVarint(unchecked((ulong)(long)v));
        return Bytes(field, inner.ToArray());
    }

    public byte[] ToArray() => _bytes.ToArray();

    private void Tag(int field, int wire) => RawVarint((ulong)((field << 3) | wire));

    private void RawVarint(ulong v)
    {
        while (v >= 0x80)
        {
            _bytes.Add((byte)(v | 0x80));
            v >>= 7;
        }
        _bytes.Add((byte)v);
    }
}

/// <summary>
/// Reads one protobuf message field by field. Unknown wire types stop the read.
/// </summary>
internal ref struct ProtoReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _pos;

    public ProtoReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _pos = 0;
    }

    /// <summary>
    /// Reads the next field. <paramref name="payload"/> is the bytes of a length-delimited field.
    /// </summary>
    public bool Next(out int field, out int wire, out long value, out ReadOnlySpan<byte> payload)
    {
        field = wire = 0;
        value = 0;
        payload = default;
        if (_pos >= _data.Length) return false;

        var tag = (int)RawVarint();
        field = tag >> 3;
        wire = tag & 7;
        switch (wire)
        {
            case 0:
                value = (long)RawVarint();
                return true;
            case 2:
                var length = (int)RawVarint();
                if (length < 0 || _pos + length > _data.Length) throw new FormatException("Field runs past the end");
                payload = _data.Slice(_pos, length);
                _pos += length;
                return true;
            case 1:
                _pos += 8;
                return _pos <= _data.Length ? true : throw new FormatException("Field runs past the end");
            case 5:
                _pos += 4;
                return _pos <= _data.Length ? true : throw new FormatException("Field runs past the end");
            default:
                throw new FormatException($"Unsupported wire type {wire}");
        }
    }

    /// <summary>
    /// Expands a packed repeated varint field.
    /// </summary>
    public static List<int> UnpackVarints(ReadOnlySpan<byte> packed)
    {
        var list = new List<int>();
        var reader = new ProtoReader(packed);
        while (reader._pos < packed.Length) list.Add((int)reader.RawVarint());
        return list;
    }

    private ulong RawVarint()
    {
        ulong result = 0;
        for (var shift = 0; shift < 70; shift += 7)
        {
            if (_pos >= _data.Length) throw new FormatException("Varint runs past the end");
            var b = _data[_pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
        }
        throw new FormatException("Varint too long");
    }
}
