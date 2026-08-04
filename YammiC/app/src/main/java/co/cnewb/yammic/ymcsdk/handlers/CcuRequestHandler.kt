package co.cnewb.yammic.ymcsdk.handlers

import co.cnewb.yammic.ymcsdk.YmcConnectionContext
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuIncomingPacket

abstract class CcuRequestHandler<T> where T : CcuIncomingPacket {
    abstract suspend fun invoke(
        packet: T,
        packetHandler: YmcPacketHandler,
        connectionContext: YmcConnectionContext,
    )
}
