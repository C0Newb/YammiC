using Xunit;

namespace CNCO.YccuSdk.Tests;

public class DdtV3WriteTests
{
    [Fact]
    public void ASmallMessage_IsOnePacketWithTheHeader()
    {
        var packets = YmcBikeProtocol.DdtV3WritePackets(0x0102, new byte[] { 9, 8, 7 }, 183);
        var packet = Assert.Single(packets);
        Assert.Equal(new byte[] { 0x01, 0x02, 1, 1, 9, 8, 7 }, packet);
    }

    [Fact]
    public void ALongMessage_IsSplitInOrder_WithTheSameSequenceInEveryPacket()
    {
        var message = Enumerable.Range(0, 400).Select(i => (byte)i).ToArray();
        var packets = YmcBikeProtocol.DdtV3WritePackets(7, message, 183); // 176 data bytes per packet

        Assert.Equal(3, packets.Count);
        for (var i = 0; i < packets.Count; i++)
        {
            Assert.Equal(new byte[] { 0, 7, (byte)(i + 1), 3 }, packets[i][..4]);
            Assert.True(packets[i].Length <= 183 - 3);
        }
        Assert.Equal(message, packets.SelectMany(p => p[4..]).ToArray());
    }

    [Fact]
    public void ALoadMessage_StartsWithTheCommandAndLength()
    {
        // The same logical message the DDT path used: [0x0208][len 3B][requestId][protobuf].
        var body = new byte[] { 0x02, 0x08, 0x00, 0x00, 0x03, 0x6F, 0x0A, 0x00 };
        var packet = Assert.Single(YmcBikeProtocol.DdtV3WritePackets(1, body, 183));
        Assert.Equal(body, packet[4..]);
    }
}
