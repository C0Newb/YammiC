package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/** CCU reports it could not clear the Virtual Pit Board display. No payload beyond the command code. */
class CcuClearVirtualPitBoardResponseNg(
    packetHandler: YmcPacketHandler,
    data: ByteArray,
) : CcuIncomingResponse(
        YmcCommandCode.ClearVirtualPitBoardResponseNg,
        packetHandler,
        data,
    ) {
    init { deserialize(data) }

    override fun deserialize(data: ByteArray) {
        // No payload.
    }
}
