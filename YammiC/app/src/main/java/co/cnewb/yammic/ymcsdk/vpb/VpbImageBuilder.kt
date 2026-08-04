package co.cnewb.yammic.ymcsdk.vpb

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import androidx.core.graphics.createBitmap
import java.io.ByteArrayOutputStream

/**
 * Builds VPB (Virtual Pit Board) image ByteArrays from various sources.
 *
 * The CCU expects a 600×306 JPEG with a 526×66 message strip pasted at (0, 163) on a black
 * background. These helpers handle the composition and JPEG encoding.
 */
object VpbImageBuilder {
    /**
     * Passes through pre-encoded JPEG bytes. Use this when you already have a complete JPEG
     * matching the CCU's expected format (600×306 with composed message strip).
     */
    fun fromJpeg(jpegBytes: ByteArray): ByteArray = jpegBytes

    /**
     * Builds a full VPB image from text and background color. Composes a 526×66 message strip
     * with white text on [backgroundColor], then pastes it onto a 600×306 black canvas at
     * position (0, 163), and compresses to JPEG (quality 100).
     */
    fun fromText(
        text: String,
        backgroundColor: Int = Color.BLUE,
    ): ByteArray {
        val boardStrip = createBitmap(526, 66)
        Canvas(boardStrip).apply {
            drawColor(backgroundColor)
            val paint =
                Paint(Paint.ANTI_ALIAS_FLAG).apply {
                    color = Color.WHITE
                    textSize = 40f
                    textAlign = Paint.Align.CENTER
                }
            val y = (height / 2f) - (paint.descent() + paint.ascent()) / 2f
            drawText(text, width / 2f, y, paint)
        }

        val vehicleImage = createBitmap(600, 306)
        Canvas(vehicleImage).apply {
            drawColor(Color.BLACK)
            drawBitmap(boardStrip, 0f, 163f, null)
        }

        return compressToJpeg(vehicleImage)
    }

    /**
     * Converts a Bitmap to JPEG bytes with default quality (100). Scales the bitmap to 600×306
     * (the CCU's expected VPB size).
     */
    fun fromBitmap(bitmap: Bitmap): ByteArray = fromBitmap(bitmap, jpegQuality = 100)

    /**
     * Converts a Bitmap to JPEG bytes with custom quality (0-100). Scales the bitmap to 600×306
     * (the CCU's expected VPB size).
     */
    fun fromBitmap(
        bitmap: Bitmap,
        jpegQuality: Int,
    ): ByteArray {
        val scaled = Bitmap.createScaledBitmap(bitmap, 600, 306, true)
        return compressToJpeg(scaled, jpegQuality)
    }

    private fun compressToJpeg(
        bitmap: Bitmap,
        jpegQuality: Int = 100,
    ): ByteArray {
        val out = ByteArrayOutputStream()
        bitmap.compress(Bitmap.CompressFormat.JPEG, jpegQuality, out)
        return out.toByteArray()
    }
}
