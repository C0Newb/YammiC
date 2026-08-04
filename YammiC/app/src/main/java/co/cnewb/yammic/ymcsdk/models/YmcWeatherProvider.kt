package co.cnewb.yammic.ymcsdk.models

import co.cnewb.yammic.ymcsdk.packets.ccu.CcuWeatherRequest

data class YmcWeatherData(
    val locationName: String,
    val forecast: List<Pair<YmcWeatherTime, YmcWeatherInfo>>,
)

interface YmcWeatherProvider {
    /**
     * Used to get the response for a weather request.
     */
    suspend fun getWeather(request: CcuWeatherRequest): YmcWeatherData
}
