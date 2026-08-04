package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

class CcuPhoneCellSignalLevelRequest(
    packetHandler: YmcPacketHandler,
    data: ByteArray,
) : CcuIncomingRequest(
        YmcCommandCode.PhoneCellSignalLevelRequest,
        packetHandler,
        data,
    ) {
    override fun deserialize(data: ByteArray) {}
}
