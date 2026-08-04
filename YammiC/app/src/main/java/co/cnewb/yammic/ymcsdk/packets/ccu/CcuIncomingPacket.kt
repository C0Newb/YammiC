package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacket
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import java.io.InvalidClassException

/**
 * Representing a packet sent by the CCU received by us.
 */
abstract class CcuIncomingPacket : YmcPacket {
    constructor(
        commandCode: YmcCommandCode,
        packetHandler: YmcPacketHandler,
        data: ByteArray,
    ) : super(commandCode, packetHandler)

    protected abstract fun deserialize(data: ByteArray)
}
