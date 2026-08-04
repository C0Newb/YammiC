package co.cnewb.yammic.ymcsdk.navigation

/**
 * In-memory fake implementation of NavByteChannel for testing.
 * Allows tests to feed bytes and record writes without real transport.
 */
class FakeNavByteChannel : NavByteChannel {
    private var inbound = ByteArray(0)
    private var pos = 0
    val written = mutableListOf<ByteArray>()

    /**
     * Queue bytes to be read by the next read() calls.
     */
    fun feed(bytes: ByteArray) {
        inbound += bytes
    }

    override fun open() {}

    override fun close() {}

    override fun write(bytes: ByteArray) {
        written += bytes
    }

    override fun read(buffer: ByteArray): Int {
        if (pos >= inbound.size) return -1
        val n = minOf(buffer.size, inbound.size - pos)
        inbound.copyInto(buffer, 0, pos, pos + n)
        pos += n
        return n
    }
}
