package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Sends the weather location name to the CCU before sending [AppWeatherInfoResponse] packets.
 * Wire: `[cmd 2B][requestId 1B][name UTF-8, max 21 bytes]`.
 */
class AppWeatherLocationResponse(
    packetHandler: YmcPacketHandler,
    val locationName: String,
) : AppOutgoingResponse(packetHandler, YmcCommandCode.WeatherLocation) {
    override val transport = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray =
        preamble + byteArrayOf(requestId) + stringBytes(locationName, maxBytes = 21)
}
