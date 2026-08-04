package co.cnewb.yammic.ymcsdk.packets

import android.bluetooth.BluetoothGattCharacteristic
import co.cnewb.yammic.logging.Logger
import co.cnewb.yammic.ymcsdk.YmcBikeProtocol
import co.cnewb.yammic.ymcsdk.YmcConnectionContext
import co.cnewb.yammic.ymcsdk.packets.app.AppOutgoingPacket
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuIncomingRequest
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuIncomingResponse
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeoutOrNull
import kotlin.time.Duration.Companion.milliseconds

class YmcBasePacketHandler(
    logger: Logger,
) : YmcPacketHandler {
    private val log = logger.section("PacketHandler")
    private var requestIdCounter = 1
    private val ddtMutex = Mutex()

    override suspend fun send(
        connectionContext: YmcConnectionContext,
        request: AppOutgoingPacket,
    ) {
        when (request.transport) {
            AppOutgoingPacket.Transport.Simple -> sendSimpleCommand(connectionContext, request)
            AppOutgoingPacket.Transport.Ddt -> sendDdtCommand(connectionContext, request)
            AppOutgoingPacket.Transport.Spp -> sendSppCommand(connectionContext, request)
        }
    }

    /**
     * Sends a command over the Classic-Bluetooth SPP socket instead of BLE GATT.
     * Used only for [co.cnewb.yammic.ymcsdk.packets.YmcCommandCode.SendVirtualPitBoardGraphic] —
     * see [AppOutgoingPacket.Transport.Spp].
     */
    private suspend fun sendSppCommand(
        connectionContext: YmcConnectionContext,
        request: AppOutgoingPacket,
    ) {
        val packet = request.serialize(nextRequestId())
        val label = "${request::class.qualifiedName} (#${request.commandCode})"
        val framed = YmcBikeProtocol.sppFramePacket(packet)
        log.debug(
            "sendSppCommand[$label]: ${packet.size} bytes framed as ${framed.size} bytes: ${framed.toHexString()}",
        )
        if (!connectionContext.writeSpp(framed)) {
            log.debug("sendSppCommand[$label]: no live SPP socket, dropped")
        }
    }

    private fun nextRequestId(): Byte {
        val id = requestIdCounter
        requestIdCounter = if (requestIdCounter >= 255) 1 else requestIdCounter + 1
        return id.toByte()
    }

    /**
     * Sends a single-packet command directly to [YmcBikeProtocol.simpleWriteUuid].
     * Used for: IdentifyAppRequest, WeatherInfo, GetDeviceLanguageResponse, AliveCheckResponse.
     * No DDT handshake or fragmentation.
     */
    private suspend fun sendSimpleCommand(
        connectionContext: YmcConnectionContext,
        request: AppOutgoingPacket,
    ) {
        val packet = request.serialize(nextRequestId())
        val label = "${request::class.qualifiedName} (#${request.commandCode})"

        val char =
            connectionContext.service.getCharacteristic(YmcBikeProtocol.simpleWriteUuid)
                ?: connectionContext.service.getCharacteristic(YmcBikeProtocol.dataUuid) // fallback
                ?: throw IllegalStateException("No write characteristic available for $label")
        log.debug(
            "sendSimpleCommand[$label]: ${packet.size} bytes to ${char.uuid}, body:\n${packet.toHexString()}",
        )
        writeCharacteristic(connectionContext, char, packet)
    }

    /**
     * Sends a command via the DDT session protocol:
     * 1. Write size-announcement to [YmcBikeProtocol.controlUuid].
     * 2. Await active-status notification from [YmcBikeProtocol.statusUuid].
     * 3. Write data chunks to [YmcBikeProtocol.dataUuid].
     * 4. Await completion-status notification.
     *
     * Only one DDT session can be in flight at a time (there's a single shared session slot
     * on the CCU). The reference SDK (`d.java`) rejects a new DDT send outright with
     * `McError.DdtSending` when one is already active — it does not queue. We match that:
     * if the channel is busy, drop this send instead of blocking and piling up a backlog of
     * increasingly stale sends, which previously starved other DDT traffic and could time out
     * the connection.
     */
    private suspend fun sendDdtCommand(
        connectionContext: YmcConnectionContext,
        request: AppOutgoingPacket,
    ) = ddtMutex.withLock {
        val control =
            connectionContext.service.getCharacteristic(YmcBikeProtocol.controlUuid)
                ?: throw IllegalStateException("Control characteristic not found")
        val data =
            connectionContext.service.getCharacteristic(YmcBikeProtocol.dataUuid)
                ?: throw IllegalStateException("Data characteristic not found")

        val requestId = nextRequestId()
        val body = request.serialize(requestId)

        log.debug(
            "sendDdtRequest: cmd=${(body[0].toInt() and 0xFF shl 8) or (body[1].toInt() and 0xFF)}, reqId=${requestId.toUByte()}, framedSize=${body.size}, mtu=${connectionContext.negotiatedMtu}, body:\n${body.toHexString()}",
        )

        // CheckInactive: verify the DDT channel is idle before starting a session.
        // The SDK (j.java) reads status before writing control — if the channel is busy
        // from a previous incomplete session, the CCU will silently ignore the control write.
        val statusChar = connectionContext.service.getCharacteristic(YmcBikeProtocol.statusUuid)
        if (statusChar != null) {
            val statusValue = connectionContext.read(statusChar)
            if (statusValue != null && statusValue.isNotEmpty() && statusValue[0] != 0.toByte()) {
                log.debug(
                    "sendDdtRequest: channel busy status=${statusValue.joinToString(" ") { "%02X".format(it) }}, aborting",
                )
                return
            }
        }

        // Subscribe to BOTH status notifications BEFORE doing any writes.
        // The CCU's active and completion signals can arrive almost simultaneously with write
        // acknowledgements; subscribing first prevents the race where a notification is emitted
        // to the shared flow and overwrites the replay buffer before our collectors start.
        coroutineScope {
            val activeJob =
                async {
                    connectionContext.notifications.first { event ->
                        event.uuid == YmcBikeProtocol.statusUuid &&
                            event.value.size >= 2 &&
                            event.value[0] == 1.toByte() &&
                            event.value[1] == requestId
                    }
                }
            val completionJob =
                async {
                    connectionContext.notifications.first { event ->
                        event.uuid == YmcBikeProtocol.statusUuid &&
                            event.value.size >= 2 &&
                            event.value[0] == 0.toByte() &&
                            event.value[1] == 0.toByte()
                    }
                }

            val sessionStartPacket = YmcBikeProtocol.sessionStartPacket(requestId, body.size)
            log.debug("sendDdtRequest: sessionStart:\n${sessionStartPacket.toHexString()}")
            writeCharacteristic(
                connectionContext,
                control,
                sessionStartPacket,
            )
            val activeSeen = withTimeoutOrNull(5_000.milliseconds) { activeJob.await() }
            if (activeSeen == null) {
                log.debug("sendDdtRequest: no active-status notification for reqId=${requestId.toUByte()}")
            }

            val chunkSize = YmcBikeProtocol.payloadChunkSize(connectionContext.negotiatedMtu)
            var offset = 0
            while (offset < body.size) {
                val end = (offset + chunkSize).coerceAtMost(body.size)
                val chunk = body.copyOfRange(offset, end)
                val chunkPacket = YmcBikeProtocol.chunkPacket(requestId, offset, chunk)
                log.debug("sendDdtRequest: chunk offset=$offset..$end:\n${chunkPacket.toHexString()}")
                writeCharacteristic(connectionContext, data, chunkPacket)
                offset = end
            }

            val completed = withTimeoutOrNull(5_000.milliseconds) { completionJob.await() }
            if (completed == null) {
                log.debug("sendDdtRequest: no completion-status notification for reqId=${requestId.toUByte()}")
            }
        }
    }

    private suspend fun writeCharacteristic(
        connectionContext: YmcConnectionContext,
        char: BluetoothGattCharacteristic,
        value: ByteArray,
    ) {
        connectionContext.write(char, value)
    }
}

private fun ByteArray.toHexString(): String = joinToString(" ") { "%02X".format(it) }
