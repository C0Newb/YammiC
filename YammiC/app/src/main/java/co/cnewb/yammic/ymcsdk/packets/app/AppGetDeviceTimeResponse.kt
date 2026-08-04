package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * VehicleSettingDatetime (cmd 0x000A) — sync the CCU clock.
 * `[cmd 2B][requestId 1B][time 8B big-endian seconds]`.
 *
 * The CCU expects Unix time in SECONDS (not milliseconds), adjusted by timezone offset.
 * Official app sends: (currentTimeMillis + timezone_offset_ms) / 1000
 */
class AppGetDeviceTimeResponse(
    packetHandler: YmcPacketHandler,
    private val currentTimeMillis: Long = System.currentTimeMillis(),
) : AppOutgoingResponse(packetHandler, YmcCommandCode.GetDatetimeResponse) {
    override val transport: Transport
        get() = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray {
        val calendar = java.util.Calendar.getInstance()
        val timezoneOffsetMs = calendar.get(java.util.Calendar.ZONE_OFFSET) + calendar.get(java.util.Calendar.DST_OFFSET)
        val timeInSeconds = (currentTimeMillis + timezoneOffsetMs) / 1000

        return preamble + byteArrayOf(requestId) +
            byteArrayOf(
                ((timeInSeconds shr 56) and 0xFF).toByte(),
                ((timeInSeconds shr 48) and 0xFF).toByte(),
                ((timeInSeconds shr 40) and 0xFF).toByte(),
                ((timeInSeconds shr 32) and 0xFF).toByte(),
                ((timeInSeconds shr 24) and 0xFF).toByte(),
                ((timeInSeconds shr 16) and 0xFF).toByte(),
                ((timeInSeconds shr 8) and 0xFF).toByte(),
                (timeInSeconds and 0xFF).toByte(),
            )
    }
}
