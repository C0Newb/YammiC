package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

class CcuVolumeControllableRequest(
    packetHandler: YmcPacketHandler,
    data: ByteArray,
) : CcuIncomingRequest(
        YmcCommandCode.VolumeControllableRequest,
        packetHandler,
        data,
    ) {
    override fun deserialize(data: ByteArray) {
        // Any data?
    }
}
