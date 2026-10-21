namespace CNCO.YccuSdk.Packets.Ccu;

using CNCO.YccuSdk.Logging;

/// <summary>
/// CCU rejected our `VirtualPitBoardRequest`. No payload beyond the command code.
/// </summary>
[SuppressUiLog(YmcCommandCode.VirtualPitBoardResponseNg)]
public class CcuVirtualPitBoardResponseNg : CcuIncomingResponse
{
    public CcuVirtualPitBoardResponseNg(IYmcPacketHandler packetHandler, byte[] data)
        : base(YmcCommandCode.VirtualPitBoardResponseNg, packetHandler, data)
    {
        Deserialize(data);
    }

    protected override void Deserialize(byte[] data)
    {
        // No payload.
    }
}
