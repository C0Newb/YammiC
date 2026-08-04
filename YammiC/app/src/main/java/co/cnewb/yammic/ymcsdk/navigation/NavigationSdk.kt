package co.cnewb.yammic.ymcsdk.navigation

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import androidx.annotation.DrawableRes
import co.cnewb.yammic.logging.Logger
import co.cnewb.yammic.ymcsdk.YmcConnectionBroker
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.CancellationException
import java.io.ByteArrayOutputStream
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.time.Duration.Companion.milliseconds

/**
 * Public facade for the navigation-screen link: activate()/deactivate() the RFCOMM connection,
 * pushImage() to render a frame. Depends on [YmcConnectionBroker] only to read
 * [YmcConnectionBroker.isBikeConnected] as a precondition — the RFCOMM link itself is
 * independent of the BLE connection the broker manages.
 */
class NavigationSdk(
    private val context: Context,
    private val controller: YmcConnectionBroker,
    private val maxFps: Int = 15,
    private val scope: CoroutineScope,
    logger: Logger,
    // Rate at which the last-pushed image is silently re-sent while nothing new comes in, so the
    // CCU keeps seeing a live feed instead of one frame followed by silence. Kept low (1-5fps)
    // since the content isn't actually changing.
    private val idleResendFps: Int = 2,
    private val channelFactory: () -> NavByteChannel = { NavRfcommChannel(logger) },
) {
    private val log = logger.section("NavigationSdk")

    private val _state = MutableStateFlow<NavigationState>(NavigationState.Idle)
    val state = _state.asStateFlow()

    // Guards entry into the connecting/active region. Flipped synchronously on the calling
    // thread, before any coroutine is launched, so two rapid activate() calls can never both
    // pass the guard and open two RFCOMM sockets.
    private val activeOrConnecting = AtomicBoolean(false)

    private var channel: NavByteChannel? = null
    private var engine: NavigationEngine? = null

    // The in-flight activate() coroutine, so deactivate() can cancel a connection attempt that
    // hasn't finished the handshake yet (otherwise it would complete after deactivate() and
    // silently resurrect the channel/engine/Active state).
    private var job: Job? = null

    // The most recently pushed image, re-sent by the idle-resend loop below.
    @Volatile private var lastImageBytes: ByteArray? = null
    private var idleResendJob: Job? = null

    // Set while an animated image (pushAnimatedImage) is playing; stopped whenever a static
    // frame is pushed or the connection is deactivated.
    private var animatedImagePlayer: NavigationAnimatedImagePlayer? = null

    /** Fired when the rider taps "Go home" on the CCU. Read dynamically by the engine's
     *  receive loop, so it may be set before or after [activate] is called. */
    var onGoHomeRequested: (() -> Unit)? = null

    /** Fired when the rider taps "Go to work" on the CCU. See [onGoHomeRequested]. */
    var onGoToWorkRequested: (() -> Unit)? = null

    /** No-ops if already connecting/active. Errors if the bike isn't BLE-connected yet. Launches async. */
    fun activate() {
        if (!activeOrConnecting.compareAndSet(false, true)) {
            log.debug("activate: already connecting/active, ignoring")
            return
        }
        if (!controller.isBikeConnected) {
            activeOrConnecting.set(false)
            _state.value = NavigationState.Error("bike not connected")
            return
        }
        _state.value = NavigationState.Connecting
        job = scope.launch {
            var newChannel: NavByteChannel? = null
            try {
                newChannel = channelFactory()
                val connectedChannel = newChannel
                val handshake = withContext(Dispatchers.IO) {
                    connectedChannel.open()
                    // No artificial timeout here: matching Pillion's Handshake.perform(), each
                    // step blocks on the underlying blocking socket read and only unblocks via a
                    // real I/O failure/exception (e.g. deactivate() closing the channel).
                    val handshake = NavigationHandshake(connectedChannel, log)
                    handshake.execute()
                    handshake
                }
                // Publish Active before starting the engine so a fast first onFrameSent callback
                // can't be clobbered by a stale assignment made after engine.start() returns.
                _state.value = NavigationState.Active(fps = maxFps, lastFrameKb = 0)
                channel = connectedChannel
                engine = NavigationEngine(
                    channel = connectedChannel,
                    reader = handshake.reader,
                    maxFps = maxFps,
                    scope = scope,
                    logger = log,
                    onFrameSent = { fps, lastFrameKb ->
                        _state.value = NavigationState.Active(fps = fps, lastFrameKb = lastFrameKb)
                    },
                    onGoHomeRequested = { onGoHomeRequested?.invoke() },
                    onGoOfficeRequested = { onGoToWorkRequested?.invoke() },
                ).also { it.start() }
                startIdleResendLoop()
                log.debug("activate: handshake complete, streaming")
            } catch (ce: CancellationException) {
                runCatching { newChannel?.close() }
                throw ce
            } catch (t: Throwable) {
                log.debug("activate: failed: ${t.message}")
                runCatching { newChannel?.close() }
                _state.value = NavigationState.Error(t.message ?: "connection failed")
            } finally {
                activeOrConnecting.set(false)
            }
        }
    }

    /** Stops the send loop and closes the RFCOMM socket. Safe to call when already idle. */
    fun deactivate() {
        job?.cancel()
        job = null
        idleResendJob?.cancel()
        idleResendJob = null
        stopAnimatedImage()
        engine?.stop()
        engine = null
        runCatching { channel?.close() }
        channel = null
        activeOrConnecting.set(false)
        _state.value = NavigationState.Idle
    }

    /**
     * Re-sends [lastImageBytes] at [idleResendFps] for as long as the connection is active, so
     * a static (unchanging) scene still looks like a live feed to the CCU instead of one frame
     * followed by silence. No-ops each tick if nothing has been pushed yet.
     */
    private fun startIdleResendLoop() {
        val intervalMs = 1000L / idleResendFps.coerceAtLeast(1)
        idleResendJob = scope.launch {
            while (true) {
                delay(intervalMs.milliseconds)
                lastImageBytes?.let { engine?.enqueueImage(it) }
            }
        }
    }

    /** Sets the road name shown on the CCU's navigation screen. No-ops (logged) if not Active. */
    fun setRoadName(roadName: String) {
        val currentEngine = engine
        if (currentEngine == null) {
            log.debug("setRoadName: not active, dropping")
            return
        }
        currentEngine.sendRoadName(roadName)
    }

    /**
     * Enqueues already-encoded image bytes as the next frame and remembers it as the frame the
     * idle-resend loop keeps re-sending. No-ops the send (logged) if not Active — the image is
     * still remembered so it goes out once the connection becomes Active. Stops any animation
     * started via [pushAnimatedImage] — a manual push always takes over.
     */
    fun pushImage(image: ByteArray) {
        stopAnimatedImage()
        pushFrame(image)
    }

    /** Same as [pushImage] but doesn't touch [animatedImagePlayer] — used by its own frames. */
    private fun pushFrame(image: ByteArray) {
        lastImageBytes = image
        val currentEngine = engine
        if (currentEngine == null) {
            log.debug("pushImage: not active, dropping")
            return
        }
        currentEngine.enqueueImage(image)
    }

    /**
     * Decodes [drawableRes] as an animated GIF or WebP and continuously pushes its frames,
     * timed by the delays baked into the source file, looping forever until replaced by another
     * [pushImage]/[pushAnimatedImage] call or [deactivate]. Each frame is letterboxed onto the
     * navigation canvas and JPEG-compressed at [quality] the same way as [pushImage]. The
     * underlying send loop already paces frames to [maxFps] and conflates a fast-arriving
     * backlog to just the latest, so a source with faster-than-maxFps timing is naturally
     * throttled without dropping the animation's own pacing.
     * If [drawableRes] isn't actually animated, a single frame is pushed instead.
     */
    fun pushAnimatedImage(
        @DrawableRes drawableRes: Int,
        quality: Int = 90,
    ) {
        stopAnimatedImage()
        val player =
            NavigationAnimatedImagePlayer(
                context = context,
                logger = log,
                letterbox = ::letterboxToNavigationCanvas,
                onFrame = ::pushFrame,
            )
        animatedImagePlayer = player
        player.start(drawableRes, quality)
    }

    /** Stops any animation started via [pushAnimatedImage]. No-op if none is playing. */
    private fun stopAnimatedImage() {
        animatedImagePlayer?.stop()
        animatedImagePlayer = null
    }

    /**
     * Decodes [drawableRes], letterboxes it onto the CCU's expected 480x240 navigation canvas
     * (the dash renders 480x240 or 480x236 — see Pillion's PROTOCOL.md "Image channel"; any
     * other resolution is not guaranteed to display), and re-compresses to JPEG at [quality]
     * (0-100) before pushing.
     */
    fun pushImage(@DrawableRes drawableRes: Int, quality: Int = 90) {
        val source = BitmapFactory.decodeResource(context.resources, drawableRes)
        if (source == null) {
            log.debug("pushImage: failed to decode drawable $drawableRes")
            return
        }
        val canvasBitmap = letterboxToNavigationCanvas(source)
        try {
            val out = ByteArrayOutputStream()
            canvasBitmap.compress(Bitmap.CompressFormat.JPEG, quality, out)
            pushImage(out.toByteArray())
        } finally {
            canvasBitmap.recycle()
            source.recycle()
        }
    }

    /** Scales [source] to fit within [NAV_CANVAS_WIDTH]x[NAV_CANVAS_HEIGHT] and centers it on a
     *  black canvas of exactly that size, preserving aspect ratio instead of stretching. */
    private fun letterboxToNavigationCanvas(source: Bitmap): Bitmap {
        val canvasBitmap = Bitmap.createBitmap(NAV_CANVAS_WIDTH, NAV_CANVAS_HEIGHT, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(canvasBitmap)
        canvas.drawColor(Color.BLACK)

        val scale = minOf(
            NAV_CANVAS_WIDTH.toFloat() / source.width,
            NAV_CANVAS_HEIGHT.toFloat() / source.height,
        )
        val scaledWidth = (source.width * scale).toInt().coerceAtLeast(1)
        val scaledHeight = (source.height * scale).toInt().coerceAtLeast(1)
        val scaled = Bitmap.createScaledBitmap(source, scaledWidth, scaledHeight, true)
        try {
            val left = (NAV_CANVAS_WIDTH - scaledWidth) / 2f
            val top = (NAV_CANVAS_HEIGHT - scaledHeight) / 2f
            canvas.drawBitmap(scaled, left, top, null)
        } finally {
            if (scaled !== source) scaled.recycle()
        }
        return canvasBitmap
    }

    private companion object {
        // The CCU's navigation display expects 480x240 (or 480x236) JPEG frames — see Pillion's
        // PROTOCOL.md "Image channel".
        const val NAV_CANVAS_WIDTH = 480
        const val NAV_CANVAS_HEIGHT = 240
    }
}
