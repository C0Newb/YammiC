namespace CNCO.YccuSdk.Packets.App;

using CNCO.YccuSdk.Logging;

/// <summary>
/// Asks the CCU to start a Virtual Pit Board display sequence.
/// The CCU replies with either `VirtualPitBoardGraphicRequest` (proceed, send the graphic) or
/// `VirtualPitBoardResponseNg` (rejected).
/// Wire: `[cmd 2B]` - no payload. The reference SDK's `d.u` payload builder for this command
/// returns an empty byte array; the content ID is only sent later, with the graphic chunks.
/// </summary>
[SuppressUiLog(YmcCommandCode.VirtualPitBoardRequest)]
public class AppVirtualPitBoardRequest : AppOutgoingRequest
{
    public AppVirtualPitBoardRequest(IYmcPacketHandler packetHandler)
        : base(packetHandler, YmcCommandCode.VirtualPitBoardRequest)
    {
    }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) => Preamble;
}
