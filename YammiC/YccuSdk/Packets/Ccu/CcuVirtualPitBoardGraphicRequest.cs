namespace CNCO.YccuSdk.Packets.Ccu;

using CNCO.YccuSdk.Logging;

/// <summary>
/// CCU is ready and asks us to send the Virtual Pit Board graphic queued by our
/// `VirtualPitBoardRequest`. We should follow up with chunked `SendVirtualPitBoardGraphic` packets
/// (or `VirtualPitBoardGraphicResponseNg` if we can't produce it). No payload beyond the command code.
/// </summary>
[SuppressUiLog(YmcCommandCode.VirtualPitBoardGraphicRequest)]
public class CcuVirtualPitBoardGraphicRequest : CcuIncomingRequest
{
    public CcuVirtualPitBoardGraphicRequest(IYmcPacketHandler packetHandler, byte[] data)
        : base(YmcCommandCode.VirtualPitBoardGraphicRequest, packetHandler, data)
    {
        Deserialize(data);
    }

    protected override void Deserialize(byte[] data)
    {
        // No payload.
    }
}
