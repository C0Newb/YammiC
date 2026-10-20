using CCU_Emulator.Emulation;
using CCU_Emulator.Protocol;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Emulation;

public class VpbReceiverTests
{
    private readonly VpbReceiver _vpb = new();

    private static VpbChunkMessage Chunk(int contentId, int index, int totalIndex, int totalSize, byte[] data, bool badCrc = false)
        => new(contentId, (byte)index, (byte)totalIndex, totalSize, badCrc ? VpbCrc32.Compute(data) + 1 : VpbCrc32.Compute(data), data);

    [Fact]
    public void SingleChunkImageCompletes()
    {
        var (response, image) = _vpb.Accept(Chunk(1, 0, 0, 3, H("FFD8FF")));
        Assert.Equal(ChunkResponse.Ok, response);
        Assert.Equal(H("FFD8FF"), image);
        Assert.False(_vpb.InProgress);
    }

    [Fact]
    public void MultiChunkImageIsConcatenatedInOrder()
    {
        Assert.Equal((ChunkResponse.Ok, (byte[]?)null), _vpb.Accept(Chunk(1, 0, 2, 6, H("AABB"))));
        Assert.True(_vpb.InProgress);
        Assert.Equal(2, _vpb.ReceivedBytes);
        Assert.Equal(6, _vpb.TotalSize);
        Assert.Equal((ChunkResponse.Ok, (byte[]?)null), _vpb.Accept(Chunk(1, 1, 2, 6, H("CCDD"))));
        var (response, image) = _vpb.Accept(Chunk(1, 2, 2, 6, H("EEFF")));
        Assert.Equal(ChunkResponse.Ok, response);
        Assert.Equal(H("AABBCCDDEEFF"), image);
    }

    [Fact]
    public void BadCrcAsksForARetryAndKeepsState()
    {
        _vpb.Accept(Chunk(1, 0, 1, 4, H("AABB")));
        Assert.Equal(ChunkResponse.Retry, _vpb.Accept(Chunk(1, 1, 1, 4, H("CCDD"), badCrc: true)).Response);
        var (response, image) = _vpb.Accept(Chunk(1, 1, 1, 4, H("CCDD")));
        Assert.Equal(ChunkResponse.Ok, response);
        Assert.Equal(H("AABBCCDD"), image);
    }

    [Fact]
    public void ResendOfThePreviousChunkIsAcknowledgedWithoutDuplicatingData()
    {
        _vpb.Accept(Chunk(1, 0, 1, 4, H("AABB")));
        Assert.Equal(ChunkResponse.Ok, _vpb.Accept(Chunk(1, 0, 1, 4, H("AABB"))).Response);
        Assert.Equal(H("AABBCCDD"), _vpb.Accept(Chunk(1, 1, 1, 4, H("CCDD"))).Image);
    }

    [Fact]
    public void SkippedChunkAborts()
    {
        _vpb.Accept(Chunk(1, 0, 2, 6, H("AABB")));
        Assert.Equal(ChunkResponse.Abort, _vpb.Accept(Chunk(1, 2, 2, 6, H("EEFF"))).Response);
        Assert.False(_vpb.InProgress);
    }

    [Fact]
    public void ChunkOfAnUnknownImageThatIsNotTheFirstAborts()
        => Assert.Equal(ChunkResponse.Abort, _vpb.Accept(Chunk(9, 1, 2, 6, H("AABB"))).Response);

    [Fact]
    public void DataBeyondTheDeclaredTotalAborts()
    {
        _vpb.Accept(Chunk(1, 0, 1, 3, H("AABB")));
        Assert.Equal(ChunkResponse.Abort, _vpb.Accept(Chunk(1, 1, 1, 3, H("CCDD"))).Response);
    }

    [Fact]
    public void LastChunkWithTheWrongTotalAborts()
        => Assert.Equal(ChunkResponse.Abort, _vpb.Accept(Chunk(1, 0, 0, 5, H("AABB"))).Response);

    [Fact]
    public void NewContentIdRestartsTheTransfer()
    {
        _vpb.Accept(Chunk(1, 0, 2, 6, H("AABB")));
        var (response, image) = _vpb.Accept(Chunk(2, 0, 0, 2, H("1122")));
        Assert.Equal(ChunkResponse.Ok, response);
        Assert.Equal(H("1122"), image);
    }
}
