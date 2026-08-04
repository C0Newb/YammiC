package co.cnewb.yammic.ymcsdk.models

/**
 * Weather conditions for a single [YmcWeatherTime] slot.
 *
 * @property weatherType Visual condition to display on the CCU.
 * @property temperatureCelsius Current (or forecasted) temperature for the slot.
 * @property maxTemperatureCelsius High temperature for the day containing this slot.
 * @property minTemperatureCelsius Low temperature for the day containing this slot.
 * @property precipitationPercent Probability of precipitation for the slot, 0–100.
 *   This is a chance-of-rain value, not an accumulation amount. Use it to decide whether
 *   to show a rain indicator alongside the [weatherType] icon; values ≥ 30 are typically
 *   worth surfacing to the rider.
 */
class YmcWeatherInfo(
    val weatherType: YmcWeatherType,
    val temperatureCelsius: Double,
    val maxTemperatureCelsius: Double,
    val minTemperatureCelsius: Double,
    val precipitationPercent: Int,
)
