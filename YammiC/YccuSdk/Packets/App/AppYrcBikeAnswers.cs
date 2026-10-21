using CNCO.YccuSdk.Yrc;

namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Tells the bike what became of the settings it just saved to the phone (its "export"). Wire:
/// <c>[0x0204][bike request id 1B][result 4B big-endian][name, at most 10 bytes of UTF-8]</c>, a simple write. Result 0 is OK.
/// </summary>
public class AppNotifySaveYrcSettingsResult : AppOutgoingResponse
{
    private readonly byte _bikeRequestId;
    private readonly int _result;
    private readonly string _name;

    public AppNotifySaveYrcSettingsResult(IYmcPacketHandler packetHandler, byte bikeRequestId, int result, string name)
        : base(packetHandler, YmcCommandCode.NotifySaveYrcSettingsDataResult)
    {
        _bikeRequestId = bikeRequestId;
        _result = result;
        _name = name;
    }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) =>
        Concat(Preamble, new[] { _bikeRequestId }, IntBytes(_result), StringBytes(_name, YrcCodec.NameMaxLength));
}

/// <summary>
/// The answer to the bike's request for the settings saved on the phone (its "import" list). Wire:
/// <c>[0x0206][length 3B][bike request id][protobuf]</c> as DDT v3 packets, with the bike's own request id.
/// </summary>
public class AppResponseSavedYrcSettings : AppOutgoingResponse
{
    private readonly byte _bikeRequestId;
    private readonly byte[] _proto;

    public AppResponseSavedYrcSettings(IYmcPacketHandler packetHandler, byte bikeRequestId, byte[] proto)
        : base(packetHandler, YmcCommandCode.ResponseSavedYrcSettingsData)
    {
        _bikeRequestId = bikeRequestId;
        _proto = proto;
    }

    public override Transport PacketTransport => Transport.DdtV3;

    public override byte[] Serialize(byte requestId) => Concat(Preamble, Int24(_proto.Length), new[] { _bikeRequestId }, _proto);
}
