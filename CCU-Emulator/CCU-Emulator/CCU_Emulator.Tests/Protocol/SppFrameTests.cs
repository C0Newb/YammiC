using CCU_Emulator.Protocol;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Protocol;

public class SppFrameTests
{
    [Fact]
    public void EncodeWrapsPayload()
        => Assert.Equal(H("01 00000003 AABBCC 04"), SppFrame.Encode(H("AABBCC")));

    [Fact]
    public void ReaderYieldsFramesSplitAcrossFeeds()
    {
        var wire = Cat(SppFrame.Encode(H("0102")), SppFrame.Encode(H("0304050607")));
        var reader = new SppFrameReader();
        var frames = new List<byte[]>();
        foreach (var b in wire) frames.AddRange(reader.Feed([b]));
        Assert.Equal(2, frames.Count);
        Assert.Equal(H("0102"), frames[0]);
        Assert.Equal(H("0304050607"), frames[1]);
    }

    [Fact]
    public void ReaderResyncsPastGarbageAndHugeLength()
    {
        var reader = new SppFrameReader();
        // Garbage, then a header claiming ~4 GiB, then a good frame.
        var wire = Cat(H("7F7F"), H("01 FFFFFFFF"), SppFrame.Encode(H("AA")));
        var frames = reader.Feed(wire);
        Assert.Contains(frames, f => f.SequenceEqual(H("AA")));
    }

    [Fact]
    public void ReaderToleratesMissingTerminator()
    {
        var reader = new SppFrameReader();
        var frames = reader.Feed(H("01 00000002 AABB 01 00000001 CC 04"));
        Assert.Equal(H("AABB"), frames[0]);
    }
}
