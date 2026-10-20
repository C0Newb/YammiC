using CCU_Emulator.Protocol;
using CCU_Emulator.Tests.Support;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Emulation;

public class CcuSessionVpbTests
{
    private readonly SessionFixture _f = new();

    private static byte[] ChunkPacket(int contentId, int index, int totalIndex, int totalSize, byte[] data, bool badCrc = false)
        => new PacketWriter().U16(0x0235).I32(contentId).U8(index).I32(data.Length).U8(totalIndex).I32(totalSize)
            .U32(badCrc ? 0xDEADBEEF : VpbCrc32.Compute(data)).Bytes(data).ToArray();

    [Fact]
    public void BleConnectStartsTheSppLinkWhenEnabled()
    {
        _f.Phone.Connect();
        Assert.Equal(1, _f.Transport.SppConnectCalls);
    }

    [Fact]
    public void SppLinkIsNotStartedWhenDisabled()
    {
        _f.Settings.SppEnabled = false;
        _f.Phone.Connect();
        Assert.Equal(0, _f.Transport.SppConnectCalls);
    }

    [Fact]
    public void SppUpSendsTheBleConnectInformationFrame()
    {
        _f.Phone.Connect();
        _f.Transport.RaiseSpp(true);
        Assert.True(_f.Session.State.SppConnected);
        var expected = SppFrame.Encode(Cat(H("013A"), Ascii("01234567890123"), Ascii("012345")));
        Assert.Equal(expected, Assert.Single(_f.Transport.SppSent));
    }

    [Fact]
    public void RequestIsAcceptedWith0233()
    {
        _f.Phone.WriteSimple(H("0231"));
        Assert.Equal(H("0233"), Assert.Single(_f.Transport.MainRx));
        Assert.True(_f.Session.State.VpbReceiving);
    }

    [Fact]
    public void RequestIsRejectedWith0232WhenToggled()
    {
        _f.Settings.Behavior.RejectVpbRequests = true;
        _f.Phone.WriteSimple(H("0231"));
        Assert.Equal(H("0232"), Assert.Single(_f.Transport.MainRx));
        Assert.False(_f.Session.State.VpbReceiving);
    }

    [Fact]
    public void ChunksOverSppAreAckedOverBleAndTheImageIsStored()
    {
        _f.Phone.WriteSimple(H("0231"));
        _f.Phone.SendSpp(ChunkPacket(20101, 0, 1, 4, H("FFD8")));
        Assert.Equal(H("0236 00004E85 00 01"), _f.Transport.MainRx[^1]);
        Assert.Null(_f.Session.State.VpbImage);
        Assert.Equal(2, _f.Session.State.VpbReceivedBytes);
        Assert.Equal(4, _f.Session.State.VpbTotalBytes);

        _f.Phone.SendSpp(ChunkPacket(20101, 1, 1, 4, H("FFD9")));
        Assert.Equal(H("0236 00004E85 01 01"), _f.Transport.MainRx[^1]);
        Assert.Equal(H("FFD8FFD9"), _f.Session.State.VpbImage);
        Assert.False(_f.Session.State.VpbReceiving);
    }

    [Fact]
    public void BadCrcChunkGetsARetryAck()
    {
        _f.Phone.SendSpp(ChunkPacket(1, 0, 0, 2, H("FFD8"), badCrc: true));
        Assert.Equal(H("0236 00000001 00 02"), _f.Transport.MainRx[^1]);
        Assert.Null(_f.Session.State.VpbImage);
    }

    [Fact]
    public void SppFramesSplitAcrossReadsAreReassembled()
    {
        var framed = SppFrame.Encode(ChunkPacket(1, 0, 0, 2, H("FFD8")));
        foreach (var b in framed) _f.Transport.RaiseData(CCU_Emulator.Transport.CcuChannel.Spp, [b]);
        Assert.Equal(H("FFD8"), _f.Session.State.VpbImage);
    }

    [Fact]
    public void ClearRemovesTheImageAndAnswers0238()
    {
        _f.Phone.SendSpp(ChunkPacket(1, 0, 0, 2, H("FFD8")));
        _f.Phone.WriteSimple(H("0237"));
        Assert.Null(_f.Session.State.VpbImage);
        Assert.Equal(H("0238"), _f.Transport.MainRx[^1]);
    }

    // Review Focus 3: a corrupt header on the SPP stream must not hang or allocate the claimed size.
    [Fact]
    public void CorruptSppHeaderIsSkipped()
    {
        _f.Transport.RaiseData(CCU_Emulator.Transport.CcuChannel.Spp, H("01 FFFFFFFF"));
        _f.Phone.SendSpp(ChunkPacket(1, 0, 0, 2, H("FFD8")));
        Assert.Equal(H("FFD8"), _f.Session.State.VpbImage);
    }

    // Review Focus 5: dropping the link mid-image starts the next one clean.
    [Fact]
    public void SppDropMidImageResetsTheTransfer()
    {
        _f.Phone.SendSpp(ChunkPacket(1, 0, 2, 6, H("AABB")));
        Assert.True(_f.Session.State.VpbReceiving);

        _f.Transport.RaiseSpp(false);
        Assert.False(_f.Session.State.VpbReceiving);

        _f.Phone.SendSpp(ChunkPacket(1, 1, 2, 6, H("CCDD")));
        Assert.Equal(H("0236 00000001 01 03"), _f.Transport.MainRx[^1]);   // abort: this image never started
    }

    [Fact]
    public void BleDropMidImageResetsTheTransfer()
    {
        _f.Phone.Connect();
        _f.Phone.SendSpp(ChunkPacket(1, 0, 2, 6, H("AABB")));
        _f.Transport.RaiseBle(false);
        Assert.False(_f.Session.State.VpbReceiving);
        Assert.Equal(0, _f.Session.State.VpbReceivedBytes);
    }
}
