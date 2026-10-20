using CNCO.YccuSdk.Packets.Ccu;

namespace CNCO.YccuSdk.Models;

public record YmcWeatherData(
    string LocationName,
    IReadOnlyList<KeyValuePair<YmcWeatherTime, YmcWeatherInfo>> Forecast);

public interface IYmcWeatherProvider
{
    /// <summary>
    /// Used to get the response for a weather request.
    /// </summary>
    Task<YmcWeatherData> GetWeatherAsync(CcuWeatherRequest request);
}
