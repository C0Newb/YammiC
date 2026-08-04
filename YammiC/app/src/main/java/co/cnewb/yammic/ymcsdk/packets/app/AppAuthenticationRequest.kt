package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Authenticates the app with the CCU.
 */
class AppAuthenticationRequest(
    packetHandler: YmcPacketHandler,
    val ccuId: String,
    val passKey: String,
) : AppOutgoingRequest(
        packetHandler,
        YmcCommandCode.AuthenticationRequest,
    ) {
    override val transport: Transport
        get() = Transport.Ddt

    /*
     * Format: `[cmd 2B][ccuId 14B][passKey 6B]`.
     * The DDT length-prefix format (frameRequest) is NOT used. YCCU expects exactly 22 bytes.
     */
    override fun serialize(requestId: Byte): ByteArray = this.preamble + stringBytes(ccuId) + stringBytes(passKey)
}
