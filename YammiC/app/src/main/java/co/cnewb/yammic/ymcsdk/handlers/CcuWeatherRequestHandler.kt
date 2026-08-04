package co.cnewb.yammic.ymcsdk.handlers

import co.cnewb.yammic.ymcsdk.YmcConnectionContext
import co.cnewb.yammic.ymcsdk.models.YmcWeatherProvider
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import co.cnewb.yammic.ymcsdk.packets.app.AppWeatherInfoResponse
import co.cnewb.yammic.ymcsdk.packets.app.AppWeatherLocationResponse
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuWeatherRequest

class CcuWeatherRequestHandler(
    private val weatherProvider: YmcWeatherProvider,
) : CcuRequestHandler<CcuWeatherRequest>() {
    override suspend fun invoke(
        packet: CcuWeatherRequest,
        packetHandler: YmcPacketHandler,
        connectionContext: YmcConnectionContext,
    ) {
        val weather = weatherProvider.getWeather(packet)
        AppWeatherLocationResponse(packetHandler, weather.locationName).send(connectionContext)
        for ((timeSlot, info) in weather.forecast) {
            AppWeatherInfoResponse(packetHandler, timeSlot, info).send(connectionContext)
        }
    }
}
