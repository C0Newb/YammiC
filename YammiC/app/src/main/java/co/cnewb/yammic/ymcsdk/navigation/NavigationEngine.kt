package co.cnewb.yammic.ymcsdk.navigation

import co.cnewb.yammic.logging.Logger
import co.cnewb.yammic.ymcsdk.navigation.protocol.NAVILITE_FRAME_TYPE_PHONE
import co.cnewb.yammic.ymcsdk.navigation.protocol.NAVILITE_PDT_POINTER
import co.cnewb.yammic.ymcsdk.navigation.protocol.NAVILITE_PDT_VALUE
import co.cnewb.yammic.ymcsdk.navigation.protocol.NaviLiteCodec
import co.cnewb.yammic.ymcsdk.navigation.protocol.NaviLiteContentType
import co.cnewb.yammic.ymcsdk.navigation.protocol.NaviLitePoiDirectionType
import co.cnewb.yammic.ymcsdk.navigation.protocol.NaviLitePoiEntry
import co.cnewb.yammic.ymcsdk.navigation.protocol.NaviLiteServiceType
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeoutOrNull
import kotlin.time.Duration.Companion.milliseconds

/**
 * Owns the IMAGE-frame send loop for one active navigation connection.
 *
 * Fed via [enqueueImage] — a [Channel.CONFLATED] queue, so a burst of pushes only keeps the
 * latest. The sender loop throttles to [maxFps] and gates outstanding sends behind a
 * sliding-window [Semaphore] (`MAX_IN_FLIGHT` permits); a separate loop drains IMAGE_ACK frames
 * off [reader] and releases the window. Does not know how frames are produced or how the
 * underlying socket works — depends only on [NavByteChannel]/[NavFrameReader].
 */
