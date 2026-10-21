namespace CNCO.YccuSdk.Packets.Ccu;

using CNCO.YccuSdk.Logging;

/// <summary>
/// CCU reports it could not clear the Virtual Pit Board display. No payload beyond the command code.
/// </summary>
[SuppressUiLog(YmcCommandCode.ClearVirtualPitBoardResponseNg)]
public class CcuClearVirtualPitBoardResponseNg : CcuIncomingResponse
{
    public CcuClearVirtualPitBoardResponseNg(IYmcPacketHandler packetHandler, byte[] data)
        : base(YmcCommandCode.ClearVirtualPitBoardResponseNg, packetHandler, data)
    {
        Deserialize(data);
    }

    protected override void Deserialize(byte[] data)
    {
        // No payload.
    }
}

/// <summary>
/// CCU confirms the Virtual Pit Board display was cleared. No payload beyond the command code.
/// </summary>
[SuppressUiLog(YmcCommandCode.ClearVirtualPitBoardResponseOk)]
public class CcuClearVirtualPitBoardResponseOk : CcuIncomingResponse
{
    public CcuClearVirtualPitBoardResponseOk(IYmcPacketHandler packetHandler, byte[] data)
        : base(YmcCommandCode.ClearVirtualPitBoardResponseOk, packetHandler, data)
    {
        Deserialize(data);
    }

    protected override void Deserialize(byte[] data)
    {
        // No payload.
    }
}
