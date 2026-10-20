using CCU_Emulator.Protocol;
using Xunit;

namespace CCU_Emulator.Tests.Protocol;

public class VpbCrc32Tests
{
    // Independent bit-by-bit reference of the variant described in docs/vpb.md:
    // IEEE-802.3 polynomial, register initialised to 0 (not 0xFFFFFFFF), final complement.
    private static uint Reference(byte[] data)
    {
        uint crc = 0;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    [Fact]
    public void EmptyInputIsAllOnes() => Assert.Equal(0xFFFFFFFFu, VpbCrc32.Compute([]));

    [Theory]
    [InlineData("00")]
    [InlineData("313233343536373839")]
    [InlineData("FFD8FFE000104A46494600010100000100010000")]
    public void MatchesBitwiseReference(string hex)
    {
        var data = Convert.FromHexString(hex);
        Assert.Equal(Reference(data), VpbCrc32.Compute(data));
    }

    [Fact]
    public void DiffersFromStandardCrc32()
    {
        // Standard CRC32("123456789") is 0xCBF43926; the variant must not produce it.
        Assert.NotEqual(0xCBF43926u, VpbCrc32.Compute("123456789"u8));
    }
}