class NavigationEngine(
    private val channel: NavByteChannel,
    private val reader: NavFrameReader,
    private val maxFps: Int,
    private val scope: CoroutineScope,
    logger: Logger,
    private val onFrameSent: (fps: Int, lastFrameKb: Int) -> Unit = { _, _ -> },
    private val onGoHomeRequested: () -> Unit = {},
    private val onGoOfficeRequested: () -> Unit = {},
) {
    private val log = logger.section("NavigationEngine")
    private val frameQueue = Channel<ByteArray>(Channel.CONFLATED)
    private val inFlight = Semaphore(MAX_IN_FLIGHT)

    // Guards channel.write() so the image sender loop and one-off status sends (e.g.
    // sendRoadName) — which run on different coroutines — can never interleave bytes on the
    // same socket.
    private val writeMutex = Mutex()

    private var sequenceNumber = 1 // matches Pillion's MirrorEngine, which starts seq at 1
    private var senderJob: Job? = null
    private var receiveJob: Job? = null

    /** Starts the sender loop and the frame-receive loop, both on [Dispatchers.IO] (blocking I/O). */
    fun start() {
        log.debug("starting sender + receive loops")
        receiveJob = scope.launch(Dispatchers.IO) { receiveLoop() }
        senderJob = scope.launch(Dispatchers.IO) { senderLoop() }
    }

    /** Cancels both loops and closes the frame queue. Does not close the underlying channel. */
    fun stop() {
        log.debug("stopping sender + receive loops")
        senderJob?.cancel()
        receiveJob?.cancel()
        frameQueue.close()
    }

    /** Enqueues raw image bytes (e.g. JPEG) as the next frame to send. */
    fun enqueueImage(image: ByteArray): Boolean = frameQueue.trySend(buildNextPayload(image)).isSuccess

    /**
     * Sends the current road name (CUR_ROAD_NAME_UPDATE, service 3) — a raw UTF-8 string with
     * no length prefix, truncated to [MAX_ROAD_NAME_BYTES] per the reference SDK. Unlike IMAGE
     * frames this isn't ACK'd or window-gated; it's a fire-and-forget status update, same as the
     * handshake's setup burst.
     */
    fun sendRoadName(roadName: String) {
        var payload = roadName.toByteArray(Charsets.UTF_8)
        if (payload.size > MAX_ROAD_NAME_BYTES) payload = payload.copyOf(MAX_ROAD_NAME_BYTES)
        val frame =
            NaviLiteCodec.build(
                frameType = NAVILITE_FRAME_TYPE_PHONE,
                serviceType = NaviLiteServiceType.ROAD,
                payloadDataType = NAVILITE_PDT_POINTER,
                payload = payload,
            )
        scope.launch(Dispatchers.IO) {
            try {
                writeMutex.withLock { channel.write(frame) }
                log.debug("sent ROAD update: \"$roadName\" (${payload.size}B)")
            } catch (t: Throwable) {
                log.debug("sendRoadName: failed to send: ${t.message}")
            }
        }
    }

    private fun buildNextPayload(image: ByteArray): ByteArray {
        val seq = (sequenceNumber++ and 0xFFFF).toShort()
        return buildImagePayload(IMAGE_TYPE_NAVIGATION, seq, image)
    }

    private suspend fun senderLoop() {
        val minIntervalMs = 1000L / maxFps
        var lastSentAtMs = 0L
        try {
            for (payload in frameQueue) {
                val nowMs = System.currentTimeMillis()
                val sinceLastMs = nowMs - lastSentAtMs
                if (lastSentAtMs != 0L && sinceLastMs < minIntervalMs) {
                    delay((minIntervalMs - sinceLastMs).milliseconds)
                }

                val acquired = withTimeoutOrNull(ACK_TIMEOUT_MS.milliseconds) { inFlight.acquire() } != null
                if (!acquired) {
                    log.debug("window stalled (lost ack?) — sending anyway")
                }

                val frame =
                    NaviLiteCodec.build(
                        frameType = NAVILITE_FRAME_TYPE_PHONE,
                        serviceType = NaviLiteServiceType.IMAGE,
                        payloadDataType = NAVILITE_PDT_POINTER,
                        payload = payload,
                    )
                writeMutex.withLock { channel.write(frame) }
                lastSentAtMs = System.currentTimeMillis()
                onFrameSent(maxFps, frame.size / 1024)
                log.debug("sent IMAGE seq=${sequenceNumber - 1} (${frame.size}B, window permits=${inFlight.availablePermits})")
            }
            log.debug("sender loop: frame queue closed, exiting")
        } catch (ce: CancellationException) {
            throw ce
        } catch (t: Throwable) {
            log.debug("sender loop ended: ${t.message}")
        }
    }

    /**
     * Drains every incoming frame on the post-handshake link: IMAGE_ACK releases the sliding
     * window, GO_HOME/GO_OFFICE requests are the CCU's "Go home"/"Go to work" button presses.
     * Every frame is logged — including ones we don't otherwise act on — since several of the
     * CCU's request service-type IDs are "inferred" per the reference protocol docs and may not
     * match this exact hardware/firmware; this is the only way to find out what actually arrives.
     */
    private suspend fun receiveLoop() {
        try {
            while (true) {
                val frame = reader.next()
                when (frame.serviceType) {
                    NaviLiteServiceType.IMAGE_ACK -> {
                        log.debug("received IMAGE_ACK")
                        if (inFlight.availablePermits < MAX_IN_FLIGHT) inFlight.release()
                    }

                    NaviLiteServiceType.GO_HOME_REQUEST -> {
                        log.debug("received GO_HOME_REQUEST")
                        onGoHomeRequested()
                    }

                    NaviLiteServiceType.GO_OFFICE_REQUEST -> {
                        log.debug("received GO_OFFICE_REQUEST")
                        onGoOfficeRequested()
                    }

                    NaviLiteServiceType.START_CONTENT_UPDATE_REQUEST -> {
                        val contentType =
                            frame.payload
                                .getOrNull(0)
                                ?.toInt()
                                ?.and(0xff)
                        log.debug("received START_CONTENT_UPDATE_REQUEST contentType=$contentType")
                        when (contentType) {
                            NaviLiteContentType.FAVORITE_LIST -> {
                                sendFakePoiList(
                                    NaviLiteServiceType.FAV_POI_LIST_UPDATE,
                                    NaviLiteServiceType.FAV_POI_LIST_DATA_UPDATE,
                                    "favorites",
                                )
                            }

                            NaviLiteContentType.STATIONS_LIST -> {
                                sendFakePoiList(
                                    NaviLiteServiceType.GAS_POI_LIST_UPDATE,
                                    NaviLiteServiceType.GAS_POI_LIST_DATA_UPDATE,
                                    "gas stations",
                                )
                            }
                        }
                    }

                    NaviLiteServiceType.STOP_CONTENT_UPDATE_REQUEST -> {
                        log.debug("received STOP_CONTENT_UPDATE_REQUEST contentType=${frame.payload.getOrNull(0)?.toInt()?.and(0xff)}")
                    }

                    else -> {
                        log.debug(
                            "received unhandled frame: serviceType=${frame.serviceType} " +
                                "payload=${frame.payload.toHexPreview()}",
                        )
                    }
                }
            }
        } catch (ce: CancellationException) {
            throw ce
        } catch (t: Throwable) {
            // reader.next() throws once the channel is closed (stop()/deactivate()) — expected exit.
            log.debug("receive loop ended: ${t.message}")
        }
    }

    /**
     * Replies to APP_START_CONTENT_UPDATE_REQUEST with [FAKE_POI_ENTRIES]: a list-size message
     * (`listServiceType`) followed by one DATA_UPDATE frame (`dataServiceType`) per entry, so the
     * CCU's Favorites/gas-station screen has something to render instead of hanging on "loading."
     * Real POI data would need a real source; this is a wire-format smoke test.
     */
    private suspend fun sendFakePoiList(
        listServiceType: Int,
        dataServiceType: Int,
        label: String,
    ) {
        try {
            val listFrame =
                NaviLiteCodec.build(
                    frameType = NAVILITE_FRAME_TYPE_PHONE,
                    serviceType = listServiceType,
                    payloadDataType = NAVILITE_PDT_VALUE,
                    payload = byteArrayOf(FAKE_POI_ENTRIES.size.toByte(), 0),
                )
            writeMutex.withLock { channel.write(listFrame) }
            for (entry in FAKE_POI_ENTRIES) {
                val dataFrame =
                    NaviLiteCodec.build(
                        frameType = NAVILITE_FRAME_TYPE_PHONE,
                        serviceType = dataServiceType,
                        payloadDataType = NAVILITE_PDT_POINTER,
                        payload = entry.toPayload(),
                    )
                writeMutex.withLock { channel.write(dataFrame) }
            }
            log.debug("sent fake $label list (${FAKE_POI_ENTRIES.size} entries)")
        } catch (t: Throwable) {
            log.debug("failed to send fake $label list: ${t.message}")
        }
    }

    /** Hex dump capped at 32 bytes so a large/malformed payload can't flood the log. */
    private fun ByteArray.toHexPreview(): String {
        val hex = take(32).joinToString(" ") { (it.toInt() and 0xff).toString(16).padStart(2, '0') }
        return if (size > 32) "$hex … (${size}B total)" else "$hex (${size}B)"
    }

    companion object {
        private const val MAX_IN_FLIGHT = 2
        private const val ACK_TIMEOUT_MS = 1000L

        // CUR_ROAD_NAME_UPDATE payload is a raw string with no length prefix, max 64 bytes
        // per Pillion's PROTOCOL.md.
        private const val MAX_ROAD_NAME_BYTES = 64

        // Per Pillion's MirrorEngine: imageType 3 = "NAVIGATION_EXPANDED". The CCU keys its
        // rendering mode off this byte — sending 0 here is a different (and wrong) image type.
        private const val IMAGE_TYPE_NAVIGATION: Byte = 3

        // Placeholder entries for the Favorites/gas-station "loading" fix — see sendFakePoiList.
        private val FAKE_POI_ENTRIES =
            listOf(
                NaviLitePoiEntry(0, NaviLitePoiDirectionType.FRONT, 1, "entry 1", "Shit fuck road"),
                NaviLitePoiEntry(1, NaviLitePoiDirectionType.BACK, 2, "entry 2", "Road 2"),
                NaviLitePoiEntry(2, NaviLitePoiDirectionType.RIGHT, 3, "entry 3", "Another one"),
            )

        /**
         * Builds the payload for an image frame to send to the bike.
         * Layout: [imageType][seqLo][seqHi][jpeg]
         */
        fun buildImagePayload(
            imageType: Byte,
            seq: Short,
            jpeg: ByteArray,
        ): ByteArray {
            val payload = ByteArray(3 + jpeg.size)
            payload[0] = imageType
            payload[1] = (seq.toInt() and 0xff).toByte()
            payload[2] = ((seq.toInt() ushr 8) and 0xff).toByte()
            jpeg.copyInto(payload, 3)
            return payload
        }
    }
}
