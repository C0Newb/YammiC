package co.cnewb.yammic.ymcsdk.packets.ccu

import co.cnewb.yammic.ymcsdk.packets.MalformedPacketException
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * CCU's per-chunk acknowledgement for a `SendVirtualPitBoardGraphic` write.
 * Wire: `[cmd 2B][contentId 4B][index 1B][response 1B]` = 8 bytes.
 */
class CcuSendVirtualPitBoardGraphicResponse(
    packetHandler: YmcPacketHandler,
    data: ByteArray,
) : CcuIncomingResponse(
        YmcCommandCode.SendVirtualPitBoardGraphicResponse,
        packetHandler,
        data,
    ) {
    enum class Response { Success, FailureRetry, FailureNotRetry, Unknown }

    var contentId: Int = 0
        private set

    var index: Byte = 0
        private set

    var response: Response = Response.Unknown
        private set

    init { deserialize(data) }

    override fun deserialize(data: ByteArray) {
        if (data.size < 8) {
            throw MalformedPacketException("SendVirtualPitBoardGraphicResponse is missing its body.")
        }
        contentId =
            ((data[2].toInt() and 0xFF) shl 24) or
                ((data[3].toInt() and 0xFF) shl 16) or
                ((data[4].toInt() and 0xFF) shl 8) or
                (data[5].toInt() and 0xFF)
        index = data[6]
        response =
            when (data[7]) {
                1.toByte() -> Response.Success
                2.toByte() -> Response.FailureRetry
                3.toByte() -> Response.FailureNotRetry
                else -> Response.Unknown
            }
    }
}
