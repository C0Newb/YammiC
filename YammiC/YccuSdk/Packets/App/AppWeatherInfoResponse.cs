using CNCO.YccuSdk.Models;

namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Sends one weather slot to the CCU in response to a <see cref="Ccu.CcuWeatherRequest"/>.
/// Send one packet per <see cref="YmcWeatherTime"/> slot. Must be preceded by <see cref="AppWeatherLocationResponse"/>.
/// Wire: `[cmd 2B][requestId 1B][timeSlot 1B][weatherType 1B][temp*10 2B][maxTemp*10 2B][minTemp*10 2B][precipitation/5 1B]`.
/// Note: precipitation is encoded as percentage/5 (0-20 range).
/// </summary>
public class AppWeatherInfoResponse : AppOutgoingResponse
{
    public AppWeatherInfoResponse(IYmcPacketHandler packetHandler, YmcWeatherTime timeSlot, YmcWeatherInfo weather)
        : base(packetHandler, YmcCommandCode.WeatherInfo)
    {
        TimeSlot = timeSlot;
        Weather = weather;
    }

    public YmcWeatherTime TimeSlot { get; }
    public YmcWeatherInfo Weather { get; }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) =>
        Concat(
            Preamble,
            new[] { requestId, (byte)TimeSlot, (byte)Weather.WeatherType },
            ShortBytes(TemperatureTicks(Weather.TemperatureCelsius)),
            ShortBytes(TemperatureTicks(Weather.MaxTemperatureCelsius)),
            ShortBytes(TemperatureTicks(Weather.MinTemperatureCelsius)),
            new[] { (byte)Math.Clamp(Weather.PrecipitationPercent / 5, 0, 20) });

    private static int TemperatureTicks(double celsius) =>
        (int)Math.Clamp((int)(celsius * 10), short.MinValue, short.MaxValue);
}
