package co.cnewb.yammic.ymcsdk

import java.util.UUID

internal object YmcBikeProtocol {
    // ── Service and characteristic UUIDs ─────────────────────────────────────

    val serviceUuid: UUID = UUID.fromString("AFA2CDF4-ECCF-46A7-A5EA-9DA428C0157A")

    /** Used with the DDT session-start handshake (announce total payload size). */
    val controlUuid: UUID = UUID.fromString("56851826-A090-4F6A-A5DF-D1776520F74A")

    /** DDT status notifications arrive here (active ACK and completion signal). */
    val statusUuid: UUID = UUID.fromString("75AB2788-87F6-4F61-BA9C-6F85A12600D1")

    /** DDT data chunks are written here. */
    val dataUuid: UUID = UUID.fromString("E998AEAB-EA8F-4063-83AA-2017C1A44484")

    /**
     * Simple (non-DDT) outgoing commands are written here.
     * Used for: weather response, language response, alive-check response, IdentifyAppRequest.
     * If the CCU rejects writes to this UUID try [dataUuid] instead.
     */
    val simpleWriteUuid: UUID = UUID.fromString("B606C7F9-E5A1-4E75-B313-2A920054A8EB")

    /** Most CCU → phone responses arrive as notifications on this characteristic. */
    val mainRxUuid: UUID = UUID.fromString("9C810D26-B605-4306-8C1C-755A1BA3066C")

    // ── App identity ─────────────────────────────────────────────────────────

    /** Y-Connect's registered app ID (0x1234 = 4660). Use the same value. */
    private const val APP_ID = 4660

    // ── Helper Functions ─────────────────────────────────────────────────────

    /** Maximum data bytes per chunk given the negotiated MTU. */
    fun payloadChunkSize(negotiatedMtu: Int): Int = (negotiatedMtu - 7).coerceAtLeast(1)

    /**
     * First packet written to [controlUuid] to announce a DDT session.
     * Tells the CCU how many bytes are coming.
     */
    fun sessionStartPacket(
        requestId: Byte,
        payloadSize: Int,
    ): ByteArray =
        byteArrayOf(
            requestId,
            ((payloadSize shr 16) and 0xFF).toByte(),
            ((payloadSize shr 8) and 0xFF).toByte(),
            (payloadSize and 0xFF).toByte(),
        )

    /**
     * Each data chunk written to [dataUuid] during a DDT session.
     * Header: [requestId][offset high][offset mid][offset low].
     */
    fun chunkPacket(
        requestId: Byte,
        offset: Int,
        chunk: ByteArray,
    ): ByteArray =
        byteArrayOf(
            requestId,
            ((offset shr 16) and 0xFF).toByte(),
            ((offset shr 8) and 0xFF).toByte(),
            (offset and 0xFF).toByte(),
        ) + chunk

    /**
     * Frames a command payload for the Classic-Bluetooth SPP socket: `[0x01][len 4B BE][payload][0x04]`.
     * Mirrors the reference SDK's OTA-socket framing (also used for the initial ccuId/passKey handoff).
     */
    fun sppFramePacket(payload: ByteArray): ByteArray =
        byteArrayOf(0x01) +
            byteArrayOf(
                ((payload.size shr 24) and 0xFF).toByte(),
                ((payload.size shr 16) and 0xFF).toByte(),
                ((payload.size shr 8) and 0xFF).toByte(),
                (payload.size and 0xFF).toByte(),
            ) +
            payload +
            byteArrayOf(0x04)

    fun concatPackets(vararg arrays: ByteArray): ByteArray {
        val total = arrays.sumOf { it.size }
        val out = ByteArray(total)
        var index = 0
        for (array in arrays) {
            array.copyInto(out, index)
            index += array.size
        }
        return out
    }
}
