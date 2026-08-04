package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

class CcuGetDeviceLanguageRequest(
    packetHandler: YmcPacketHandler,
    data: ByteArray,
) : CcuIncomingRequest(
        YmcCommandCode.LanguageRequest,
        packetHandler,
        data,
    ) {
    override fun deserialize(data: ByteArray) {
        // Is there any data?
    }
}
