package co.cnewb.yammic.logging

class Logger(
    val tag: String,
    private val sinks: List<LogSink>,
) {
    fun section(name: String): Logger = Logger(tag = "$tag/$name", sinks = sinks)

    fun debug(message: String) = write(LogLevel.DEBUG, message)

    fun info(message: String) = write(LogLevel.INFO, message)

    fun warn(message: String) = write(LogLevel.WARN, message)

    fun error(message: String) = write(LogLevel.ERROR, message)

    fun error(
        message: String,
        throwable: Throwable,
    ) = write(LogLevel.ERROR, "$message\n${throwable.stackTraceToString()}")

    private fun write(
        level: LogLevel,
        message: String,
    ) {
        sinks.forEach { it.write(level, tag, message) }
    }
}
