package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/** CCU confirms the Virtual Pit Board display was cleared. No payload beyond the command code. */
class CcuClearVirtualPitBoardResponseOk(
    packetHandler: YmcPacketHandler,
    data: ByteArray,
) : CcuIncomingResponse(
        YmcCommandCode.ClearVirtualPitBoardResponseOk,
        packetHandler,
        data,
    ) {
    init { deserialize(data) }

    override fun deserialize(data: ByteArray) {
        // No payload.
    }
}
