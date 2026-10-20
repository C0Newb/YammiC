using System.Text;

namespace CCU_Emulator.Protocol;

/// <summary>Big-endian packet builder.</summary>
public sealed class PacketWriter
{
    private readonly List<byte> _buf = [];

    public PacketWriter U8(int v) { _buf.Add((byte)v); return this; }
    public PacketWriter U16(int v) { _buf.Add((byte)(v >> 8)); _buf.Add((byte)v); return this; }
    public PacketWriter U24(int v) { _buf.Add((byte)(v >> 16)); _buf.Add((byte)(v >> 8)); _buf.Add((byte)v); return this; }
    public PacketWriter I32(int v) => U32(unchecked((uint)v));
    public PacketWriter U32(uint v)
    {
        _buf.Add((byte)(v >> 24)); _buf.Add((byte)(v >> 16)); _buf.Add((byte)(v >> 8)); _buf.Add((byte)v);
        return this;
    }
    public PacketWriter Bytes(byte[] b) { _buf.AddRange(b); return this; }
    public PacketWriter Utf8(string s) => Bytes(Encoding.UTF8.GetBytes(s));
    public byte[] ToArray() => _buf.ToArray();
}
