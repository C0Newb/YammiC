package co.cnewb.yammic.ymcsdk.handlers

import co.cnewb.yammic.ymcsdk.YmcConnectionContext
import co.cnewb.yammic.ymcsdk.models.YmcLanguage
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import co.cnewb.yammic.ymcsdk.packets.app.AppGetDeviceLanguageResponse
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuGetDeviceLanguageRequest
import org.intellij.lang.annotations.Language

/**
 * Handles responses to the [CcuGetDeviceLanguageRequest] by responding with [language].
 */
class CcuGetDeviceLanguageRequestHandler(
    language: YmcLanguage,
) : CcuRequestHandler<CcuGetDeviceLanguageRequest>() {
    /**
     * Language sent to CCU upon request.
     */
    var language: YmcLanguage = language
        private set

    override suspend fun invoke(
        packet: CcuGetDeviceLanguageRequest,
        packetHandler: YmcPacketHandler,
        connectionContext: YmcConnectionContext,
    ) {
        AppGetDeviceLanguageResponse(packetHandler, language).send(connectionContext)
    }
}
