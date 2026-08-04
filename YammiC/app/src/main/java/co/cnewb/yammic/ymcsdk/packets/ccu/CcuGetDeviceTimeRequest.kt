package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * CCU requests current date/time (repeats every ~5s until answered).
 */
class CcuGetDeviceTimeRequest(
    packetHandler: YmcPacketHandler,
    data: ByteArray,
) : CcuIncomingRequest(
        YmcCommandCode.VehicleSettingRequest,
        packetHandler,
        data,
    ) {
    override fun deserialize(data: ByteArray) {
        // Is there any data?
    }
}
