namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Asks the CCU which data IDs one server supports. Wire (simple write): `[cmd 2B: 0x0126][requestId 1B][server 2B big-endian]`.
/// </summary>
public class AppSupportIdListRequest : AppOutgoingRequest
{
    private readonly int _server;

    public AppSupportIdListRequest(IYmcPacketHandler packetHandler, int server)
        : base(packetHandler, YmcCommandCode.VehicleInformationSupportIdListRequest)
    {
        _server = server;
    }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) => Concat(Preamble, new[] { requestId }, ShortBytes(_server));
}
