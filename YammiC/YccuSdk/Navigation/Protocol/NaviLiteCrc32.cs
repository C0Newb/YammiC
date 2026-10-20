namespace CNCO.YccuSdk.Navigation.Protocol;

/// <summary>
/// CRC-32/MPEG-2: poly 0x04C11DB7, init 0xFFFFFFFF, no input/output reflection, xorout 0.
/// Ported from the Pillion reference app's Crc32Mpeg2. Pure function, no I/O.
/// </summary>
internal static class NaviLiteCrc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var entries = new uint[256];
        for (var index = 0; index < 256; index++)
        {
            var crc = (uint)index << 24;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & (1u << 31)) != 0 ? (crc << 1) ^ 0x04C11DB7u : crc << 1;
            }
            entries[index] = crc;
        }
        return entries;
    }

    public static uint Compute(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc = (crc << 8) ^ Table[((crc >> 24) ^ b) & 0xFF];
        }
        return crc;
    }
}
