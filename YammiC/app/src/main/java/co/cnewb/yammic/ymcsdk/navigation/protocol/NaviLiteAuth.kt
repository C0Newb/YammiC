package co.cnewb.yammic.ymcsdk.navigation.protocol

/**
 * NaviLite authentication — universal, no per-bike key.
 *
 * The CCU's SEC_DATA payload is `("<part-number>" + nonce) XOR 0x0a`. Authenticating
 * means de-obfuscating it and echoing the 4-byte nonce back in SEC_DATA_ACK.
 * Ported from the Pillion reference app's Auth.kt. Pure transform, no I/O.
 */
object NaviLiteAuth {
    private const val OBFUSCATION = 0x0a
    private const val NONCE_LEN = 4

    /** The SEC_DATA_ACK payload = the de-obfuscated 4-byte nonce (last 4 bytes XOR 0x0a). */
    fun secDataAckPayload(secDataPayload: ByteArray): ByteArray =
        ByteArray(NONCE_LEN) { index ->
            (secDataPayload[secDataPayload.size - NONCE_LEN + index].toInt() xor OBFUSCATION).toByte()
        }

    /** The CCU part number the bike announces (de-obfuscated string before the nonce). */
    fun partNumber(secDataPayload: ByteArray): String {
        val partNumberLength = secDataPayload.size - NONCE_LEN
        val chars = CharArray(partNumberLength) { index -> (secDataPayload[index].toInt() xor OBFUSCATION).toChar() }
        return chars.concatToString()
    }
}
