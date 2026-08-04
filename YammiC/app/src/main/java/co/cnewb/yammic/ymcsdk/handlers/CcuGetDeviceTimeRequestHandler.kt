package co.cnewb.yammic.ymcsdk.handlers

import co.cnewb.yammic.ymcsdk.YmcConnectionContext
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import co.cnewb.yammic.ymcsdk.packets.app.AppGetDeviceTimeResponse
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuGetDeviceTimeRequest

/**
 * Handles responses to the [CcuGetDeviceTimeRequestHandler] by responding with the system time
 * ([System.currentTimeMillis]).
 */
open class CcuGetDeviceTimeRequestHandler : CcuRequestHandler<CcuGetDeviceTimeRequest>() {
    override suspend fun invoke(
        packet: CcuGetDeviceTimeRequest,
        packetHandler: YmcPacketHandler,
        connectionContext: YmcConnectionContext,
    ) {
        AppGetDeviceTimeResponse(packetHandler).send(connectionContext)
    }
}
