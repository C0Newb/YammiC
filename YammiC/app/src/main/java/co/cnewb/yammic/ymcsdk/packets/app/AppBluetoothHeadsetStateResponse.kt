package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Reply to a [co.cnewb.yammic.ymcsdk.packets.ccu.CcuBluetoothHeadsetStateRequest].
 * Wire: `[cmd 2B][connected 1B]` — no requestId.
 */
class AppBluetoothHeadsetStateResponse(
    packetHandler: YmcPacketHandler,
    val connected: Boolean,
) : AppOutgoingResponse(packetHandler, YmcCommandCode.BluetoothHeadsetStateResponse) {
    override val transport = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray =
        preamble + byteArrayOf(if (connected) 0x01 else 0x00)
}
