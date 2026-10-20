using CCU_Emulator.Protocol;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Protocol;

public class DdtReceiverTests
{
    private readonly List<string> _events = [];
    private readonly List<byte[]> _messages = [];
    private readonly DdtReceiver _ddt = new();

    public DdtReceiverTests()
    {
        _ddt.StatusNotify += s => _events.Add("status " + Convert.ToHexString(s));
        _ddt.MessageComplete += m => { _events.Add("message"); _messages.Add(m); };
    }

    [Fact]
    public void ReassemblesChunksAndSignalsInOrder()
    {
        _ddt.OnControl(H("05 000006"));
        _ddt.OnData(H("05 000000 AABBCC"));
        _ddt.OnData(H("05 000003 DDEEFF"));

        Assert.Equal(["status 0105", "message", "status 0000"], _events);
        Assert.Equal(H("AABBCCDDEEFF"), Assert.Single(_messages));
        Assert.False(_ddt.Active);
        Assert.Equal(H("0000"), _ddt.StatusValue);
    }

    [Fact]
    public void StatusValueShowsActiveSession()
    {
        _ddt.OnControl(H("07 000003"));
        Assert.True(_ddt.Active);
        Assert.Equal(H("0107"), _ddt.StatusValue);
    }

    [Fact]
    public void ChunksMayArriveOutOfOrder()
    {
        _ddt.OnControl(H("01 000004"));
        _ddt.OnData(H("01 000002 CCDD"));
        _ddt.OnData(H("01 000000 AABB"));
        Assert.Equal(H("AABBCCDD"), Assert.Single(_messages));
    }

    // Review Focus 2
    [Fact]
    public void DataWithNoSessionIsIgnored()
    {
        _ddt.OnData(H("05 000000 AABB"));
        Assert.Empty(_events);
    }

    [Fact]
    public void WrongRequestIdIsIgnored()
    {
        _ddt.OnControl(H("05 000002"));
        _ddt.OnData(H("06 000000 AABB"));
        Assert.Empty(_messages);
        Assert.True(_ddt.Active);
    }

    [Fact]
    public void ChunkPastDeclaredSizeIsIgnored()
    {
        _ddt.OnControl(H("05 000002"));
        _ddt.OnData(H("05 000001 AABBCC"));
        Assert.Empty(_messages);
        _ddt.OnData(H("05 000000 AABB"));
        Assert.Equal(H("AABB"), Assert.Single(_messages));
    }

    [Fact]
    public void DuplicateChunkDoesNotCompleteEarly()
    {
        _ddt.OnControl(H("05 000004"));
        _ddt.OnData(H("05 000000 AABB"));
        _ddt.OnData(H("05 000000 AABB"));
        Assert.Empty(_messages);
        _ddt.OnData(H("05 000002 CCDD"));
        Assert.Equal(H("AABBCCDD"), Assert.Single(_messages));
    }

    [Fact]
    public void NewSessionMidTransferDropsTheOldOne()
    {
        _ddt.OnControl(H("05 000004"));
        _ddt.OnData(H("05 000000 AABB"));
        _ddt.OnControl(H("06 000002"));
        _ddt.OnData(H("06 000000 1122"));
        Assert.Equal(H("1122"), Assert.Single(_messages));
    }

    [Theory]
    [InlineData("05")]
    [InlineData("05 000000")]       // zero size
    [InlineData("05 FFFFFF")]       // 16 MiB: over the cap
    public void BadControlWritesAreIgnored(string hex)
    {
        _ddt.OnControl(H(hex));
        Assert.False(_ddt.Active);
        Assert.Empty(_events);
    }

    [Fact]
    public void ShortDataWriteIsIgnored()
    {
        _ddt.OnControl(H("05 000002"));
        _ddt.OnData(H("05 00"));
        Assert.True(_ddt.Active);
    }
}
