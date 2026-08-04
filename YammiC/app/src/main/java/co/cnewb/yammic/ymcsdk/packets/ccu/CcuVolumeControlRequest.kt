package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.MalformedPacketException
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

class CcuVolumeControlRequest(
    packetHandler: YmcPacketHandler,
    data: ByteArray,
) : CcuIncomingRequest(
        YmcCommandCode.PhoneVolumeControlRequest,
        packetHandler,
        data,
    ) {
    enum class Action { Unknown, PhoneUp, PhoneDown, MuteMicrophone, MuteSpeaker, MediaUp, MediaDown }

    var action: Action = Action.Unknown
        private set

    init { deserialize(data) }

    override fun deserialize(data: ByteArray) {
        if (data.size < 3) {
            throw MalformedPacketException("Volume control request is missing the control byte.")
        }
        action =
            when (data[2]) {
                0x01.toByte() -> Action.PhoneUp
                0x02.toByte() -> Action.PhoneDown
                0x03.toByte() -> Action.MuteMicrophone
                0x04.toByte() -> Action.MuteSpeaker
                0x05.toByte() -> Action.MediaUp
                0x06.toByte() -> Action.MediaDown
                else -> throw MalformedPacketException("Volume control request has an unknown control byte.")
            }
    }
}
