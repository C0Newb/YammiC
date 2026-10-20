namespace CNCO.YccuSdk.Packets.App;

using CNCO.YccuSdk.Logging;

/// <summary>
/// Asks the CCU to clear the Virtual Pit Board display.
/// The CCU replies with `ClearVirtualPitBoardResponseOk` or `ClearVirtualPitBoardResponseNg`.
/// No payload - matches the reference SDK's empty-body encoder.
/// </summary>
[SuppressUiLog(YmcCommandCode.ClearVirtualPitBoard)]
public class AppClearVirtualPitBoard : AppOutgoingRequest
{
    public AppClearVirtualPitBoard(IYmcPacketHandler packetHandler)
        : base(packetHandler, YmcCommandCode.ClearVirtualPitBoard)
    {
    }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) => Preamble;
}
