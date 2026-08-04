package co.cnewb.yammic.ymcsdk.navigation

import co.cnewb.yammic.ymcsdk.navigation.protocol.NaviLiteCodec

/**
 * A frame parsed off the wire.
 */
data class NavFrameView(val serviceType: Int, val payload: ByteArray)

/**
 * Reads complete NaviLite frames from a [NavByteChannel].
 * Single responsibility: framing/resync. It owns no transport and no protocol semantics
 * beyond locating frame boundaries.
 *
 * Ported from Pillion's FrameReader.
 */
class NavFrameReader(private val channel: NavByteChannel) {
    private var buf = ByteArray(0)
    private val tmp = ByteArray(8192)

    /**
     * Reads and returns the next complete frame from the channel.
     * May skip garbage bytes to resynchronize on frame magic.
     * Throws if the channel is closed.
     */
    fun next(): NavFrameView {
        fill(NaviLiteCodec.HEADER_SIZE + NaviLiteCodec.CRC_SIZE)
        while (!NaviLiteCodec.hasMagicAt(buf, 0)) dropOne()
        val len = NaviLiteCodec.frameLengthAt(buf, 0)
        check(len >= NaviLiteCodec.HEADER_SIZE + NaviLiteCodec.CRC_SIZE) { "bad frame length $len" }
        fill(len)
        val view = NavFrameView(NaviLiteCodec.serviceTypeAt(buf, 0), NaviLiteCodec.payloadAt(buf, 0))
        buf = buf.copyOfRange(len, buf.size)
        return view
    }

    private fun fill(n: Int) {
        while (buf.size < n) {
            val r = channel.read(tmp)
            if (r < 0) error("channel closed")
            if (r > 0) buf += tmp.copyOf(r)
        }
    }

    private fun dropOne() {
        fill(4)
        buf = buf.copyOfRange(1, buf.size)
    }
}
