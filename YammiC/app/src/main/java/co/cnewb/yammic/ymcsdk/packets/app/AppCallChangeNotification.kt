package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Tells the CCU whether a phone call is currently active (off-hook) or has ended (on-hook).
 * Wire: `[cmd 2B][state 1B]` where state is 1=Started, 2=Ended.
 */
class AppCallChangeNotification(
    packetHandler: YmcPacketHandler,
    private val started: Boolean,
) : AppOutgoingResponse(packetHandler, YmcCommandCode.CallChangeNotification) {
    override val transport = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray = preamble + byteArrayOf(if (started) 0x01 else 0x02)
}
