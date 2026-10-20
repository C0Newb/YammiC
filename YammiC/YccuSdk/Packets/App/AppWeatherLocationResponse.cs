namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Sends the weather location name to the CCU before sending <see cref="AppWeatherInfoResponse"/> packets.
/// Wire: `[cmd 2B][requestId 1B][name UTF-8, max 21 bytes]`.
/// </summary>
public class AppWeatherLocationResponse : AppOutgoingResponse
{
    public AppWeatherLocationResponse(IYmcPacketHandler packetHandler, string locationName)
        : base(packetHandler, YmcCommandCode.WeatherLocation)
    {
        LocationName = locationName;
    }

    public string LocationName { get; }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) =>
        Concat(Preamble, new[] { requestId }, StringBytes(LocationName, maxBytes: 21));
}
