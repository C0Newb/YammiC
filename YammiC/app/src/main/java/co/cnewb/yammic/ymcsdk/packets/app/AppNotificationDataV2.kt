package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import java.util.Calendar

/**
 * Sends a notification to the CCU display.
 *
 * Wire: `[cmd 2B: 0x012F][payloadLen 3B][protobuf payload]`
 *
 * The payload is a hand-encoded Protocol Buffers message (McNotificationDataV2):
 * - field 3  notificationId (varint)
 * - field 4  category (varint: 0=MESSAGE, 1=CALL)
 * - field 5–10  year/month/day/hour/minutes/second (varint each)
 * - field 11 title (len-delimited string, max 64 UTF-8 bytes)
 * - field 12 subTitle (len-delimited string, max 64 UTF-8 bytes)
 * - field 13 detail (len-delimited string, max 512 UTF-8 bytes)
 */
class AppNotificationDataV2(
    packetHandler: YmcPacketHandler,
    private val notificationId: Int,
    private val category: Category,
    private val title: String,
    private val subTitle: String = "",
    private val detail: String,
    private val timestamp: Calendar = Calendar.getInstance(),
) : AppOutgoingRequest(packetHandler, YmcCommandCode.NotificationDataV2) {

    enum class Category(val value: Int) {
        Message(0),
        Call(1),
    }

    override val transport = Transport.Ddt

    override fun serialize(requestId: Byte): ByteArray {
        val proto = buildProto()
        return preamble + int24(proto.size) + proto
    }

    private fun buildProto(): ByteArray {
        val titleBytes = stringBytes(title, maxBytes = 64)
        val subTitleBytes = stringBytes(subTitle, maxBytes = 64)
        val detailBytes = stringBytes(detail, maxBytes = 512)

        return protoVarint(3, notificationId) +
            protoVarint(4, category.value) +
            protoVarint(5, timestamp.get(Calendar.YEAR)) +
            protoVarint(6, timestamp.get(Calendar.MONTH) + 1) +
            protoVarint(7, timestamp.get(Calendar.DAY_OF_MONTH)) +
            protoVarint(8, timestamp.get(Calendar.HOUR_OF_DAY)) +
            protoVarint(9, timestamp.get(Calendar.MINUTE)) +
            protoVarint(10, timestamp.get(Calendar.SECOND)) +
            (if (titleBytes.isNotEmpty()) protoString(11, titleBytes) else byteArrayOf()) +
            (if (subTitleBytes.isNotEmpty()) protoString(12, subTitleBytes) else byteArrayOf()) +
            (if (detailBytes.isNotEmpty()) protoString(13, detailBytes) else byteArrayOf())
    }

    private fun protoVarint(fieldNumber: Int, value: Int): ByteArray {
        val tag = (fieldNumber shl 3) or 0
        return varint(tag) + varint(value)
    }

    private fun protoString(fieldNumber: Int, utf8Bytes: ByteArray): ByteArray {
        val tag = (fieldNumber shl 3) or 2
        return varint(tag) + varint(utf8Bytes.size) + utf8Bytes
    }

    private fun varint(value: Int): ByteArray {
        var v = value
        val buf = ByteArray(5)
        var pos = 0
        do {
            var b = v and 0x7F
            v = v ushr 7
            if (v != 0) b = b or 0x80
            buf[pos++] = b.toByte()
        } while (v != 0)
        return buf.copyOf(pos)
    }
}
