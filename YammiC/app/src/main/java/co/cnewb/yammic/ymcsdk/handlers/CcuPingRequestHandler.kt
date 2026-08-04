package co.cnewb.yammic.ymcsdk.handlers

import co.cnewb.yammic.ymcsdk.YmcConnectionContext
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import co.cnewb.yammic.ymcsdk.packets.app.AppPongResponse
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuPingRequest

/**
 * Handles responding to the CCU's keep-alive (ping) requests.
 * This is critical.
 */
class CcuPingRequestHandler : CcuRequestHandler<CcuPingRequest>() {
    override suspend fun invoke(
        packet: CcuPingRequest,
        packetHandler: YmcPacketHandler,
        connectionContext: YmcConnectionContext,
    ) {
        AppPongResponse(packetHandler).send(connectionContext)
    }
}
