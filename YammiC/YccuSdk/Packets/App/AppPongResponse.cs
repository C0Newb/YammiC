namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Pong reply to a keepalive from the CCU.
/// </summary>
public class AppPongResponse : AppOutgoingResponse
{
    public AppPongResponse(IYmcPacketHandler packetHandler)
        : base(packetHandler, YmcCommandCode.PongResponse)
    {
    }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) => Concat(Preamble, new[] { requestId });
}
