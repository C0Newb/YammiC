namespace CNCO.YccuSdk.Packets.Ccu;

using CNCO.YccuSdk.Logging;

/// <summary>
/// CCU's per-chunk acknowledgement for a `SendVirtualPitBoardGraphic` write.
/// Wire: `[cmd 2B][contentId 4B][index 1B][response 1B]` = 8 bytes.
/// </summary>
[SuppressUiLog(YmcCommandCode.SendVirtualPitBoardGraphicResponse)]
public class CcuSendVirtualPitBoardGraphicResponse : CcuIncomingResponse
{
    public CcuSendVirtualPitBoardGraphicResponse(IYmcPacketHandler packetHandler, byte[] data)
        : base(YmcCommandCode.SendVirtualPitBoardGraphicResponse, packetHandler, data)
    {
        Deserialize(data);
    }

    public enum ResponseType { Success, FailureRetry, FailureNotRetry, Unknown }

    public int ContentId { get; private set; }

    public byte Index { get; private set; }

    public ResponseType Response { get; private set; } = ResponseType.Unknown;

    protected override void Deserialize(byte[] data)
    {
        if (data.Length < 8)
        {
            throw new MalformedPacketException("SendVirtualPitBoardGraphicResponse is missing its body.");
        }
        ContentId =
            (data[2] << 24) |
            (data[3] << 16) |
            (data[4] << 8) |
            data[5];
        Index = data[6];
        Response = data[7] switch
        {
            1 => ResponseType.Success,
            2 => ResponseType.FailureRetry,
            3 => ResponseType.FailureNotRetry,
            _ => ResponseType.Unknown,
        };
    }
}
