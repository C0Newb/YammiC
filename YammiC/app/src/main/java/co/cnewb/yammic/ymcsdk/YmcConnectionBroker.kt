package co.cnewb.yammic.ymcsdk

import android.Manifest
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothManager
import android.content.Context
import android.os.Build
import androidx.annotation.RequiresPermission
import co.cnewb.yammic.logging.Logger
import co.cnewb.yammic.ymcsdk.handlers.CcuGetDeviceLanguageRequestHandler
import co.cnewb.yammic.ymcsdk.handlers.CcuGetDeviceTimeRequestHandler
import co.cnewb.yammic.ymcsdk.handlers.CcuPingRequestHandler
import co.cnewb.yammic.ymcsdk.handlers.CcuVolumeControllableRequestHandler
import co.cnewb.yammic.ymcsdk.models.YmcLanguage
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuGetDeviceLanguageRequest
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuGetDeviceTimeRequest
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuPingRequest
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuVolumeControllableRequest
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineExceptionHandler
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.time.Duration
import kotlin.time.Duration.Companion.milliseconds

class YmcConnectionBroker(
    private val context: Context,
    private val packetHandler: YmcPacketHandler,
    private val scope: CoroutineScope,
    logger: Logger,
    logFileLabel: String,
) {
    private val log = logger.section("YmcConnectionBroker")
    private val bluetoothManager = context.getSystemService(BluetoothManager::class.java)
    private val bluetoothAdapter: BluetoothAdapter? = bluetoothManager?.adapter

    private val sessionStore = YmcBikeSessionStore(context)
    private val _state = MutableStateFlow(sessionStore.loadState(logFileLabel))
    val state = _state.asStateFlow()

    private val btManager =
        YmcBluetoothConnectionManager(
            context = context,
            bluetoothAdapter = bluetoothAdapter,
            scope = scope,
            onLog = ::pushLog,
            onDisconnected = {
                messageBroker = null
                stopConnectedJobs()
                pushState("Disconnected")
            },
        )

    val messageHandler =
        YmcMessageHandler(
            logger = logger,
            packetHandler = packetHandler,
            scope = scope,
            notifications = btManager.notifications,
        )

    val languageHandler = CcuGetDeviceLanguageRequestHandler(YmcLanguage.English)

    private val volumeControllableHandler = CcuVolumeControllableRequestHandler(context)

    private val timeHandler = CcuGetDeviceTimeRequestHandler()
    private val pingHandler = CcuPingRequestHandler()
    private val virtualPitBoardController = YmcVirtualPitBoardController(logger)

    var messageBroker: YmcMessageBroker? = null
        private set

    val isBikeConnected: Boolean get() = messageBroker != null

    private data class PeriodicJob(
        val interval: Duration,
        val block: suspend (YmcMessageBroker) -> Unit,
    )

    private val periodicJobs = mutableListOf<PeriodicJob>()
    private val activeJobHandles = mutableListOf<Job>()
    private val onConnectJobs = mutableListOf<suspend (YmcMessageBroker) -> Unit>()

    fun addConnectedJob(
        interval: Duration,
        block: suspend (YmcMessageBroker) -> Unit,
    ) {
        periodicJobs.add(PeriodicJob(interval, block))
    }

    /** Registers [block] to run once, immediately after every successful connect. */
    fun addOnConnectJob(block: suspend (YmcMessageBroker) -> Unit) {
        onConnectJobs.add(block)
    }

    private suspend fun runOnConnectJobs(broker: YmcMessageBroker) {
        for (job in onConnectJobs) {
            try {
                job(broker)
            } catch (e: CancellationException) {
                throw e
            } catch (t: Throwable) {
                logThrowable("onConnectJob", t)
            }
        }
    }

    private fun startConnectedJobs(broker: YmcMessageBroker) {
        for (job in periodicJobs) {
            activeJobHandles.add(
                scope.launch(errorHandler) {
                    while (true) {
                        try {
                            job.block(broker)
                        } catch (e: CancellationException) {
                            throw e
                        } catch (t: Throwable) {
                            logThrowable("periodicJob", t)
                        }
                        delay(job.interval)
                    }
                },
            )
        }
    }

    private fun stopConnectedJobs() {
        activeJobHandles.forEach { it.cancel() }
        activeJobHandles.clear()
    }

    private var scanJob: Job? = null
    private var sppCredentials: kotlinx.coroutines.CompletableDeferred<Pair<String, String>>? = null
    private val active = AtomicBoolean(false)

    private val errorHandler =
        CoroutineExceptionHandler { _, throwable ->
            logThrowable("coroutine", throwable)
        }

    // ── Public API ────────────────────────────────────────────────────────────

    fun setPermissionsGranted(granted: Boolean) {
        _state.update { it.copy(permissionsGranted = granted) }
    }

    fun updateCredentials(
        ccuId: String,
        passKey: String,
    ) {
        sessionStore.saveCredentials(ccuId, passKey)
        _state.update { it.copy(ccuId = ccuId, passKey = passKey) }
    }

    fun updateDeviceName(deviceName: String) {
        sessionStore.saveDeviceName(deviceName)
        _state.update { it.copy(deviceName = deviceName) }
    }

    fun clearLogs() {
        _state.update { it.copy(logs = emptyList()) }
    }

    fun logThrowable(
        tag: String,
        throwable: Throwable,
    ) {
        val stack =
            buildString {
                append(tag)
                append(": ")
                append(throwable::class.java.name)
                append(": ")
                append(throwable.message ?: "")
                append('\n')
                append(throwable.stackTraceToString())
            }
        pushError(stack)
    }

    /**
     * Sends [image] (already-encoded graphic bytes, e.g. JPEG) to the Virtual Pit Board.
     * Returns false if not connected, the CCU rejected the request, or a chunk failed after retries.
     */
    suspend fun sendVirtualPitBoardMessage(image: ByteArray): Boolean {
        val broker = messageBroker ?: return false
        return virtualPitBoardController.sendMessage(broker.packetHandler, broker.connectionContext, image)
    }

    /** Clears the Virtual Pit Board display. Returns false if not connected or the CCU rejected it. */
    suspend fun clearVirtualPitBoard(): Boolean {
        val broker = messageBroker ?: return false
        return virtualPitBoardController.clear(broker.packetHandler, broker.connectionContext)
    }

    fun disconnect() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            PermissionsException.assertGranted(context, Manifest.permission.BLUETOOTH_CONNECT)
        }
        scanJob?.cancel()
        scanJob = null
        sppCredentials?.cancel()
        sppCredentials = null
        active.set(false)
        messageBroker = null
        stopConnectedJobs()
        messageHandler.stopMessageHandler()
        btManager.disconnect()
        pushState("Disconnected")
    }

    @RequiresPermission(Manifest.permission.BLUETOOTH_CONNECT)
    fun connectAndSend() {
        if (scanJob?.isActive == true) return

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            PermissionsException.assertGranted(context, Manifest.permission.BLUETOOTH_CONNECT)
        }

        scanJob =
            scope.launch(errorHandler) {
                pushLog("connectJob: started")
                coroutineContext[Job]?.invokeOnCompletion { cause ->
                    if (cause != null) {
                        pushLog("connectJob: completed with ${cause::class.java.name}: ${cause.message ?: ""}")
                    } else {
                        pushLog("connectJob: completed")
                    }
                    cause?.let { logThrowable("connectJobCompletion", it) }
                }
                runConnectAndSend()
            }
    }

    // ── Connection lifecycle ──────────────────────────────────────────────────

    @RequiresPermission(Manifest.permission.BLUETOOTH_CONNECT)
    private suspend fun runConnectAndSend() {
        if (bluetoothAdapter == null || !bluetoothAdapter.isEnabled) {
            pushError("Bluetooth is off")
            return
        }

        val credentials = state.value.credentials
        val targetName =
            state.value.deviceName
                .trim()
                .ifBlank { "YCCU_${credentials.ccuId}" }

        try {
            active.set(true)
            pushLog(
                "Start connect: target=$targetName " +
                    "bonded=${bluetoothAdapter.bondedDevices.size} " +
                    "permissions=${state.value.permissionsGranted}",
            )

            sppCredentials = btManager.openSppServer()

            pushState("Scanning")
            pushLog("Scanning for $targetName")
            val device =
                btManager.scanForDevice(targetName)
                    ?: throw IllegalStateException("Bike not found: $targetName")

            pushState("Connecting")
            pushLog("Connecting to ${device.name} / ${device.address} bondState=${device.bondState}")
            val gatt = btManager.connect(device)

            pushState("Discovering")
            pushLog("Negotiating MTU and discovering services")
            btManager.requestMtu(gatt, 183)
            btManager.discoverServices(gatt)

            val service =
                gatt.getService(YmcBikeProtocol.serviceUuid)
                    ?: throw IllegalStateException("Yamaha service not found")

            val connectionContext = YmcConnectionContext(gatt, service, context, btManager)
            val localBroker = YmcMessageBroker(packetHandler, connectionContext, context)

            pushState("Subscribing")
            btManager.subscribeToNotifications(gatt, service)

            messageHandler.registerHandler(YmcCommandCode.PingRequest, ::CcuPingRequest, pingHandler)
            messageHandler.registerHandler(YmcCommandCode.VehicleSettingRequest, ::CcuGetDeviceTimeRequest, timeHandler)
            messageHandler.registerHandler(YmcCommandCode.LanguageRequest, ::CcuGetDeviceLanguageRequest, languageHandler)
            messageHandler.registerHandler(
                YmcCommandCode.VolumeControllableRequest,
                ::CcuVolumeControllableRequest,
                volumeControllableHandler,
            )
            virtualPitBoardController.registerHandlers(messageHandler)
            messageHandler.startMessageHandler(connectionContext)

            pushState("Listening")
            pushLog("Waiting for CCU startup notifications")
            delay(2_000.milliseconds)

            pushState("Authenticating")
            pushLog("Authenticating")
            val sppCreds = withTimeoutOrNull(2_000.milliseconds) { sppCredentials?.await() }
            val authCcuId = sppCreds?.first ?: credentials.ccuId
            val authPassKey = sppCreds?.second ?: credentials.passKey
            pushLog("Auth: using ${if (sppCreds != null) "RFCOMM" else "stored"} credentials ccuId=$authCcuId")
            localBroker.sendAuthenticationRequest(authCcuId, authPassKey)
            delay(300.milliseconds)

            pushState("Identifying")
            pushLog("Sending IdentifyAppRequest")
            localBroker.sendIdentificationRequest()

            messageBroker = localBroker
            runOnConnectJobs(localBroker)
            startConnectedJobs(localBroker)
            pushState("Done")
            pushLog("Connected!")
        } catch (e: CancellationException) {
            messageBroker = null
            stopConnectedJobs()
            logThrowable("runConnectAndSend.cancelled", e)
        } catch (t: Throwable) {
            messageBroker = null
            stopConnectedJobs()
            logThrowable("runConnectAndSend", t)
        } finally {
            active.set(false)
        }
    }

    // ── Logging / state ───────────────────────────────────────────────────────

    private fun pushState(label: String) {
        _state.update {
            it.copy(isBusy = label !in setOf("Done", "Idle", "Disconnected"), connectionLabel = label, lastError = "")
        }
        pushLog(label)
    }

    private fun pushError(message: String) {
        _state.update { it.copy(isBusy = false, connectionLabel = "Failed", lastError = message) }
        pushLog("ERROR: $message")
    }

    private fun pushLog(message: String) {
        val line = message.trim()
        scope.launch {
            _state.update { current -> current.copy(logs = (current.logs + line).takeLast(200)) }
            log.debug(line)
        }
    }
}
