package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * CCU's response to our request.
 */
abstract class CcuIncomingResponse(
    commandCode: YmcCommandCode,
    packetHandler: YmcPacketHandler,
    data: ByteArray,
) : CcuIncomingPacket(commandCode, packetHandler, data)
