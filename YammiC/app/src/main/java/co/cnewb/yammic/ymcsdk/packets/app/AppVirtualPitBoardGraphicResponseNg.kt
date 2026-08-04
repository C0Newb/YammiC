package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Tells the CCU we can't produce the graphic it just requested via `VirtualPitBoardGraphicRequest`.
 * No payload — matches the reference SDK's empty-body encoder.
 */
class AppVirtualPitBoardGraphicResponseNg(
    packetHandler: YmcPacketHandler,
) : AppOutgoingResponse(packetHandler, YmcCommandCode.VirtualPitBoardGraphicResponseNg) {
    override val transport: Transport
        get() = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray = preamble
}
