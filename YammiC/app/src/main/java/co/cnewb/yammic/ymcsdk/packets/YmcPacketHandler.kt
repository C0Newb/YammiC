package co.cnewb.yammic.ymcsdk.packets

import co.cnewb.yammic.ymcsdk.YmcConnectionContext
import co.cnewb.yammic.ymcsdk.packets.app.AppOutgoingPacket
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuIncomingRequest
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuIncomingResponse

interface YmcPacketHandler {
    /**
     * Sends a packet to the CCU.
     */
    suspend fun send(
        connectionContext: YmcConnectionContext,
        request: AppOutgoingPacket,
    )
}
