package co.cnewb.yammic.logging

import android.util.Log

class LogcatSink : LogSink {
    override fun write(level: LogLevel, tag: String, message: String) {
        when (level) {
            LogLevel.DEBUG -> Log.d(tag, message)
            LogLevel.INFO -> Log.i(tag, message)
            LogLevel.WARN -> Log.w(tag, message)
            LogLevel.ERROR -> Log.e(tag, message)
        }
    }
}
