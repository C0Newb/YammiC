package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.models.YmcMediaMetaData
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Sends music metadata to the CCU via DDT session.
 * Wire: `[cmd 2B][artistLen 1B][artist][albumLen 1B][album][songLen 1B][song][duration 3B seconds][state 1B][rate 2B][elapsed 3B seconds]`.
 * Strings are length-prefixed, max 64 UTF-8 bytes each.
 * Duration and elapsed are 24-bit big-endian seconds (not milliseconds), confirmed against a real CCU capture.
 */
class AppMusicMetaDataResponse(
    packetHandler: YmcPacketHandler,
    val metadata: YmcMediaMetaData,
) : AppOutgoingResponse(packetHandler, YmcCommandCode.BluetoothMusicMetaData) {
    override val transport = Transport.Ddt

    override fun serialize(requestId: Byte): ByteArray {
        val artist = stringBytes(metadata.artistName, maxBytes = 64)
        val album = stringBytes(metadata.albumName, maxBytes = 64)
        val song = stringBytes(metadata.songName, maxBytes = 64)
        val payload =
            byteArrayOf(artist.size.toByte()) + artist +
                byteArrayOf(album.size.toByte()) + album +
                byteArrayOf(song.size.toByte()) + song +
                int24(metadata.durationSeconds) +
                byteArrayOf(metadata.playbackState.value) +
                shortBytes((metadata.playbackRate * 10.0f).toInt()) +
                int24(metadata.elapsedSeconds)
        return preamble + payload
    }
}
