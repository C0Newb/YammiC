package co.cnewb.yammic.logging

import android.content.ContentValues
import android.content.Context
import android.net.Uri
import android.os.Environment
import android.provider.MediaStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class FileSink(
    private val context: Context,
) : LogSink {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val contentResolver = context.contentResolver
    private val lock = Any()
    private var fileUri: Uri? = null
    private val timestampFormat = SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS", Locale.US)

    override fun write(
        level: LogLevel,
        tag: String,
        message: String,
    ) {
        val entry = "[${timestampFormat.format(Date())}] [${level.prefix}/$tag] $message\n"
        scope.launch {
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
    }

    fun fileLabel(): String = "Downloads/YammiC"

    private fun createLogFile(): Uri {
        val fileName = "yammic-${System.currentTimeMillis()}.log"
        val values =
            ContentValues().apply {
                put(MediaStore.MediaColumns.DISPLAY_NAME, fileName)
                put(MediaStore.MediaColumns.MIME_TYPE, "text/plain")
                put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/YammiC")
            }
        return contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values)
            ?: error("Unable to create log file")
    }
}
