namespace CNCO.YccuSdk.Packets.App;

using CNCO.YccuSdk.Logging;

/// <summary>
/// Sends one chunk of Virtual Pit Board image data, following a `VirtualPitBoardGraphicRequest`.
/// Sent over the Classic-Bluetooth SPP link (see <see cref="AppOutgoingPacket.Transport.Spp"/>) - the CCU's
/// per-chunk ack (`SendVirtualPitBoardGraphicResponse`) still arrives over BLE, matching the
/// reference SDK's asymmetric transport for this command family.
///
/// Wire: `[cmd 2B][contentId 4B][index 1B][chunkSize 4B][totalIndex 1B][totalSize 4B][crc32 4B][chunkData]`.
/// The reference SDK's `e.a(f)` helper serializes its `f` (checksum) wrapper as the low 32 bits of
/// a custom CRC32 variant over the chunk bytes - not the chunk length. The CCU validates this and
/// silently returns `FailureRetry` for every chunk if it's wrong, regardless of chunk content.
/// </summary>
[SuppressUiLog(YmcCommandCode.SendVirtualPitBoardGraphic)]
public class AppSendVirtualPitBoardGraphic : AppOutgoingRequest
{
    /// <summary>
    /// Matches the reference SDK's `FileDataController.splitSize`.
    /// </summary>
    public const int ChunkSize = 6144;

    private readonly int _contentId;
    private readonly byte _index;
    private readonly byte _totalIndex;
    private readonly int _totalSize;
    private readonly byte[] _chunk;

    public AppSendVirtualPitBoardGraphic(
        IYmcPacketHandler packetHandler,
        int contentId,
        byte index,
        byte totalIndex,
        int totalSize,
        byte[] chunk)
        : base(packetHandler, YmcCommandCode.SendVirtualPitBoardGraphic)
    {
        _contentId = contentId;
        _index = index;
        _totalIndex = totalIndex;
        _totalSize = totalSize;
        _chunk = chunk;
    }

    public override Transport PacketTransport => Transport.Spp;

    public override byte[] Serialize(byte requestId) =>
        Concat(
            Preamble,
            IntBytes(_contentId),
            new[] { _index },
            IntBytes(_chunk.Length),
            new[] { _totalIndex },
            IntBytes(_totalSize),
            IntBytes(unchecked((int)Crc32(_chunk))),
            _chunk);

    /// <summary>
    /// Standard IEEE-802.3 CRC32 table (the Kotlin source spelled out all 256 entries; this generates the same table).
    /// </summary>
    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var bit = 0; bit < 8; bit++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            table[i] = c;
        }
        return table;
    }

    /// <summary>
    /// So, for whatever reason this custom CRC32 standard IEEE-802.3 polynomial table is needed,
    /// but initialized at 0 (not 0xFFFFFFFF) before the final complement.
    ///
    /// I don't know, Claude wrote it .. the packet works, so it'll stay.
    /// </summary>
    internal static uint Crc32(byte[] data)
    {
        uint crc = 0;
        foreach (var b in data)
        {
            var index = (crc ^ b) & 0xFF;
            crc = (crc >> 8) ^ Crc32Table[index];
        }
        return ~crc;
    }
}
