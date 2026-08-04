package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/** CCU rejected our `VirtualPitBoardRequest`. No payload beyond the command code. */
class CcuVirtualPitBoardResponseNg(
    packetHandler: YmcPacketHandler,
    data: ByteArray,
) : CcuIncomingResponse(
        YmcCommandCode.VirtualPitBoardResponseNg,
        packetHandler,
        data,
    ) {
    init { deserialize(data) }

    override fun deserialize(data: ByteArray) {
        // No payload.
    }
}
