package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.MalformedPacketException
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

class CcuMusicControlRequest(
    packetHandler: YmcPacketHandler,
    data: ByteArray,
) : CcuIncomingRequest(
        YmcCommandCode.MusicControlRequest,
        packetHandler,
        data,
    ) {
    enum class Action { Unknown, NextTrack, PreviousTrack, Play, Pause }

    var action: Action = Action.Unknown
        private set

    init { deserialize(data) }

    override fun deserialize(data: ByteArray) {
        if (data.size < 3) {
            throw MalformedPacketException("Music control request is missing the control byte.")
        }
        action =
            when (data[2]) {
                0x01.toByte() -> Action.NextTrack
                0x02.toByte() -> Action.PreviousTrack
                0x03.toByte() -> Action.Play
                0x04.toByte() -> Action.Pause
                else -> throw MalformedPacketException("Music control request has an unknown control byte.")
            }
    }
}
