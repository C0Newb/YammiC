namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Asks the CCU which ECU servers it can read data from. Wire (simple write): `[cmd 2B: 0x0103][requestId 1B]`.
/// </summary>
public class AppConnectServerListRequest : AppOutgoingRequest
{
    public AppConnectServerListRequest(IYmcPacketHandler packetHandler)
        : base(packetHandler, YmcCommandCode.ConnectServerListRequest)
    {
    }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) => Concat(Preamble, new[] { requestId });
}
