namespace CCU_Emulator.Protocol;

/// <summary>
/// The CRC32 variant the CCU expects on VPB graphic chunks (docs/vpb.md): the IEEE-802.3
/// polynomial table, but with the register initialised to 0 and a final bitwise complement.
/// </summary>
public static class VpbCrc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0;
        foreach (var b in data) crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        return ~crc;
    }
}
