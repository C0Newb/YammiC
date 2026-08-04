package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * CCU's request for action and/or possible response.
 */
abstract class CcuIncomingRequest : CcuIncomingPacket {
    constructor(
        commandCode: YmcCommandCode,
        packetHandler: YmcPacketHandler,
        data: ByteArray,
    ) : super(commandCode, packetHandler, data)
}
