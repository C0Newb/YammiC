package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Asks the CCU to clear the Virtual Pit Board display.
 * The CCU replies with `ClearVirtualPitBoardResponseOk` or `ClearVirtualPitBoardResponseNg`.
 * No payload — matches the reference SDK's empty-body encoder.
 */
class AppClearVirtualPitBoard(
    packetHandler: YmcPacketHandler,
) : AppOutgoingRequest(packetHandler, YmcCommandCode.ClearVirtualPitBoard) {
    override val transport: Transport
        get() = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray = preamble
}
