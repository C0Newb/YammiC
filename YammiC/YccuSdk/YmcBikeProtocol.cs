namespace CNCO.YccuSdk;

internal static class YmcBikeProtocol
{
    // -- Service and characteristic UUIDs -------------------------------------

    public static readonly Guid ServiceUuid = Guid.Parse("AFA2CDF4-ECCF-46A7-A5EA-9DA428C0157A");

    /// <summary>
    /// Used with the DDT session-start handshake (announce total payload size).
    /// </summary>
    public static readonly Guid ControlUuid = Guid.Parse("56851826-A090-4F6A-A5DF-D1776520F74A");

    /// <summary>
    /// DDT status notifications arrive here (active ACK and completion signal).
    /// </summary>
    public static readonly Guid StatusUuid = Guid.Parse("75AB2788-87F6-4F61-BA9C-6F85A12600D1");

    /// <summary>
    /// DDT data chunks are written here.
    /// </summary>
    public static readonly Guid DataUuid = Guid.Parse("E998AEAB-EA8F-4063-83AA-2017C1A44484");

    /// <summary>
    /// Simple (non-DDT) outgoing commands are written here.
    /// Used for: weather response, language response, alive-check response, IdentifyAppRequest.
    /// If the CCU rejects writes to this UUID try <see cref="DataUuid"/> instead.
    /// </summary>
    public static readonly Guid SimpleWriteUuid = Guid.Parse("B606C7F9-E5A1-4E75-B313-2A920054A8EB");

    /// <summary>
    /// Most CCU to phone responses arrive as notifications on this characteristic.
    /// </summary>
    public static readonly Guid MainRxUuid = Guid.Parse("9C810D26-B605-4306-8C1C-755A1BA3066C");

    /// <summary>
    /// Bike-to-phone DDT v2 characteristic (notifications). The CCU answers CAN requests here (same framing as v3).
    /// </summary>
    public static readonly Guid DdtV2Uuid = Guid.Parse("E974B9A1-FA31-43C6-91AE-09931F3F6EF1");

    /// <summary>
    /// Bike-to-phone DDT v3 characteristic (indications): CAN responses arrive here.
    /// </summary>
    public static readonly Guid DdtV3Uuid = Guid.Parse("56DC2D58-CBE1-4BA9-A104-E9B8EB4DA95C");

    /// <summary>
    /// Phone-to-bike DDT v3 characteristic (written, not subscribed). The ride-mode messages (<c>LoadYrcSettingsData</c> and the
    /// saved-settings answers) travel this way instead of through the control/data DDT session: sent the other way the bike takes the
    /// bytes but never acts on them.
    /// </summary>
    public static readonly Guid DdtV3WriteUuid = Guid.Parse("7690EEF4-F3A1-11EC-B939-0242AC120002");

    /// <summary>
    /// Characteristics we enable notifications / indications on once connected.
    /// </summary>
    public static readonly Guid[] NotifyUuids =
    {
        Guid.Parse("9c810d26-b605-4306-8c1c-755a1ba3066c"), // main RX - most CCU responses
        Guid.Parse("75ab2788-87f6-4f61-ba9c-6f85a12600d1"), // DDT status
        Guid.Parse("77808451-e42f-4f6b-83ed-3ab44aa3d16a"), // DDT hi-bandwidth
        Guid.Parse("347456ee-a588-4dd5-bd98-62be3672e27a"), // DDT large
        Guid.Parse("e974b9a1-fa31-43c6-91ae-09931f3f6ef1"), // DDT v2
        Guid.Parse("ab8d233a-cc58-4932-b397-a3751911e021"), // YRC RX
        Guid.Parse("56dc2d58-cbe1-4ba9-a104-e9b8eb4da95c"), // DDT v3 (indication)
    };

    // -- App identity ---------------------------------------------------------

    /// <summary>
    /// Y-Connect's registered app ID (0x1234 = 4660). Use the same value.
    /// </summary>
    public const int AppId = 4660;

    // -- Helper Functions -----------------------------------------------------

    /// <summary>
    /// Maximum data bytes per chunk given the negotiated MTU.
    /// </summary>
    public static int PayloadChunkSize(int negotiatedMtu) => Math.Max(negotiatedMtu - 7, 1);

    /// <summary>
    /// First packet written to <see cref="ControlUuid"/> to announce a DDT session.
    /// Tells the CCU how many bytes are coming.
    /// </summary>
    public static byte[] SessionStartPacket(byte requestId, int payloadSize) =>
        new[]
        {
            requestId,
            (byte)((payloadSize >> 16) & 0xFF),
            (byte)((payloadSize >> 8) & 0xFF),
            (byte)(payloadSize & 0xFF),
        };

    /// <summary>
    /// Each data chunk written to <see cref="DataUuid"/> during a DDT session.
    /// Header: [requestId][offset high][offset mid][offset low].
    /// </summary>
    public static byte[] ChunkPacket(byte requestId, int offset, byte[] chunk) =>
        ConcatPackets(
            new[]
            {
                requestId,
                (byte)((offset >> 16) & 0xFF),
                (byte)((offset >> 8) & 0xFF),
                (byte)(offset & 0xFF),
            },
            chunk);

    /// <summary>
    /// Splits a message into the packets written to <see cref="DdtV3WriteUuid"/>:
    /// <c>[sequence u16 BE][index, from 1][count][data]</c>, with the same sequence in every packet of one message. Each packet holds
    /// at most <see cref="PayloadChunkSize"/> data bytes. They are written one at a time, each after the previous write completed.
    /// </summary>
    public static IReadOnlyList<byte[]> DdtV3WritePackets(ushort sequence, byte[] message, int negotiatedMtu)
    {
        var size = PayloadChunkSize(negotiatedMtu);
        var count = Math.Max(1, (message.Length + size - 1) / size);
        if (count > 255) throw new ArgumentException("Message too long for one DDT v3 transfer", nameof(message));

        var packets = new List<byte[]>(count);
        for (var i = 0; i < count; i++)
        {
            var data = message[(i * size)..Math.Min(message.Length, (i + 1) * size)];
            packets.Add(ConcatPackets(new[] { (byte)(sequence >> 8), (byte)(sequence & 0xFF), (byte)(i + 1), (byte)count }, data));
        }
        return packets;
    }

    /// <summary>
    /// Frames a command payload for the Classic-Bluetooth SPP socket: <c>[0x01][len 4B BE][payload][0x04]</c>.
    /// Mirrors the reference SDK's OTA-socket framing (also used for the initial ccuId/passKey handoff).
    /// </summary>
    public static byte[] SppFramePacket(byte[] payload) =>
        ConcatPackets(
            new byte[] { 0x01 },
            new[]
            {
                (byte)((payload.Length >> 24) & 0xFF),
                (byte)((payload.Length >> 16) & 0xFF),
                (byte)((payload.Length >> 8) & 0xFF),
                (byte)(payload.Length & 0xFF),
            },
            payload,
            new byte[] { 0x04 });

    public static byte[] ConcatPackets(params byte[][] arrays)
    {
        var total = arrays.Sum(a => a.Length);
        var output = new byte[total];
        var index = 0;
        foreach (var array in arrays)
        {
            array.CopyTo(output, index);
            index += array.Length;
        }
        return output;
    }
}
