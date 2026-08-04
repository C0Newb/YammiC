package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Identifies the app to the CCU.
 * YC: `IdentifyAppRequest`
 */
class AppIdentifyRequest : AppOutgoingRequest {
    constructor(
        packetHandler: YmcPacketHandler,
        appId: Int? = 4660,
    ) : super(packetHandler, YmcCommandCode.IdentifyAppRequest) {
        this.appId = appId ?: 4660
    }

    override val transport: Transport
        get() = Transport.Simple

    private val appId: Int

    override fun serialize(requestId: Byte): ByteArray = this.preamble + byteArrayOf(requestId) + intBytes(this.appId)
}
