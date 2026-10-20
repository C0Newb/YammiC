using CCU_Emulator.Protocol;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Protocol;

public class PacketReaderWriterTests
{
    [Fact]
    public void ReaderReadsBigEndianIntegers()
    {
        var r = new PacketReader(H("01 0203 040506 07080910 FFFE"));
        Assert.Equal(1, r.U8());
        Assert.Equal(0x0203, r.U16());
        Assert.Equal(0x040506, r.U24());
        Assert.Equal(0x07080910, r.I32());
        Assert.Equal((short)-2, r.I16());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void ReaderReadsInt64AndVarint()
    {
        var r = new PacketReader(H("00 00 00 00 65 00 00 00  E9 0F"));
        Assert.Equal(0x65000000L, r.I64());
        Assert.Equal(2025UL, r.Varint());
    }

    [Fact]
    public void ReaderThrowsMalformedWhenOutOfBytes()
    {
        var r = new PacketReader(H("01"));
        r.U8();
        Assert.Throws<MalformedPacketException>(() => r.U8());
        Assert.Throws<MalformedPacketException>(() => new PacketReader(H("01")).Bytes(5));
        Assert.Throws<MalformedPacketException>(() => new PacketReader(H("01")).Bytes(-1));
        Assert.Throws<MalformedPacketException>(() => new PacketReader(H("FF FF FF FF FF FF FF FF FF FF FF")).Varint());
    }

    [Fact]
    public void Utf8LengthLargerThanRemainingIsMalformed()
    {
        var r = new PacketReader(Cat(H("0A"), Ascii("abc")));
        Assert.Throws<MalformedPacketException>(() => r.Utf8(r.U8()));
    }

    [Fact]
    public void WriterRoundTrips()
    {
        var bytes = new PacketWriter().U8(1).U16(0x0203).U24(0x040506).I32(0x07080910).Utf8("hi").ToArray();
        Assert.Equal(H("01 0203 040506 07080910 6869"), bytes);
    }
}
