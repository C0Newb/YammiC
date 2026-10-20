namespace CNCO.YccuSdk.Models;

/// <summary>
/// Weather conditions for a single <see cref="YmcWeatherTime"/> slot.
/// </summary>
/// <param name="WeatherType">Visual condition to display on the CCU.</param>
/// <param name="TemperatureCelsius">Current (or forecasted) temperature for the slot.</param>
/// <param name="MaxTemperatureCelsius">High temperature for the day containing this slot.</param>
/// <param name="MinTemperatureCelsius">Low temperature for the day containing this slot.</param>
/// <param name="PrecipitationPercent">
/// Probability of precipitation for the slot, 0-100. This is a chance-of-rain value, not an
/// accumulation amount. Use it to decide whether to show a rain indicator alongside the
/// <paramref name="WeatherType"/> icon; values of 30 or more are typically worth surfacing to the rider.
/// </param>
public record YmcWeatherInfo(
    YmcWeatherType WeatherType,
    double TemperatureCelsius,
    double MaxTemperatureCelsius,
    double MinTemperatureCelsius,
    int PrecipitationPercent);
