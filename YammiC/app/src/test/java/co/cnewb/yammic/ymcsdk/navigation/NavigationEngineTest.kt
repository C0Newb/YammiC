package co.cnewb.yammic.ymcsdk.navigation

import co.cnewb.yammic.logging.Logger
import co.cnewb.yammic.ymcsdk.navigation.protocol.NAVILITE_FRAME_TYPE_PHONE
import co.cnewb.yammic.ymcsdk.navigation.protocol.NAVILITE_PDT_VALUE
import co.cnewb.yammic.ymcsdk.navigation.protocol.NaviLiteCodec
import co.cnewb.yammic.ymcsdk.navigation.protocol.NaviLiteServiceType
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Job
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Test

class NavigationEngineTest {
    @Test
    fun `buildImagePayload constructs correct byte layout`() {
        // Arrange
        val imageType = 0x42.toByte()
        val seq: Short = 1000
        val jpeg = byteArrayOf(0xAA.toByte(), 0xBB.toByte(), 0xCC.toByte())

        // Act
        val payload = NavigationEngine.buildImagePayload(imageType, seq, jpeg)

        // Assert
        assertEquals(6, payload.size) // 1 byte type + 2 byte seq LE16 + 3 bytes jpeg
        assertEquals(imageType, payload[0])
        assertEquals((1000 and 0xff).toByte(), payload[1]) // seq low byte
        assertEquals(((1000 ushr 8) and 0xff).toByte(), payload[2]) // seq high byte
        assertEquals(0xAA.toByte(), payload[3])
        assertEquals(0xBB.toByte(), payload[4])
        assertEquals(0xCC.toByte(), payload[5])
    }

    @Test
    fun `buildImagePayload handles edge case sequences`() {
        // Arrange
        val imageType = 0x01.toByte()
        val seqMax: Short = 65535.toShort() // Max 16-bit value
        val jpeg = ByteArray(0) // Empty jpeg

        // Act
        val payload = NavigationEngine.buildImagePayload(imageType, seqMax, jpeg)

        // Assert
        assertEquals(3, payload.size) // 1 byte type + 2 byte seq LE16 + 0 bytes jpeg
        assertEquals(imageType, payload[0])
        assertEquals((65535 and 0xff).toByte(), payload[1]) // 0xFF
        assertEquals(((65535 ushr 8) and 0xff).toByte(), payload[2]) // 0xFF
    }

    @Test
    fun `receive loop dispatches GO_HOME_REQUEST to onGoHomeRequested`() = runBlocking {
        val channel = FakeNavByteChannel()
        channel.feed(
            NaviLiteCodec.build(
                frameType = NAVILITE_FRAME_TYPE_PHONE,
                serviceType = NaviLiteServiceType.GO_HOME_REQUEST,
                payloadDataType = NAVILITE_PDT_VALUE,
                payload = byteArrayOf(0),
            ),
        )
        val goHomeCalled = CompletableDeferred<Unit>()
        val engine = NavigationEngine(
            channel = channel,
            reader = NavFrameReader(channel),
            maxFps = 15,
            scope = this,
            logger = Logger(tag = "test", sinks = emptyList()),
            onGoHomeRequested = { goHomeCalled.complete(Unit) },
        )

        engine.start()
        withTimeout(2000) { goHomeCalled.await() }
        engine.stop()
    }

    @Test
    fun `receive loop dispatches GO_OFFICE_REQUEST to onGoOfficeRequested`() = runBlocking {
        val channel = FakeNavByteChannel()
        channel.feed(
            NaviLiteCodec.build(
                frameType = NAVILITE_FRAME_TYPE_PHONE,
                serviceType = NaviLiteServiceType.GO_OFFICE_REQUEST,
                payloadDataType = NAVILITE_PDT_VALUE,
                payload = byteArrayOf(0),
            ),
        )
        val goOfficeCalled = CompletableDeferred<Unit>()
        val engine = NavigationEngine(
            channel = channel,
            reader = NavFrameReader(channel),
            maxFps = 15,
            scope = this,
            logger = Logger(tag = "test", sinks = emptyList()),
            onGoOfficeRequested = { goOfficeCalled.complete(Unit) },
        )

        engine.start()
        withTimeout(2000) { goOfficeCalled.await() }
        engine.stop()
    }

