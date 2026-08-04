package co.cnewb.yammic.bike

import co.cnewb.yammic.ymcsdk.models.YmcWeatherData
import co.cnewb.yammic.ymcsdk.models.YmcWeatherInfo
import co.cnewb.yammic.ymcsdk.models.YmcWeatherProvider
import co.cnewb.yammic.ymcsdk.models.YmcWeatherTime
import co.cnewb.yammic.ymcsdk.models.YmcWeatherType
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuWeatherRequest

class DemoWeatherProvider : YmcWeatherProvider {
    override suspend fun getWeather(request: CcuWeatherRequest): YmcWeatherData =
        YmcWeatherData(
            locationName = "Fuckbeckistan, NJ or whatever",
            forecast =
                listOf(
                    YmcWeatherTime.Current to
                        YmcWeatherInfo(
                            weatherType = YmcWeatherType.Tornado,
                            temperatureCelsius = 22.0,
                            maxTemperatureCelsius = 60.0,
                            minTemperatureCelsius = -20.0,
                            precipitationPercent = 125,
                        ),
                    YmcWeatherTime.After1Hour to
                        YmcWeatherInfo(
                            weatherType = YmcWeatherType.Smog,
                            temperatureCelsius = 55.5,
                            maxTemperatureCelsius = 58.0,
                            minTemperatureCelsius = 50.0,
                            precipitationPercent = 5,
                        ),
                    YmcWeatherTime.After2Hours to
                        YmcWeatherInfo(
                            weatherType = YmcWeatherType.Sunny,
                            temperatureCelsius = 42.0,
                            maxTemperatureCelsius = 45.0,
                            minTemperatureCelsius = 37.0,
                            precipitationPercent = 45,
                        ),
                    YmcWeatherTime.After3Hours to
                        YmcWeatherInfo(
                            weatherType = YmcWeatherType.SnowHeavy,
                            temperatureCelsius = -12.0,
                            maxTemperatureCelsius = -10.0,
                            minTemperatureCelsius = -15.0,
                            precipitationPercent = 100,
                        ),
                    YmcWeatherTime.After1Day to
                        YmcWeatherInfo(
                            weatherType = YmcWeatherType.CloudyDay,
                            temperatureCelsius = 16.0,
                            maxTemperatureCelsius = 18.0,
                            minTemperatureCelsius = 11.0,
                            precipitationPercent = 30,
                        ),
                    YmcWeatherTime.After2Days to
                        YmcWeatherInfo(
                            weatherType = YmcWeatherType.RainLight,
                            temperatureCelsius = 14.0,
                            maxTemperatureCelsius = 16.0,
                            minTemperatureCelsius = 10.0,
                            precipitationPercent = 70,
                        ),
                    YmcWeatherTime.After3Days to
                        YmcWeatherInfo(
                            weatherType = YmcWeatherType.Clear,
                            temperatureCelsius = 20.0,
                            maxTemperatureCelsius = 23.0,
                            minTemperatureCelsius = 14.0,
                            precipitationPercent = 0,
                        ),
                    YmcWeatherTime.After4Days to
                        YmcWeatherInfo(
                            weatherType = YmcWeatherType.PartlyCloudyDay,
                            temperatureCelsius = 17.0,
                            maxTemperatureCelsius = 20.0,
                            minTemperatureCelsius = 12.0,
                            precipitationPercent = 20,
                        ),
                    YmcWeatherTime.After5Days to
                        YmcWeatherInfo(
                            weatherType = YmcWeatherType.Hail,
                            temperatureCelsius = 22.0,
                            maxTemperatureCelsius = 25.0,
                            minTemperatureCelsius = 15.0,
                            precipitationPercent = 85,
                        ),
                    YmcWeatherTime.After6Days to
                        YmcWeatherInfo(
                            weatherType = YmcWeatherType.RainSnowMixHeavy,
                            temperatureCelsius = 0.0,
                            maxTemperatureCelsius = 3276.0,
                            minTemperatureCelsius = -3276.0,
                            precipitationPercent = 95,
                        ),
                ),
        )
}
