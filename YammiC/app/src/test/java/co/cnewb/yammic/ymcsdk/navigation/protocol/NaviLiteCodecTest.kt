package co.cnewb.yammic.ymcsdk.navigation.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class NaviLiteCodecTest {
    @Test
    fun `build produces a frame that round-trips through the parse helpers`() {
        val payload = byteArrayOf(1, 2, 3, 4, 5)
        val frame = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.IMAGE,
            payloadDataType = NAVILITE_PDT_POINTER,
            payload = payload,
        )

        assertTrue(NaviLiteCodec.hasMagicAt(frame, 0))
        assertEquals(NaviLiteCodec.HEADER_SIZE + NaviLiteCodec.CRC_SIZE + payload.size, NaviLiteCodec.frameLengthAt(frame, 0))
        assertEquals(NaviLiteServiceType.IMAGE, NaviLiteCodec.serviceTypeAt(frame, 0))
        assertEquals(payload.toList(), NaviLiteCodec.payloadAt(frame, 0).toList())
    }

    @Test
    fun `hasMagicAt is false when the buffer doesn't start with the magic bytes`() {
        val garbage = byteArrayOf(0, 0, 0, 0)
        assertFalse(NaviLiteCodec.hasMagicAt(garbage, 0))
    }

    @Test
    fun `changing the payload changes the CRC bytes`() {
        val frameA = NaviLiteCodec.build(NAVILITE_FRAME_TYPE_PHONE, NaviLiteServiceType.IMAGE, NAVILITE_PDT_POINTER, byteArrayOf(1))
        val frameB = NaviLiteCodec.build(NAVILITE_FRAME_TYPE_PHONE, NaviLiteServiceType.IMAGE, NAVILITE_PDT_POINTER, byteArrayOf(2))
        val crcRangeA = frameA.copyOfRange(12, 16).toList()
        val crcRangeB = frameB.copyOfRange(12, 16).toList()
        assertTrue(crcRangeA != crcRangeB)
    }

    @Test
    fun `CRC matches a real SEC_DATA_ACK frame captured from a Yamaha CCU`() {
        // Captured wire vector from Pillion's ProtocolTest: header(frameType=6, svc=0x54,
        // pdt=1, size=4) + payload b0a04756 -> CRC field 24 f0 73 5b. Confirms our CRC/codec
        // port matches real hardware, not just itself.
        val payload = "b0a04756".chunked(2).map { it.toInt(16).toByte() }.toByteArray()
        val frame = NaviLiteCodec.build(
            frameType = 6,
            serviceType = 0x54,
            payloadDataType = 1,
            payload = payload,
        )
        val crcHex = frame.copyOfRange(12, 16).joinToString("") { (it.toInt() and 0xff).toString(16).padStart(2, '0') }
        assertEquals("24f0735b", crcHex)
    }
}
