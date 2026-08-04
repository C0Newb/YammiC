package co.cnewb.yammic.ymcsdk.handlers

import android.content.Context
import co.cnewb.yammic.ymcsdk.YmcConnectionContext
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import co.cnewb.yammic.ymcsdk.packets.app.AppPhoneCellSignalLevelResponse
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuPhoneCellSignalLevelRequest

class CcuPhoneCellSignalLevelRequestHandler(
    private val context: Context,
) : CcuRequestHandler<CcuPhoneCellSignalLevelRequest>() {
    override suspend fun invoke(
        packet: CcuPhoneCellSignalLevelRequest,
        packetHandler: YmcPacketHandler,
        connectionContext: YmcConnectionContext,
    ) {
        AppPhoneCellSignalLevelResponse(packetHandler, context).send(connectionContext)
    }
}
