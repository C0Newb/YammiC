using CNCO.YccuSdk.Yrc;

namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Gives the CCU ride-mode settings to apply. Wire: `[cmd 2B: 0x0208][payloadLen 3B][requestId 1B][protobuf]`, sent as DDT v3
/// packets to the v3 write characteristic (the control/data DDT session is accepted by the bike but ignored for this command).
/// The bike answers with 0x0209.
/// </summary>
public class AppLoadYrcSettingsData : AppOutgoingRequest
{
    private readonly byte[] _proto;
    private readonly byte? _bikeRequestId;

    public AppLoadYrcSettingsData(IYmcPacketHandler packetHandler, IEnumerable<YrcProfile> profiles)
        : base(packetHandler, YmcCommandCode.LoadYrcSettingsData)
    {
        _proto = YrcCodec.BuildLoad(profiles);
    }

    /// <summary>
    /// A load the bike asked for (from its menu): carries the bike's own request id and an already built protobuf.
    /// </summary>
    public AppLoadYrcSettingsData(IYmcPacketHandler packetHandler, byte bikeRequestId, byte[] proto)
        : base(packetHandler, YmcCommandCode.LoadYrcSettingsData)
    {
        _bikeRequestId = bikeRequestId;
        _proto = proto;
    }

    public override Transport PacketTransport => Transport.DdtV3;

    public override byte[] Serialize(byte requestId) => Concat(Preamble, Int24(_proto.Length), new[] { _bikeRequestId ?? requestId }, _proto);
}
