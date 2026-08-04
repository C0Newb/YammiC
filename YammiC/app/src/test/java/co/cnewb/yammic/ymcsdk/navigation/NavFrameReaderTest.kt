package co.cnewb.yammic.ymcsdk.navigation

import co.cnewb.yammic.ymcsdk.navigation.protocol.NAVILITE_FRAME_TYPE_PHONE
import co.cnewb.yammic.ymcsdk.navigation.protocol.NAVILITE_PDT_POINTER
import co.cnewb.yammic.ymcsdk.navigation.protocol.NAVILITE_PDT_VALUE
import co.cnewb.yammic.ymcsdk.navigation.protocol.NaviLiteCodec
import co.cnewb.yammic.ymcsdk.navigation.protocol.NaviLiteServiceType
import org.junit.Assert.assertEquals
import org.junit.Test

class NavFrameReaderTest {
    @Test
    fun `reads a single complete frame correctly`() {
        val payload = byteArrayOf(1, 2, 3, 4, 5)
        val frame = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.IMAGE,
            payloadDataType = NAVILITE_PDT_VALUE,
            payload = payload,
        )
        val channel = FakeNavByteChannel()
        channel.feed(frame)

        val reader = NavFrameReader(channel)
        val result = reader.next()

        assertEquals(NaviLiteServiceType.IMAGE, result.serviceType)
        assertEquals(payload.toList(), result.payload.toList())
    }

    @Test
    fun `resyncs past garbage and parses consecutive frames`() {
        val f1 = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.ESN_UPDATE,
            payloadDataType = NAVILITE_PDT_VALUE,
            payload = byteArrayOf(),
        )
        val f2 = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.SEC_DATA,
            payloadDataType = NAVILITE_PDT_POINTER,
            payload = byteArrayOf(1, 2, 3, 4),
        )
        val channel = FakeNavByteChannel()
        // Lead with a stray byte the reader must skip to find the magic.
        channel.feed(byteArrayOf(0x00) + f1 + f2)

        val reader = NavFrameReader(channel)

        val a = reader.next()
        assertEquals(NaviLiteServiceType.ESN_UPDATE, a.serviceType)
        assertEquals(0, a.payload.size)

        val b = reader.next()
        assertEquals(NaviLiteServiceType.SEC_DATA, b.serviceType)
        assertEquals(byteArrayOf(1, 2, 3, 4).toList(), b.payload.toList())
    }

    @Test
    fun `reads frame with empty payload`() {
        val frame = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.HOME,
            payloadDataType = NAVILITE_PDT_VALUE,
            payload = byteArrayOf(),
        )
        val channel = FakeNavByteChannel()
        channel.feed(frame)

        val reader = NavFrameReader(channel)
        val result = reader.next()

        assertEquals(NaviLiteServiceType.HOME, result.serviceType)
        assertEquals(0, result.payload.size)
    }

    @Test(expected = IllegalStateException::class)
    fun `next throws when channel returns -1 (closed)`() {
        val frame = NaviLiteCodec.build(
            NAVILITE_FRAME_TYPE_PHONE,
            NaviLiteServiceType.ESN_UPDATE,
            NAVILITE_PDT_POINTER,
            ByteArray(0)
        )
        val channel = FakeNavByteChannel()
        channel.feed(frame.copyOfRange(0, 10)) // partial frame
        val reader = NavFrameReader(channel)

        // First call will fill buffer and get the partial frame
        reader.next() // should throw "channel closed" when trying to read rest
    }

    @Test(expected = IllegalStateException::class)
    fun `next throws on invalid frame length`() {
        // Build a header with length field set to impossibly large value
        val badHeader = byteArrayOf(
            0x6e, 0x41, 0x6c, 0x40, // magic "nAl@"
            1, 6, 0, // version, frameType, serviceType
            -1, -1, -1, -1, // payloadSize = 0xFFFFFFFF (4GB!)
            1 // payloadDataType
        )
        // Add a fake CRC so hasMagicAt passes
        val fakeFrame = badHeader + byteArrayOf(0, 0, 0, 0)

        val channel = FakeNavByteChannel()
        channel.feed(fakeFrame)
        val reader = NavFrameReader(channel)
        reader.next() // should throw on check(len >= ...)
    }
}
