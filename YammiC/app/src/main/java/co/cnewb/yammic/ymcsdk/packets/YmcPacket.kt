package co.cnewb.yammic.ymcsdk.packets

abstract class YmcPacket {
    constructor(commandCode: YmcCommandCode, packetHandler: YmcPacketHandler) {
        this.packetHandler = packetHandler
        this.commandCode = commandCode.code
    }

    protected var packetHandler: YmcPacketHandler

    /**
     * Packet id.
     */
    val commandCode: Int

    // -- Helpers --

    protected fun stringBytes(
        value: String,
        maxBytes: Int? = null,
    ): ByteArray {
        val bytes = value.toByteArray(Charsets.UTF_8)
        return if (maxBytes == null || bytes.size <= maxBytes) bytes else bytes.copyOf(maxBytes)
    }

    protected fun int24(value: Int): ByteArray =
        byteArrayOf(
            ((value shr 16) and 0xFF).toByte(),
            ((value shr 8) and 0xFF).toByte(),
            (value and 0xFF).toByte(),
        )

    protected fun shortBytes(value: Int): ByteArray {
        val safe = value and 0xFFFF
        return byteArrayOf(((safe shr 8) and 0xFF).toByte(), (safe and 0xFF).toByte())
    }

    protected fun intBytes(value: Int): ByteArray =
        byteArrayOf(
            ((value shr 24) and 0xFF).toByte(),
            ((value shr 16) and 0xFF).toByte(),
            ((value shr 8) and 0xFF).toByte(),
            (value and 0xFF).toByte(),
        )
}
