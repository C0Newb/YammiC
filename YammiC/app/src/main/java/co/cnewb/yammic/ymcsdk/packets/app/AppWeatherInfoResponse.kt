package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.models.YmcWeatherInfo
import co.cnewb.yammic.ymcsdk.models.YmcWeatherTime
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Sends one weather slot to the CCU in response to a [co.cnewb.yammic.ymcsdk.packets.ccu.CcuWeatherRequest].
 * Send one packet per [YmcWeatherTime] slot. Must be preceded by [AppWeatherLocationResponse].
 * Wire: `[cmd 2B][requestId 1B][timeSlot 1B][weatherType 1B][temp*10 2B][maxTemp*10 2B][minTemp*10 2B][precipitation÷5 1B]`.
 * Note: precipitation is encoded as percentage÷5 (0–20 range).
 */
class AppWeatherInfoResponse(
    packetHandler: YmcPacketHandler,
    val timeSlot: YmcWeatherTime,
    val weather: YmcWeatherInfo,
) : AppOutgoingResponse(packetHandler, YmcCommandCode.WeatherInfo) {
    override val transport = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray =
        preamble +
            byteArrayOf(requestId, timeSlot.wireValue, weather.weatherType.wireValue) +
            shortBytes(temperatureTicks(weather.temperatureCelsius)) +
            shortBytes(temperatureTicks(weather.maxTemperatureCelsius)) +
            shortBytes(temperatureTicks(weather.minTemperatureCelsius)) +
            byteArrayOf((weather.precipitationPercent / 5).coerceIn(0, 20).toByte())

    private fun temperatureTicks(celsius: Double): Int =
        (celsius * 10).toInt().coerceIn(Short.MIN_VALUE.toInt(), Short.MAX_VALUE.toInt())
}
