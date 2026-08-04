package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Our request for action and/or possible response from the CCU.
 */
abstract class AppOutgoingRequest(
    packetHandler: YmcPacketHandler,
    commandCode: YmcCommandCode,
) : AppOutgoingPacket(packetHandler, commandCode)
