package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

class CcuAuthenticationResponse(
    packetHandler: YmcPacketHandler,
    data: ByteArray,
) : CcuIncomingResponse(
        YmcCommandCode.AuthenticationResponse,
        packetHandler,
        data,
    ) {
    var success: Boolean = false
        private set

    init { deserialize(data) }

    override fun deserialize(data: ByteArray) {
        success = data.size >= 3 && data[2] == 0x01.toByte()
    }
}
