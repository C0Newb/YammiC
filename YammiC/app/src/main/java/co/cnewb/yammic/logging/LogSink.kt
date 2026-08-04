package co.cnewb.yammic.logging

interface LogSink {
    fun write(
        level: LogLevel,
        tag: String,
        message: String,
    )
}
