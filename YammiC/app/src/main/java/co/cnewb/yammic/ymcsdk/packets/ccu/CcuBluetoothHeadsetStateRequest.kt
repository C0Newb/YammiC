package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

class CcuBluetoothHeadsetStateRequest(
    packetHandler: YmcPacketHandler,
    data: ByteArray,
) : CcuIncomingRequest(
        YmcCommandCode.BluetoothHeadsetStateRequest,
        packetHandler,
        data,
    ) {
    override fun deserialize(data: ByteArray) {
        // Data?
    }
}
