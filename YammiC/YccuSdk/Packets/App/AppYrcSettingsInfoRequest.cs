using CNCO.YccuSdk.Yrc;

namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Asks the CCU for its ride-mode settings. Wire (simple write): `[cmd 2B: 0x0201][payloadLen 3B][requestId 1B][protobuf]`.
/// The answer (0x0202) arrives as a DDT session from the bike.
/// </summary>
public class AppYrcSettingsInfoRequest : AppOutgoingRequest
{
    private readonly byte[] _proto;

    public AppYrcSettingsInfoRequest(IYmcPacketHandler packetHandler, IReadOnlyCollection<int> kinds, IReadOnlyCollection<int> modes)
        : base(packetHandler, YmcCommandCode.RequestYrcSettingsInfo)
    {
        _proto = YrcCodec.BuildRequest(kinds, modes);
    }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) => Concat(Preamble, Int24(_proto.Length), new[] { requestId }, _proto);
}
