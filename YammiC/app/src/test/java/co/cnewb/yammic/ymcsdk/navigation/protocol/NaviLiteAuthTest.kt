package co.cnewb.yammic.ymcsdk.navigation.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Test

class NaviLiteAuthTest {
    @Test
    fun `partNumber and secDataAckPayload de-obfuscate a self-built SEC_DATA payload`() {
        // The transform is XOR 0x0a, its own inverse, so we can build a fake SEC_DATA
        // payload by obfuscating known plaintext and confirm both accessors recover it.
        val partNumber = "1KS-H2540-00"
        val nonce = byteArrayOf(0x11, 0x22, 0x33, 0x44)
        val obfuscated = (partNumber.map { it.code.toByte() } + nonce.toList())
            .map { (it.toInt() xor 0x0a).toByte() }
            .toByteArray()

        assertEquals(partNumber, NaviLiteAuth.partNumber(obfuscated))
        assertArrayEquals(nonce, NaviLiteAuth.secDataAckPayload(obfuscated))
    }

    @Test
    fun `de-obfuscates real SEC_DATA vectors captured from a Yamaha CCU`() {
        // Captured wire vectors from Pillion's ProtocolTest — same CCU part number, three
        // different session nonces. Confirms our port matches real hardware, not just itself.
        val cases = listOf(
            "3a3a3c27483e3b3c3a273a3a" + "baaa4d5c" to "b0a04756",
            "3a3a3c27483e3b3c3a273a3a" + "c12b8a7b" to "cb218071",
            "3a3a3c27483e3b3c3a273a3a" + "43719e55" to "497b945f",
        )
        for ((seedHex, expectedNonceHex) in cases) {
            val seed = hexToBytes(seedHex)
            assertEquals("006-B4160-00", NaviLiteAuth.partNumber(seed))
            assertEquals(expectedNonceHex, NaviLiteAuth.secDataAckPayload(seed).toHexString())
        }
    }

    private fun hexToBytes(s: String): ByteArray =
        s.chunked(2).map { it.toInt(16).toByte() }.toByteArray()

    private fun ByteArray.toHexString(): String =
        joinToString("") { (it.toInt() and 0xff).toString(16).padStart(2, '0') }
}
