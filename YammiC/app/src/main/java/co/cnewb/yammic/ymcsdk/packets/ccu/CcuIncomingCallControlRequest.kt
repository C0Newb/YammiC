package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.MalformedPacketException
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

class CcuIncomingCallControlRequest(
    packetHandler: YmcPacketHandler,
    data: ByteArray,
) : CcuIncomingRequest(
        YmcCommandCode.IncomingCallControl,
        packetHandler,
        data,
    ) {
    enum class Action { Answer, Reject, OnHook, Unknown }

    var action: Action = Action.Unknown
        private set

    init { deserialize(data) }

    override fun deserialize(data: ByteArray) {
        if (data.size < 3) {
            throw MalformedPacketException("IncomingCallControl request is missing the action byte.")
        }
        action = when (data[2]) {
            0x00.toByte() -> Action.Reject
            0x01.toByte() -> Action.Answer
            0x02.toByte() -> Action.OnHook
            else -> Action.Unknown
        }
    }
}
