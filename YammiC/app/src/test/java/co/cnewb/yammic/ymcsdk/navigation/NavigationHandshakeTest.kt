package co.cnewb.yammic.ymcsdk.navigation

import co.cnewb.yammic.ymcsdk.navigation.protocol.NAVILITE_FRAME_TYPE_PHONE
import co.cnewb.yammic.ymcsdk.navigation.protocol.NAVILITE_PDT_VALUE
import co.cnewb.yammic.ymcsdk.navigation.protocol.NaviLiteCodec
import co.cnewb.yammic.ymcsdk.navigation.protocol.NaviLiteAuth
import co.cnewb.yammic.ymcsdk.navigation.protocol.NaviLiteServiceType
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Test

class NavigationHandshakeTest {

    @Test
    fun `handshake completes the full sequence ESN-ack to setup burst`() {
        // Set up the fake channel with frames from the bike
        val channel = FakeNavByteChannel()

        // Step 1: ESN_UPDATE from bike, then handshake sends ESN_ACK
        val esnUpdateFrame = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.ESN_UPDATE,
            payloadDataType = NAVILITE_PDT_VALUE,
            payload = byteArrayOf(1, 2, 3), // Fake ESN bytes
        )

        // Step 3: SEC_DATA from bike, handshake responds with SEC_DATA_ACK
        val partNumber = "1KS-H2540-00"
        val nonce = byteArrayOf(0x11, 0x22, 0x33, 0x44)
        val secDataPayload = (partNumber.map { it.code.toByte() } + nonce.toList())
            .map { (it.toInt() xor 0x0a).toByte() }
            .toByteArray()
        val secDataFrame = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.SEC_DATA,
            payloadDataType = NAVILITE_PDT_VALUE,
            payload = secDataPayload,
        )

        // Feed frames to the channel
        channel.feed(esnUpdateFrame + secDataFrame)

        // Run the handshake
        val handshake = NavigationHandshake(channel)
        handshake.execute()

        // Verify that frames were written: ESN_ACK, AUTH_REQUEST, SEC_DATA_ACK, then the
        // 11-frame setup burst (GPS and APP_SETTING are each sent twice).
        val written = channel.written
        assertEquals(14, written.size)

        // Verify ESN_ACK (first write) - fixed 2-byte payload, not empty.
        val esnAckFrame = written[0]
        assertTrue(NaviLiteCodec.hasMagicAt(esnAckFrame, 0))
        assertEquals(NaviLiteServiceType.ESN_ACK, NaviLiteCodec.serviceTypeAt(esnAckFrame, 0))
        assertArrayEquals(byteArrayOf(1, 0), NaviLiteCodec.payloadAt(esnAckFrame, 0))

        // Verify AUTH_REQUEST (second write) - carries the app-version payload, not empty.
        val authReqFrame = written[1]
        assertTrue(NaviLiteCodec.hasMagicAt(authReqFrame, 0))
        assertEquals(NaviLiteServiceType.AUTH_REQUEST, NaviLiteCodec.serviceTypeAt(authReqFrame, 0))
        assertArrayEquals(byteArrayOf(0x1c, 0x07, 0, 1, 0, 0, 0, 0), NaviLiteCodec.payloadAt(authReqFrame, 0))

        // Verify SEC_DATA_ACK (third write) - should contain the nonce
        val secDataAckFrame = written[2]
        assertTrue(NaviLiteCodec.hasMagicAt(secDataAckFrame, 0))
        assertEquals(
            NaviLiteServiceType.SEC_DATA_ACK,
            NaviLiteCodec.serviceTypeAt(secDataAckFrame, 0)
        )
        val secDataAckPayload = NaviLiteCodec.payloadAt(secDataAckFrame, 0)
        assertArrayEquals(nonce, secDataAckPayload)

        // Verify setup burst frames (GPS and APP_SETTING appear twice)
        val expectedBurst = listOf(
            NaviLiteServiceType.NAV_STATUS,
            NaviLiteServiceType.DAY_NIGHT,
            NaviLiteServiceType.HOME,
            NaviLiteServiceType.OFFICE,
            NaviLiteServiceType.GPS,
            NaviLiteServiceType.APP_SETTING,
            NaviLiteServiceType.ZOOM,
            NaviLiteServiceType.ROAD,
            NaviLiteServiceType.SPEED_LIMIT,
            NaviLiteServiceType.GPS,
            NaviLiteServiceType.APP_SETTING,
        )
        for ((i, expectedServiceType) in expectedBurst.withIndex()) {
            val burstFrame = written[3 + i]
            assertTrue(NaviLiteCodec.hasMagicAt(burstFrame, 0))
            assertEquals(
                expectedServiceType,
                NaviLiteCodec.serviceTypeAt(burstFrame, 0)
            )
        }
    }

    @Test
    fun `sendEsnAck sends ESN_ACK frame with fixed 2-byte payload`() {
        val channel = FakeNavByteChannel()
        val handshake = NavigationHandshake(channel)

        handshake.sendEsnAck()

        assertEquals(1, channel.written.size)
        val frame = channel.written[0]
        assertTrue(NaviLiteCodec.hasMagicAt(frame, 0))
        assertEquals(NaviLiteServiceType.ESN_ACK, NaviLiteCodec.serviceTypeAt(frame, 0))
        assertArrayEquals(byteArrayOf(1, 0), NaviLiteCodec.payloadAt(frame, 0))
    }

    @Test
    fun `sendAuthRequest sends AUTH_REQUEST frame with app-version payload`() {
        val channel = FakeNavByteChannel()
        val handshake = NavigationHandshake(channel)

        handshake.sendAuthRequest()

        assertEquals(1, channel.written.size)
        val frame = channel.written[0]
        assertTrue(NaviLiteCodec.hasMagicAt(frame, 0))
        assertEquals(NaviLiteServiceType.AUTH_REQUEST, NaviLiteCodec.serviceTypeAt(frame, 0))
        assertArrayEquals(byteArrayOf(0x1c, 0x07, 0, 1, 0, 0, 0, 0), NaviLiteCodec.payloadAt(frame, 0))
    }

    @Test
    fun `receiveSecData and respondSecDataAck extracts nonce and sends correct response`() {
        // Build SEC_DATA frame from bike
        val partNumber = "1KS-H2540-00"
        val nonce = byteArrayOf(0x11, 0x22, 0x33, 0x44)
        val secDataPayload = (partNumber.map { it.code.toByte() } + nonce.toList())
            .map { (it.toInt() xor 0x0a).toByte() }
            .toByteArray()
        val secDataFrame = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.SEC_DATA,
            payloadDataType = NAVILITE_PDT_VALUE,
            payload = secDataPayload,
        )

        val channel = FakeNavByteChannel()
        channel.feed(secDataFrame)

        val handshake = NavigationHandshake(channel)
        handshake.receiveSecDataAndRespond()

        // Verify that SEC_DATA_ACK was sent with the correct nonce
        assertEquals(1, channel.written.size)
        val responseFrame = channel.written[0]
        assertTrue(NaviLiteCodec.hasMagicAt(responseFrame, 0))
        assertEquals(NaviLiteServiceType.SEC_DATA_ACK, NaviLiteCodec.serviceTypeAt(responseFrame, 0))
        val responsePayload = NaviLiteCodec.payloadAt(responseFrame, 0)
        assertArrayEquals(nonce, responsePayload)
    }

    @Test
    fun `receiveSecData skips unrelated frames sent before SEC_DATA`() {
        // A dash button-press request the CCU can interleave before SEC_DATA arrives.
        val unrelatedFrame = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.GO_HOME_REQUEST,
            payloadDataType = NAVILITE_PDT_VALUE,
            payload = byteArrayOf(0),
        )
        val partNumber = "1KS-H2540-00"
        val nonce = byteArrayOf(0x11, 0x22, 0x33, 0x44)
        val secDataPayload = (partNumber.map { it.code.toByte() } + nonce.toList())
            .map { (it.toInt() xor 0x0a).toByte() }
            .toByteArray()
        val secDataFrame = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.SEC_DATA,
            payloadDataType = NAVILITE_PDT_VALUE,
            payload = secDataPayload,
        )

        val channel = FakeNavByteChannel()
        channel.feed(unrelatedFrame + secDataFrame)

        val handshake = NavigationHandshake(channel)
        handshake.receiveSecDataAndRespond()

        assertEquals(1, channel.written.size)
        assertEquals(
            NaviLiteServiceType.SEC_DATA_ACK,
            NaviLiteCodec.serviceTypeAt(channel.written[0], 0),
        )
    }

    @Test
    fun `sendSetupBurst sends all required status frames in order with real payloads`() {
        val channel = FakeNavByteChannel()
        val handshake = NavigationHandshake(channel)

        handshake.sendSetupBurst()

        val expectedBurst = listOf(
            NaviLiteServiceType.NAV_STATUS to byteArrayOf(0, 0),
            NaviLiteServiceType.DAY_NIGHT to byteArrayOf(1, 0),
            NaviLiteServiceType.HOME to byteArrayOf(1),
            NaviLiteServiceType.OFFICE to byteArrayOf(1),
            NaviLiteServiceType.GPS to byteArrayOf(1, 0),
            NaviLiteServiceType.APP_SETTING to byteArrayOf(0, 0),
            NaviLiteServiceType.ZOOM to byteArrayOf(0x07, 0x19, 0x06, 0x00, 0x30, 0x2e, 0x32, 0x20, 0x6d, 0x69),
            NaviLiteServiceType.ROAD to ByteArray(0),
            NaviLiteServiceType.SPEED_LIMIT to byteArrayOf(0, 0, 0, 0, 0x03, 0x6d, 0x70, 0x68),
            NaviLiteServiceType.GPS to byteArrayOf(1, 0),
            NaviLiteServiceType.APP_SETTING to byteArrayOf(1, 0),
        )
        assertEquals(expectedBurst.size, channel.written.size)

        for ((i, expected) in expectedBurst.withIndex()) {
            val (expectedServiceType, expectedPayload) = expected
            val frame = channel.written[i]
            assertTrue(NaviLiteCodec.hasMagicAt(frame, 0))
            assertEquals(expectedServiceType, NaviLiteCodec.serviceTypeAt(frame, 0))
            assertArrayEquals(expectedPayload, NaviLiteCodec.payloadAt(frame, 0))
        }
    }

    @Test
    fun `handleEsnUpdate receives ESN_UPDATE frame and returns true`() {
        val esnUpdateFrame = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.ESN_UPDATE,
            payloadDataType = NAVILITE_PDT_VALUE,
            payload = byteArrayOf(1, 2, 3),
        )

        val channel = FakeNavByteChannel()
        channel.feed(esnUpdateFrame)

        val handshake = NavigationHandshake(channel)
        val result = handshake.handleEsnUpdate()

        assertEquals(true, result)
    }

    @Test
    fun `handleEsnUpdate skips unrelated frames sent before ESN_UPDATE`() {
        val unrelatedFrame = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.AUTH_REQUEST, // some other frame first
            payloadDataType = NAVILITE_PDT_VALUE,
            payload = byteArrayOf(),
        )
        val esnUpdateFrame = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.ESN_UPDATE,
            payloadDataType = NAVILITE_PDT_VALUE,
            payload = byteArrayOf(1, 2, 3),
        )

        val channel = FakeNavByteChannel()
        channel.feed(unrelatedFrame + esnUpdateFrame)

        val handshake = NavigationHandshake(channel)

        assertEquals(true, handshake.handleEsnUpdate())
    }

    @Test(expected = IllegalStateException::class)
    fun `handleEsnUpdate throws once the channel closes without ESN_UPDATE ever arriving`() {
        val unrelatedFrame = NaviLiteCodec.build(
            frameType = NAVILITE_FRAME_TYPE_PHONE,
            serviceType = NaviLiteServiceType.AUTH_REQUEST,
            payloadDataType = NAVILITE_PDT_VALUE,
            payload = byteArrayOf(),
        )

        val channel = FakeNavByteChannel()
        channel.feed(unrelatedFrame) // no ESN_UPDATE ever sent

        val handshake = NavigationHandshake(channel)
        handshake.handleEsnUpdate() // should throw once the fake channel runs dry ("channel closed")
    }

    private fun assertTrue(condition: Boolean) {
        org.junit.Assert.assertTrue(condition)
    }
}
