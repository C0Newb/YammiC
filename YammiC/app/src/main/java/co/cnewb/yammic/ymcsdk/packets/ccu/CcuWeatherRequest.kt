package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.models.YmcLatLong
import co.cnewb.yammic.ymcsdk.models.YmcWeatherRequestType
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * CCU is requesting weather data.
 *
 * If [type] is [YmcWeatherRequestType.Point], then requesting weather for [point].
 * Otherwise, requesting weather for current phone location.
 */
class CcuWeatherRequest(
    packetHandler: YmcPacketHandler,
    data: ByteArray,
) : CcuIncomingRequest(
        YmcCommandCode.WeatherRequest,
        packetHandler,
        data,
    ) {
    /**
     * Location being request
     * Almost always current, but if [YmcWeatherRequestType.Point] then [point] will
     * be the coordinates.
     */
    var type: YmcWeatherRequestType = YmcWeatherRequestType.Current
        private set

    /**
     * If [type] is [YmcWeatherRequestType.Point], request the weather for this point.
     */
    var point: YmcLatLong? = null
        private set

    init { deserialize(data) }

    override fun deserialize(data: ByteArray) {
        // Contains lat/long, doesn't specifically ask for current
        if (data.size >= 11 && data[2] != YmcWeatherRequestType.Current.code) {
            type = YmcWeatherRequestType.Point

            // Point type: [cmd 2B][type 1B][lat 4B big-endian][lng 4B big-endian]
            val lat =
                ((data[3].toInt() and 0xFF) shl 24) or ((data[4].toInt() and 0xFF) shl 16) or
                    ((data[5].toInt() and 0xFF) shl 8) or (data[6].toInt() and 0xFF)
            val long =
                ((data[7].toInt() and 0xFF) shl 24) or ((data[8].toInt() and 0xFF) shl 16) or
                    ((data[9].toInt() and 0xFF) shl 8) or (data[10].toInt() and 0xFF)
            point = YmcLatLong(lat, long)
        } else {
            type = YmcWeatherRequestType.Current
        }
    }
}
