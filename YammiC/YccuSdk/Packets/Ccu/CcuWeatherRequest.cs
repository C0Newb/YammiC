using CNCO.YccuSdk.Models;

namespace CNCO.YccuSdk.Packets.Ccu;

/// <summary>
/// CCU is requesting weather data.
///
/// If <see cref="Type"/> is <see cref="YmcWeatherRequestType.Point"/>, then requesting weather for <see cref="Point"/>.
/// Otherwise, requesting weather for current phone location.
/// </summary>
public class CcuWeatherRequest : CcuIncomingRequest
{
    public CcuWeatherRequest(IYmcPacketHandler packetHandler, byte[] data)
        : base(YmcCommandCode.WeatherRequest, packetHandler, data)
    {
        Deserialize(data);
    }

    /// <summary>
    /// Location being request
    /// Almost always current, but if <see cref="YmcWeatherRequestType.Point"/> then <see cref="Point"/> will
    /// be the coordinates.
    /// </summary>
    public YmcWeatherRequestType Type { get; private set; } = YmcWeatherRequestType.Current;

    /// <summary>
    /// If <see cref="Type"/> is <see cref="YmcWeatherRequestType.Point"/>, request the weather for this point.
    /// </summary>
    public YmcLatLong? Point { get; private set; }

    protected override void Deserialize(byte[] data)
    {
        // Contains lat/long, doesn't specifically ask for current
        if (data.Length >= 11 && data[2] != (byte)YmcWeatherRequestType.Current)
        {
            Type = YmcWeatherRequestType.Point;

            // Point type: [cmd 2B][type 1B][lat 4B big-endian][lng 4B big-endian]
            var lat = (data[3] << 24) | (data[4] << 16) | (data[5] << 8) | data[6];
            var lng = (data[7] << 24) | (data[8] << 16) | (data[9] << 8) | data[10];
            Point = new YmcLatLong(lat, lng);
        }
        else
        {
            Type = YmcWeatherRequestType.Current;
        }
    }
}
