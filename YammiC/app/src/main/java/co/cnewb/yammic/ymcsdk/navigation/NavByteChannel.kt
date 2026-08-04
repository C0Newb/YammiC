package co.cnewb.yammic.ymcsdk.navigation

/**
 * A bidirectional raw byte link to the bike device. The frame reader and all higher-level
 * protocol handlers depend on this abstraction; platforms provide concrete implementations
 * (e.g., Bluetooth RFCOMM).
 *
 * Ported from Pillion's ByteChannel.
 */
interface NavByteChannel {
    fun open()

    fun write(bytes: ByteArray)

    /**
     * Blocking read of up to [buffer].size bytes.
     * Returns the count of bytes read (>0), or -1 when the channel is closed.
     */
    fun read(buffer: ByteArray): Int

    fun close()
}
