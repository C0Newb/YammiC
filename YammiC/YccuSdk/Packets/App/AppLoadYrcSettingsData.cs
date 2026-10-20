using CNCO.YccuSdk.Yrc;

namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Gives the CCU ride-mode settings to apply. Wire (DDT): `[cmd 2B: 0x0208][payloadLen 3B][requestId 1B][protobuf]`.
/// The bike answers with 0x0209.
/// </summary>
public class AppLoadYrcSettingsData : AppOutgoingRequest
{
    private readonly byte[] _proto;

    public AppLoadYrcSettingsData(IYmcPacketHandler packetHandler, IEnumerable<YrcProfile> profiles)
        : base(packetHandler, YmcCommandCode.LoadYrcSettingsData)
    {
        _proto = YrcCodec.BuildLoad(profiles);
    }

    public override Transport PacketTransport => Transport.Ddt;

    public override byte[] Serialize(byte requestId) => Concat(Preamble, Int24(_proto.Length), new[] { requestId }, _proto);
}
