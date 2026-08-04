package co.cnewb.yammic.ymcsdk

import co.cnewb.yammic.logging.Logger
import co.cnewb.yammic.ymcsdk.handlers.CcuRequestHandler
import co.cnewb.yammic.ymcsdk.packets.GattNotificationEvent
import co.cnewb.yammic.ymcsdk.packets.MalformedPacketException
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuIncomingPacket
import kotlinx.coroutines.CoroutineExceptionHandler
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.launch

/**
 * Processes and handles incoming packets from the CCU.
 * Forwards deserialize requests and responses to their respective handlers.
 */
class YmcMessageHandler(
    private val logger: Logger,
    private val packetHandler: YmcPacketHandler,
    private val scope: CoroutineScope,
    private val notifications: SharedFlow<GattNotificationEvent>,
) {
    /** Message handler task */
    private var messageHandlerJob: Job? = null

    private val log = logger.section("MessageHandler")
    private var connectionContext: YmcConnectionContext? = null

    // Handlers are suspend so they run sequentially inside the collect coroutine.
    // This prevents concurrent handlers from interleaving their BLE writes
    // (e.g. a datetime response being injected mid-weather sequence).
    private val handlers = mutableMapOf<Int, MutableMap<Any, suspend (ByteArray) -> Unit>>()

    /**
     * Registers a handler for a specific command (request or response [YmcCommandCode]).
     *
     * @param commandCode Command ID to register for.
     * @param factory Class for the command (request or response), such as `::CcuPingRequest`.
     * @param handler Function that should handle (process) the command (request or response instance).
     */
    fun <T : CcuIncomingPacket> register(
        commandCode: YmcCommandCode,
        factory: (YmcPacketHandler, ByteArray) -> T,
        handler: (T) -> Unit,
    ) {
        val wrapped: suspend (ByteArray) -> Unit = { bytes -> handler(factory(packetHandler, bytes)) }
        handlers.getOrPut(commandCode.code) { mutableMapOf() }[handler] = wrapped
    }

    /**
     * Registers a [CcuRequestHandler] for a specific command [YmcCommandCode].
     *
     * @param commandCode Command ID to register for.
     * @param factory Class for the command, such as `::CcuGetDeviceLanguageRequest`.
     * @param handler [CcuRequestHandler] instance to handle the command.
     */
    fun <T : CcuIncomingPacket> registerHandler(
        commandCode: YmcCommandCode,
        factory: (YmcPacketHandler, ByteArray) -> T,
        handler: CcuRequestHandler<T>,
    ) {
        val wrapped: suspend (ByteArray) -> Unit = wrapped@{ bytes ->
            val ctx = connectionContext ?: return@wrapped
            handler.invoke(factory(packetHandler, bytes), packetHandler, ctx)
        }
        handlers.getOrPut(commandCode.code) { mutableMapOf() }[handler] = wrapped
    }

    /**
     * Removes a specific handler for a specific command (request or response [YmcCommandCode]).
     *
     * @param commandCode Command ID to remove a handler for.
     * @param handler Function to remove as a handler for [commandCode].
     */
    fun <T : CcuIncomingPacket> unregister(
        commandCode: YmcCommandCode,
        handler: (T) -> Unit,
    ) {
        handlers[commandCode.code]?.remove(handler)
    }

    /**
     * Starts the message handler.
     *
     * @param connectionContext Connection context for the active BLE session.
     */
    fun startMessageHandler(connectionContext: YmcConnectionContext) {
        this.connectionContext = connectionContext
        messageHandlerJob?.cancel()
        messageHandlerJob =
            scope.launch(errorHandler) {
                notifications.collect { event ->
                    if (event.uuid != YmcBikeProtocol.mainRxUuid || event.value.size < 2) return@collect
                    val bytes = event.value
                    val commandCode = ((bytes[0].toInt() and 0xFF) shl 8) or (bytes[1].toInt() and 0xFF)
                    handlers[commandCode]?.values?.forEach { handler ->
                        try {
                            handler(bytes)
                        } catch (e: MalformedPacketException) {
                            log.debug("malformed packet cmd=0x${commandCode.toString(16)}: ${e.message}")
                        } catch (e: Exception) {
                            log.debug("handler error cmd=0x${commandCode.toString(16)} ${e::class.java.simpleName}: ${e.message}")
                        }
                    }
                }
            }
    }

    /**
     * Stops the message handler.
     */
    fun stopMessageHandler() {
        messageHandlerJob?.cancel()
        messageHandlerJob = null
        connectionContext = null
    }

    private val errorHandler =
        CoroutineExceptionHandler { _, throwable ->
            val stack =
                buildString {
                    append(YmcMessageHandler::class.simpleName ?: "messageHandler")
                    append(": ")
                    append(throwable::class.java.name)
                    append(": ")
                    append(throwable.message ?: "")
                    append('\n')
                    append(throwable.stackTraceToString())
                }
            logger.error(stack, throwable)
        }
}