    @Test
    fun `sendRoadName writes a CUR_ROAD_NAME_UPDATE frame with the raw UTF-8 name`() = runBlocking {
        val channel = FakeNavByteChannel()
        val engine = NavigationEngine(
            channel = channel,
            reader = NavFrameReader(channel),
            maxFps = 15,
            scope = this,
            logger = Logger(tag = "test", sinks = emptyList()),
        )

        engine.sendRoadName("This is a test")
        currentCoroutineContext()[Job]!!.children.toList().forEach { it.join() }

        assertEquals(1, channel.written.size)
        val frame = channel.written[0]
        assertEquals(NaviLiteServiceType.ROAD, NaviLiteCodec.serviceTypeAt(frame, 0))
        assertArrayEquals("This is a test".toByteArray(Charsets.UTF_8), NaviLiteCodec.payloadAt(frame, 0))
    }

    @Test
    fun `sendRoadName truncates names longer than 64 bytes`() = runBlocking {
        val channel = FakeNavByteChannel()
        val engine = NavigationEngine(
            channel = channel,
            reader = NavFrameReader(channel),
            maxFps = 15,
            scope = this,
            logger = Logger(tag = "test", sinks = emptyList()),
        )
        val longName = "x".repeat(100)

        engine.sendRoadName(longName)
        currentCoroutineContext()[Job]!!.children.toList().forEach { it.join() }

        val frame = channel.written[0]
        assertEquals(64, NaviLiteCodec.payloadAt(frame, 0).size)
    }

    @Test
    fun `START_CONTENT_UPDATE_REQUEST for favorites gets a 3-entry FAV_POI_LIST_UPDATE reply`() = runBlocking {
        val channel = FakeNavByteChannel()
        channel.feed(
            NaviLiteCodec.build(
                frameType = NAVILITE_FRAME_TYPE_PHONE,
                serviceType = NaviLiteServiceType.START_CONTENT_UPDATE_REQUEST,
                payloadDataType = NAVILITE_PDT_VALUE,
                payload = byteArrayOf(3, 0), // contentType=3 (FAVORITE_LIST)
            ),
        )
        val engine = NavigationEngine(
            channel = channel,
            reader = NavFrameReader(channel),
            maxFps = 15,
            scope = this,
            logger = Logger(tag = "test", sinks = emptyList()),
        )

        engine.start()
        withTimeout(2000) {
            while (channel.written.size < 4) kotlinx.coroutines.yield()
        }
        engine.stop()

        val listFrame = channel.written[0]
        assertEquals(NaviLiteServiceType.FAV_POI_LIST_UPDATE, NaviLiteCodec.serviceTypeAt(listFrame, 0))
        assertArrayEquals(byteArrayOf(3, 0), NaviLiteCodec.payloadAt(listFrame, 0))

        val firstEntry = channel.written[1]
        assertEquals(NaviLiteServiceType.FAV_POI_LIST_DATA_UPDATE, NaviLiteCodec.serviceTypeAt(firstEntry, 0))
        val payload = NaviLiteCodec.payloadAt(firstEntry, 0)
        assertEquals(0, payload[0].toInt()) // index
        assertEquals(1, payload[2].toInt()) // direction = FRONT
        val nameLen = payload[3].toInt()
        val roadLen = payload[4].toInt()
        val road = String(payload, 6, roadLen, Charsets.US_ASCII)
        val name = String(payload, 6 + roadLen, nameLen, Charsets.US_ASCII)
        assertEquals("entry 1", name)
        assertEquals(6 + roadLen + nameLen, payload.size) // road+name lengths account for the whole payload
    }

    @Test
    fun `START_CONTENT_UPDATE_REQUEST for gas stations gets a 3-entry GAS_POI_LIST_UPDATE reply`() = runBlocking {
        val channel = FakeNavByteChannel()
        channel.feed(
            NaviLiteCodec.build(
                frameType = NAVILITE_FRAME_TYPE_PHONE,
                serviceType = NaviLiteServiceType.START_CONTENT_UPDATE_REQUEST,
                payloadDataType = NAVILITE_PDT_VALUE,
                payload = byteArrayOf(4, 0), // contentType=4 (STATIONS_LIST)
            ),
        )
        val engine = NavigationEngine(
            channel = channel,
            reader = NavFrameReader(channel),
            maxFps = 15,
            scope = this,
            logger = Logger(tag = "test", sinks = emptyList()),
        )

        engine.start()
        withTimeout(2000) {
            while (channel.written.size < 4) kotlinx.coroutines.yield()
        }
        engine.stop()

        val listFrame = channel.written[0]
        assertEquals(NaviLiteServiceType.GAS_POI_LIST_UPDATE, NaviLiteCodec.serviceTypeAt(listFrame, 0))
        assertArrayEquals(byteArrayOf(3, 0), NaviLiteCodec.payloadAt(listFrame, 0))
        assertEquals(4, channel.written.size)
        for (i in 1..3) {
            assertEquals(NaviLiteServiceType.GAS_POI_LIST_DATA_UPDATE, NaviLiteCodec.serviceTypeAt(channel.written[i], 0))
        }
    }
}
