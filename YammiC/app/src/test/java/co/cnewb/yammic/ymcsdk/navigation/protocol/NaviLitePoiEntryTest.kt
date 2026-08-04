package co.cnewb.yammic.ymcsdk.navigation.protocol

import org.junit.Assert.assertEquals
import org.junit.Test

class NaviLitePoiEntryTest {
    @Test
    fun `toPayload lays out fields in the order confirmed from the native packer`() {
        val entry = NaviLitePoiEntry(
            index = 2,
            direction = NaviLitePoiDirectionType.RIGHT,
            distance = 3,
            name = "entry 3",
            road = "3 miles east",
        )

        val payload = entry.toPayload()

        assertEquals(2, payload[0].toInt()) // index
        assertEquals(0, payload[1].toInt()) // unknown field
        assertEquals(NaviLitePoiDirectionType.RIGHT, payload[2]) // direction
        assertEquals("entry 3".length, payload[3].toInt()) // len(name)
        assertEquals("3 miles east".length, payload[4].toInt()) // len(road)
        assertEquals(3, payload[5].toInt()) // distance

        val roadLen = payload[4].toInt()
        val nameLen = payload[3].toInt()
        val road = String(payload, 6, roadLen, Charsets.US_ASCII)
        val name = String(payload, 6 + roadLen, nameLen, Charsets.US_ASCII)
        assertEquals("3 miles east", road)
        assertEquals("entry 3", name)
        assertEquals(6 + roadLen + nameLen, payload.size)
    }
}
