package co.cnewb.yammic.ymcsdk

import co.cnewb.yammic.logging.Logger
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import co.cnewb.yammic.ymcsdk.packets.app.AppClearVirtualPitBoard
import co.cnewb.yammic.ymcsdk.packets.app.AppSendVirtualPitBoardGraphic
import co.cnewb.yammic.ymcsdk.packets.app.AppVirtualPitBoardGraphicResponseNg
import co.cnewb.yammic.ymcsdk.packets.app.AppVirtualPitBoardRequest
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuClearVirtualPitBoardResponseNg
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuClearVirtualPitBoardResponseOk
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuSendVirtualPitBoardGraphicResponse
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuVirtualPitBoardGraphicRequest
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuVirtualPitBoardResponseNg
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeoutOrNull
import kotlin.time.Duration.Companion.milliseconds

/**
 * Drives the Virtual Pit Board handshake: request → (graphic request | reject) → chunked graphic
 * send with per-chunk retry → and the separate clear flow. Mirrors the reference SDK's
 * `MpnSettingManager` state machine for `McMpnType.VIRTUAL_PITBOARD` (PhotoFrame/PushNotification
 * share the same machine upstream but aren't implemented here).
 *
 * Only one send (or clear) can be in flight at a time — matches the CCU's single MPN state slot.
 */
class YmcVirtualPitBoardController(
    logger: Logger,
) {
    private val log = logger.section("VirtualPitBoard")
    private val sendMutex = Mutex()

    private var graphicRequestDeferred: CompletableDeferred<Boolean>? = null
    private var chunkAckDeferred: CompletableDeferred<CcuSendVirtualPitBoardGraphicResponse.Response>? = null
    private var clearDeferred: CompletableDeferred<Boolean>? = null

    private var nextContentId = 20101

    /** Registers the CCU-side handlers this controller needs. Call once per connection. */
    fun registerHandlers(messageHandler: YmcMessageHandler) {
        messageHandler.register(YmcCommandCode.VirtualPitBoardResponseNg, ::CcuVirtualPitBoardResponseNg) {
            graphicRequestDeferred?.complete(false)
        }
        messageHandler.register(YmcCommandCode.VirtualPitBoardGraphicRequest, ::CcuVirtualPitBoardGraphicRequest) {
            graphicRequestDeferred?.complete(true)
        }
        messageHandler.register(
            YmcCommandCode.SendVirtualPitBoardGraphicResponse,
            ::CcuSendVirtualPitBoardGraphicResponse,
        ) { packet ->
            chunkAckDeferred?.complete(packet.response)
        }
        messageHandler.register(YmcCommandCode.ClearVirtualPitBoardResponseOk, ::CcuClearVirtualPitBoardResponseOk) {
            clearDeferred?.complete(true)
        }
        messageHandler.register(YmcCommandCode.ClearVirtualPitBoardResponseNg, ::CcuClearVirtualPitBoardResponseNg) {
            clearDeferred?.complete(false)
        }
    }

    /**
     * Sends [image] (already-encoded graphic bytes, e.g. JPEG) to the Virtual Pit Board.
     * Returns true once every chunk was acknowledged, false if the CCU rejected the request or a
     * chunk failed after retries. At most 256 chunks are supported (single-byte chunk index),
     * i.e. images up to [AppSendVirtualPitBoardGraphic.CHUNK_SIZE] * 256 bytes.
     */
    suspend fun sendMessage(
        packetHandler: YmcPacketHandler,
        connectionContext: YmcConnectionContext,
        image: ByteArray,
    ): Boolean =
        sendMutex.withLock {
            if (image.isEmpty()) {
                log.debug("sendMessage: empty image, nothing to send")
                return@withLock false
            }

            val contentId = nextContentId
            nextContentId = if (nextContentId >= Int.MAX_VALUE - 1) 1 else nextContentId + 1

            graphicRequestDeferred = CompletableDeferred()
            AppVirtualPitBoardRequest(packetHandler).send(connectionContext)
            val proceed = withTimeoutOrNull(5_000.milliseconds) { graphicRequestDeferred?.await() } ?: false
            graphicRequestDeferred = null
            if (!proceed) {
                log.debug("sendMessage: CCU rejected VirtualPitBoardRequest or timed out")
                return@withLock false
            }

            val chunkSize = AppSendVirtualPitBoardGraphic.CHUNK_SIZE
            val chunkCount = (image.size + chunkSize - 1) / chunkSize
            if (chunkCount > 256) {
                log.debug("sendMessage: image too large ($chunkCount chunks > 256), aborting")
                AppVirtualPitBoardGraphicResponseNg(packetHandler).send(connectionContext)
                return@withLock false
            }
            val totalIndex = (chunkCount - 1).toByte()

            for (index in 0 until chunkCount) {
                val start = index * chunkSize
                val end = minOf(start + chunkSize, image.size)
                val chunk = image.copyOfRange(start, end)

                var attempt = 0
                var success = false
                while (attempt < 3 && !success) {
                    chunkAckDeferred = CompletableDeferred()
                    AppSendVirtualPitBoardGraphic(
                        packetHandler,
                        contentId,
                        index.toByte(),
                        totalIndex,
                        image.size,
                        chunk,
                    ).send(connectionContext)
                    val response =
                        withTimeoutOrNull(5_000.milliseconds) { chunkAckDeferred?.await() }
                            ?: CcuSendVirtualPitBoardGraphicResponse.Response.FailureNotRetry
                    chunkAckDeferred = null
                    when (response) {
                        CcuSendVirtualPitBoardGraphicResponse.Response.Success -> {
                            success = true
                        }

                        CcuSendVirtualPitBoardGraphicResponse.Response.FailureRetry -> {
                            attempt++
                        }

                        else -> {
                            log.debug("sendMessage: chunk $index aborted (response=$response)")
                            return@withLock false
                        }
                    }
                }
                if (!success) {
                    log.debug("sendMessage: chunk $index failed after retries")
                    return@withLock false
                }
            }
            true
        }

    /** Clears the Virtual Pit Board display. Returns true on `ClearVirtualPitBoardResponseOk`. */
    suspend fun clear(
        packetHandler: YmcPacketHandler,
        connectionContext: YmcConnectionContext,
    ): Boolean {
        clearDeferred = CompletableDeferred()
        AppClearVirtualPitBoard(packetHandler).send(connectionContext)
        val result = withTimeoutOrNull(5_000.milliseconds) { clearDeferred?.await() } ?: false
        clearDeferred = null
        return result
    }
}
