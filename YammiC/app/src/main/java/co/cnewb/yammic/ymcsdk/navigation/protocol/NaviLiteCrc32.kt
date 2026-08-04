package co.cnewb.yammic.ymcsdk.navigation.protocol

/**
 * CRC-32/MPEG-2: poly 0x04C11DB7, init 0xFFFFFFFF, no input/output reflection, xorout 0.
 * Ported from the Pillion reference app's Crc32Mpeg2. Pure function, no I/O.
 */
internal object NaviLiteCrc32 {
    private val table: IntArray = IntArray(256).also { entries ->
        for (index in 0 until 256) {
            var crc = index shl 24
            repeat(8) { crc = if (crc and (1 shl 31) != 0) (crc shl 1) xor 0x04C11DB7 else crc shl 1 }
            entries[index] = crc
        }
    }

    fun compute(data: ByteArray): Int {
        var crc = -1 // 0xFFFFFFFF
        for (byte in data) {
            crc = (crc shl 8) xor table[((crc ushr 24) xor (byte.toInt() and 0xff)) and 0xff]
        }
        return crc
    }
}
