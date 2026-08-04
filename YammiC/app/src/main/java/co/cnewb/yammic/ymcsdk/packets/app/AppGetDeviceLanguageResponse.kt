package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.models.YmcLanguage
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

class AppGetDeviceLanguageResponse(
    packetHandler: YmcPacketHandler,
    val language: YmcLanguage,
) : AppOutgoingResponse(packetHandler, YmcCommandCode.GetDeviceLanguageResponse) {
    override val transport: Transport
        get() = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray = preamble + byteArrayOf(requestId, language.value)
}
