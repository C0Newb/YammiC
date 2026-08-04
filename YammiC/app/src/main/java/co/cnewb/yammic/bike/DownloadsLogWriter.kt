package co.cnewb.yammic.bike

import android.content.ContentValues
import android.content.Context
import android.os.Environment
import android.provider.MediaStore
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class DownloadsLogWriter(private val context: Context) {
    private val contentResolver = context.contentResolver
    private val lock = Any()
    private var fileUri: android.net.Uri? = null
    private val timestampFormat = SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS", Locale.US)

    suspend fun append(line: String) = withContext(Dispatchers.IO) {
        val entry = "[${timestampFormat.format(Date())}] $line\n"
        synchronized(lock) {
            val uri = fileUri ?: createLogFile().also { fileUri = it }
            try {
                contentResolver.openOutputStream(uri, "wa")?.use { output ->
                    output.write(entry.toByteArray(Charsets.UTF_8))
                    output.flush()
                }
            } catch (_: Exception) {
                // Best-effort logging only.
            }
        }
    }

    fun fileLabel(): String = "Downloads/YammiC"

    private fun createLogFile(): android.net.Uri {
        val fileName = "yammic-${System.currentTimeMillis()}.log"
        val values = ContentValues().apply {
            put(MediaStore.MediaColumns.DISPLAY_NAME, fileName)
            put(MediaStore.MediaColumns.MIME_TYPE, "text/plain")
            put(
                MediaStore.MediaColumns.RELATIVE_PATH,
                Environment.DIRECTORY_DOWNLOADS + "/YammiC"
            )
        }
        return contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values)
            ?: error("Unable to create log file")
    }
}
