package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * App's response to the CCU's request.
 */
abstract class AppOutgoingResponse(
    packetHandler: YmcPacketHandler,
    commandCode: YmcCommandCode,
) : AppOutgoingPacket(packetHandler, commandCode)
