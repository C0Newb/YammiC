package co.cnewb.yammic.ymcsdk.navigation

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.ImageDecoder
import android.graphics.drawable.AnimatedImageDrawable
import android.graphics.drawable.Drawable
import android.os.Handler
import android.os.HandlerThread
import androidx.annotation.DrawableRes
import co.cnewb.yammic.logging.Logger
import java.io.ByteArrayOutputStream

/**
 * Decodes an animated GIF/WebP drawable resource and drives its playback on a dedicated
 * [HandlerThread] — required by [AnimatedImageDrawable], which schedules its own frame timing via
 * a [Handler]. Each time the drawable invalidates itself (i.e. at the per-frame delay baked into
 * the source file), the current frame is drawn, letterboxed via [letterbox], JPEG-compressed, and
 * handed to [onFrame]. Loops forever (repeat count is forced to infinite) until [stop] is called.
 * A resource that isn't actually animated pushes a single frame and stops.
 */
internal class NavigationAnimatedImagePlayer(
    private val context: Context,
    logger: Logger,
    private val letterbox: (Bitmap) -> Bitmap,
    private val onFrame: (ByteArray) -> Unit,
) {
    private val log = logger.section("NavigationAnimatedImagePlayer")
    private var thread: HandlerThread? = null
    private var handler: Handler? = null
    private var drawable: Drawable? = null

    /** Starts decoding/playing [resId] on a fresh handler thread. Call [stop] first if reusing. */
    fun start(
        @DrawableRes resId: Int,
        quality: Int,
    ) {
        val handlerThread = HandlerThread("NavAnimatedImage").also { it.start() }
        thread = handlerThread
        val threadHandler = Handler(handlerThread.looper)
        handler = threadHandler
        threadHandler.post { decodeAndPlay(resId, quality) }
    }

    /** Stops playback and tears down the handler thread. Safe to call more than once. */
    fun stop() {
        val playingDrawable = drawable as? AnimatedImageDrawable
        handler?.post { playingDrawable?.stop() }
        drawable = null
        thread?.quitSafely()
        thread = null
        handler = null
    }

    private fun decodeAndPlay(
        @DrawableRes resId: Int,
        quality: Int,
    ) {
        val source = ImageDecoder.createSource(context.resources, resId)
        val decoded =
            try {
                ImageDecoder.decodeDrawable(source)
            } catch (t: Throwable) {
                log.debug("decodeAndPlay: failed to decode resource $resId: ${t.message}")
                return
            }
        drawable = decoded

        if (decoded !is AnimatedImageDrawable) {
            log.debug("decodeAndPlay: resource $resId is not animated, pushing single frame")
            emitFrame(decoded, quality)
            return
        }

        decoded.repeatCount = AnimatedImageDrawable.REPEAT_INFINITE
        decoded.setBounds(0, 0, decoded.intrinsicWidth, decoded.intrinsicHeight)
        decoded.callback =
            object : Drawable.Callback {
                override fun invalidateDrawable(who: Drawable) = emitFrame(who, quality)

                override fun scheduleDrawable(
                    who: Drawable,
                    what: Runnable,
                    `when`: Long,
                ) {
                    handler?.postAtTime(what, who, `when`)
                }

                override fun unscheduleDrawable(
                    who: Drawable,
                    what: Runnable,
                ) {
                    handler?.removeCallbacks(what, who)
                }
            }
        decoded.start()
    }

    /** Draws [drawable]'s current frame, letterboxes it, and emits it as JPEG via [onFrame]. */
    private fun emitFrame(
        drawable: Drawable,
        quality: Int,
    ) {
        val width = drawable.intrinsicWidth.coerceAtLeast(1)
        val height = drawable.intrinsicHeight.coerceAtLeast(1)
        val frame = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(frame)
        drawable.setBounds(0, 0, width, height)
        drawable.draw(canvas)

        val canvasBitmap = letterbox(frame)
        try {
            val out = ByteArrayOutputStream()
            canvasBitmap.compress(Bitmap.CompressFormat.JPEG, quality, out)
            onFrame(out.toByteArray())
        } finally {
            if (canvasBitmap !== frame) canvasBitmap.recycle()
            frame.recycle()
        }
    }
}
