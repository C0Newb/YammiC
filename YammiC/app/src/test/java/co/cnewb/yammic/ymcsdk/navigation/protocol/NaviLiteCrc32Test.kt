package co.cnewb.yammic.ymcsdk.navigation.protocol

import org.junit.Assert.assertEquals
import org.junit.Test

class NaviLiteCrc32Test {
    @Test
    fun `matches the standard CRC-32 MPEG-2 check value`() {
        // "123456789" is the standard CRC catalogue check string; CRC-32/MPEG-2's
        // published check value is 0x0376E6E7 (poly 0x04C11DB7, init 0xFFFFFFFF, no
        // reflection, xorout 0) — verifies the ported table/algorithm independent of
        // any NaviLite-specific behavior.
        val data = "123456789".toByteArray(Charsets.US_ASCII)
        assertEquals(0x0376E6E7, NaviLiteCrc32.compute(data))
    }
}
