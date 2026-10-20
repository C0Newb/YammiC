using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.App;
using CNCO.YccuSdk.Packets.Ccu;

namespace CNCO.YccuSdk.Handlers;

public class CcuWeatherRequestHandler : CcuRequestHandler<CcuWeatherRequest>
{
    private readonly IYmcWeatherProvider _weatherProvider;

    public CcuWeatherRequestHandler(IYmcWeatherProvider weatherProvider)
    {
        _weatherProvider = weatherProvider;
    }

    public override async Task InvokeAsync(
        CcuWeatherRequest packet,
        IYmcPacketHandler packetHandler,
        YmcConnectionContext connectionContext)
    {
        var weather = await _weatherProvider.GetWeatherAsync(packet).ConfigureAwait(false);
        await new AppWeatherLocationResponse(packetHandler, weather.LocationName).SendAsync(connectionContext).ConfigureAwait(false);
        foreach (var (timeSlot, info) in weather.Forecast)
        {
            await new AppWeatherInfoResponse(packetHandler, timeSlot, info).SendAsync(connectionContext).ConfigureAwait(false);
        }
    }
}
