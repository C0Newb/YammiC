namespace CNCO.YccuSdk.Packets.App;

using CNCO.YccuSdk.Logging;

/// <summary>
/// Tells the CCU we can't produce the graphic it just requested via `VirtualPitBoardGraphicRequest`.
/// No payload - matches the reference SDK's empty-body encoder.
/// </summary>
[SuppressUiLog(YmcCommandCode.VirtualPitBoardGraphicResponseNg)]
public class AppVirtualPitBoardGraphicResponseNg : AppOutgoingResponse
{
    public AppVirtualPitBoardGraphicResponseNg(IYmcPacketHandler packetHandler)
        : base(packetHandler, YmcCommandCode.VirtualPitBoardGraphicResponseNg)
    {
    }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) => Preamble;
}
