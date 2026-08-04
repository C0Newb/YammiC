package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * CCU is ready and asks us to send the Virtual Pit Board graphic queued by our
 * `VirtualPitBoardRequest`. We should follow up with chunked `SendVirtualPitBoardGraphic` packets
 * (or `VirtualPitBoardGraphicResponseNg` if we can't produce it). No payload beyond the command code.
 */
class CcuVirtualPitBoardGraphicRequest(
    packetHandler: YmcPacketHandler,
    data: ByteArray,
) : CcuIncomingRequest(
        YmcCommandCode.VirtualPitBoardGraphicRequest,
        packetHandler,
        data,
    ) {
    init { deserialize(data) }

    override fun deserialize(data: ByteArray) {
        // No payload.
    }
}
