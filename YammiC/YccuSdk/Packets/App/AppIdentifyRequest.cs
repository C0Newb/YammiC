namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Identifies the app to the CCU.
/// YC: `IdentifyAppRequest`
/// </summary>
public class AppIdentifyRequest : AppOutgoingRequest
{
    private readonly int _appId;

    public AppIdentifyRequest(IYmcPacketHandler packetHandler, int? appId = 4660)
        : base(packetHandler, YmcCommandCode.IdentifyAppRequest)
    {
        _appId = appId ?? 4660;
    }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) => Concat(Preamble, new[] { requestId }, IntBytes(_appId));
}
