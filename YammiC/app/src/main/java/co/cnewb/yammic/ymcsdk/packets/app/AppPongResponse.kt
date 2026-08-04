package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Pong reply to a keepalive from the CCU.
 */
class AppPongResponse(
    packetHandler: YmcPacketHandler,
) : AppOutgoingResponse(
        packetHandler,
        YmcCommandCode.PongResponse,
    ) {
    override val transport: Transport
        get() = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray = preamble + byteArrayOf(requestId)
}
