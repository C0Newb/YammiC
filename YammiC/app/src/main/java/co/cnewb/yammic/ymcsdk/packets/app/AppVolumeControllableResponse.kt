package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Reply to a [co.cnewb.yammic.ymcsdk.packets.ccu.CcuVolumeControllableRequest].
 * Wire: `[cmd 2B][canControl 1B]` — no requestId.
 */
class AppVolumeControllableResponse(
    packetHandler: YmcPacketHandler,
    val canControl: Boolean,
) : AppOutgoingResponse(packetHandler, YmcCommandCode.VolumeControllableResponse) {
    override val transport = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray =
        preamble + byteArrayOf(if (canControl) 0x01 else 0x00)
}
