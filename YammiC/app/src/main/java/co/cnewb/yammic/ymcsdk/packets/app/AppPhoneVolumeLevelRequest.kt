package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.models.BikeVolumeType
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Reports the current volume level to the CCU so its display stays in sync.
 * Wire: `[cmd 2B][volume 1B][type 1B]` — no requestId.
 */
class AppPhoneVolumeLevelRequest(
    packetHandler: YmcPacketHandler,
    val type: BikeVolumeType,
    val volumePercent: Int,
) : AppOutgoingRequest(packetHandler, YmcCommandCode.PhoneVolumeLevel) {
    override val transport = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray =
        preamble + byteArrayOf(volumePercent.coerceIn(0, 100).toByte(), type.wireValue)
}
