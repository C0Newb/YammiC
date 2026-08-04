package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Asks the CCU to start a Virtual Pit Board display sequence.
 * The CCU replies with either `VirtualPitBoardGraphicRequest` (proceed, send the graphic) or
 * `VirtualPitBoardResponseNg` (rejected).
 * Wire: `[cmd 2B]` — no payload. The reference SDK's `d.u` payload builder for this command
 * returns an empty byte array; the content ID is only sent later, with the graphic chunks.
 */
class AppVirtualPitBoardRequest(
    packetHandler: YmcPacketHandler,
) : AppOutgoingRequest(packetHandler, YmcCommandCode.VirtualPitBoardRequest) {
    override val transport: Transport
        get() = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray = preamble
}
