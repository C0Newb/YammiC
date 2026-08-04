package co.cnewb.yammic.bike

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.ComponentName
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.drawable.BitmapDrawable
import android.os.Binder
import android.os.IBinder
import androidx.core.content.ContextCompat
import androidx.core.graphics.createBitmap
import co.cnewb.yammic.MainActivity
import co.cnewb.yammic.R
import co.cnewb.yammic.logging.FileSink
import co.cnewb.yammic.logging.LogcatSink
import co.cnewb.yammic.logging.Logger
import co.cnewb.yammic.ymcsdk.YmcConnectionBroker
import co.cnewb.yammic.ymcsdk.handlers.CcuBluetoothHeadsetStateRequestHandler
import co.cnewb.yammic.ymcsdk.handlers.CcuIncomingCallControlRequestHandler
import co.cnewb.yammic.ymcsdk.handlers.CcuMediaControlRequestHandler
import co.cnewb.yammic.ymcsdk.handlers.CcuPhoneCellSignalLevelRequestHandler
import co.cnewb.yammic.ymcsdk.handlers.CcuVolumeControlRequestHandler
import co.cnewb.yammic.ymcsdk.handlers.CcuWeatherRequestHandler
import co.cnewb.yammic.ymcsdk.navigation.NavigationSdk
import co.cnewb.yammic.ymcsdk.navigation.NavigationState
import co.cnewb.yammic.ymcsdk.packets.YmcBasePacketHandler
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuBluetoothHeadsetStateRequest
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuIncomingCallControlRequest
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuMusicControlRequest
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuPhoneCellSignalLevelRequest
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuVolumeControlRequest
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuWeatherRequest
import co.cnewb.yammic.ymcsdk.models.YmcMediaMetaData
import co.cnewb.yammic.ymcsdk.vpb.VpbImageBuilder
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlin.time.Duration.Companion.seconds

/**
 * Foreground service that keeps the BLE connection alive when the screen is off
 * or the app is backgrounded. The [YmcConnectionBroker] lives here, not in the Activity.
 *
 * Lifecycle:
 *  - Started via [startService] when the user taps "Connect".
 *  - Activity binds via [LocalBinder] to access [controller].
 *  - Service stays running until [stopSelf] is called (e.g. disconnect action in notification).
 */
class BikeForegroundService : Service() {
    private val scope = CoroutineScope(Dispatchers.Main + SupervisorJob())

    private val fileSink by lazy { FileSink(applicationContext) }
    private val logger by lazy { Logger("YammiC", listOf(LogcatSink(), fileSink)) }

    private val packetHandler by lazy { YmcBasePacketHandler(logger) }

    private val weatherProvider = DemoWeatherProvider()
    private val phoneCallMonitor by lazy {
        PhoneCallMonitor(applicationContext, scope) { controller.messageBroker }
    }
    private val mediaStateMonitor by lazy {
        MediaStateMonitor(
            applicationContext,
            scope,
            logger,
            { controller.messageBroker },
            ComponentName(applicationContext, YammicNotificationListenerService::class.java),
        )
    }
    val musicMetadataProvider by lazy {
        MusicMetadataProvider(
            applicationContext,
            ComponentName(applicationContext, YammicNotificationListenerService::class.java),
        )
    }

    /** Lazily created so the application context is available when first accessed. */
    val controller: YmcConnectionBroker by lazy {
        YmcConnectionBroker(
            applicationContext,
            packetHandler,
            scope,
            logger,
            fileSink.fileLabel(),
        )
    }

    val navigation: NavigationSdk by lazy {
        NavigationSdk(applicationContext, controller, 15, scope, logger)
    }

    // ── Feature toggles ──────────────────────────────────────────────────────
    // VPB (Virtual Pit Board) and navigation share the CCU's display and may interfere with each
    // other. These let the user disable one independently to isolate/avoid conflicts.

    private val _vpbEnabled = MutableStateFlow(true)
    val vpbEnabled = _vpbEnabled.asStateFlow()

    private val _navigationEnabled = MutableStateFlow(true)
    val navigationEnabled = _navigationEnabled.asStateFlow()

    private val _vpbStatus = MutableStateFlow("Idle")
    val vpbStatus = _vpbStatus.asStateFlow()

    val mediaMetadata: StateFlow<YmcMediaMetaData?> get() = mediaStateMonitor.latestMetadata

    /** Toggles VPB pushes. When disabled, all VPB sends (onConnect, ticker, nav-button echo) are
     *  dropped and any currently-displayed VPB message is cleared. Takes effect immediately if
     *  the bike is connected. */
    fun setVpbEnabled(enabled: Boolean) {
        _vpbEnabled.value = enabled
        if (!enabled) {
            _vpbStatus.value = "Disabled"
            if (controller.isBikeConnected) {
                scope.launch {
                    try {
                        controller.clearVirtualPitBoard()
                    } catch (t: Throwable) {
                        logger.debug("setVpbEnabled: clearVirtualPitBoard failed: ${t.message}")
                    }
                }
            }
        } else {
            _vpbStatus.value = "Idle"
        }
    }

    /** Toggles the navigation-screen link. Activates/deactivates it immediately if the bike is
     *  connected; otherwise just remembered for the next connection. */
    fun setNavigationEnabled(enabled: Boolean) {
        _navigationEnabled.value = enabled
        if (!controller.isBikeConnected) return
        if (enabled) {
            navigation.activate()
        } else {
            navigation.deactivate()
        }
    }

    // ── Binder ────────────────────────────────────────────────────────────────

    inner class LocalBinder : Binder() {
        val service: BikeForegroundService get() = this@BikeForegroundService
    }

    private val binder = LocalBinder()

    override fun onBind(intent: Intent): IBinder = binder

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
        startForeground(NOTIFICATION_ID, buildNotification(connected = false))
        controller.messageHandler.registerHandler(
            YmcCommandCode.MusicControlRequest,
            ::CcuMusicControlRequest,
            CcuMediaControlRequestHandler(
                applicationContext,
                logger,
            ) { musicMetadataProvider.getActiveController() },
        )
        controller.messageHandler.registerHandler(
            YmcCommandCode.PhoneVolumeControlRequest,
            ::CcuVolumeControlRequest,
            CcuVolumeControlRequestHandler(applicationContext, logger),
        )
        controller.messageHandler.registerHandler(
            YmcCommandCode.BluetoothHeadsetStateRequest,
            ::CcuBluetoothHeadsetStateRequest,
            CcuBluetoothHeadsetStateRequestHandler(true),
        )
        controller.messageHandler.registerHandler(
            YmcCommandCode.WeatherRequest,
            ::CcuWeatherRequest,
            CcuWeatherRequestHandler(weatherProvider),
        )
        controller.messageHandler.registerHandler(
            YmcCommandCode.IncomingCallControl,
            ::CcuIncomingCallControlRequest,
            CcuIncomingCallControlRequestHandler(applicationContext),
        )
        controller.messageHandler.registerHandler(
            YmcCommandCode.PhoneCellSignalLevelRequest,
            ::CcuPhoneCellSignalLevelRequest,
            CcuPhoneCellSignalLevelRequestHandler(applicationContext),
        )

        // Each step below is isolated in its own try/catch: a DDT timeout on an earlier
        // step (e.g. the test notification) must not prevent later steps (the real call
        // state and media metadata push) from running. addOnConnectJob only isolates
        // failures between separately-registered jobs, not between lines within one job.
        controller.addOnConnectJob { broker ->
            runOnConnectStep("phone state") {
                broker.sendPhoneState()
                broker.sendPhoneThermalState()
                broker.sendNotificationCategory(incomingCall = true, missedCall = true, social = true, other = true)
            }

            runOnConnectStep("test notification") {
                broker.sendNotification(id = 1, title = "YammiC", detail = "It works!")
            }

            runOnConnectStep("first time call state") { phoneCallMonitor.pushCurrentCallState() }
            runOnConnectStep("first time media state") { mediaStateMonitor.pushMetadata() }

            runOnConnectStep("send Connected message via VPB") {
                if (_vpbEnabled.value) {
                    val vpbConnected = controller.sendVirtualPitBoardMessage(buildConnectedVirtualPitBoardImage())
                    logger.debug("Virtual Pit Board Connected message result: $vpbConnected")
                    if (vpbConnected) _vpbStatus.value = "Connected message"
                } else {
                    logger.debug("VPB disabled, skipping Connected message")
                }
            }

            runOnConnectStep("activate navigation screen") {
                if (_navigationEnabled.value) {
                    navigation.activate()
                } else {
                    logger.debug("Navigation disabled, skipping activate")
                }
            }

            runOnConnectStep("send sandwich image via VPB (delayed 15s)") {
                delay(15.seconds)
                if (_vpbEnabled.value) {
                    val sandwichBitmap = ContextCompat.getDrawable(applicationContext, R.drawable.sandwich) as? BitmapDrawable
                    if (sandwichBitmap != null) {
                        val vpbImage = VpbImageBuilder.fromBitmap(sandwichBitmap.bitmap)
                        val result = controller.sendVirtualPitBoardMessage(vpbImage)
                        logger.debug("Sandwich image send result: $result")
                        if (result) _vpbStatus.value = "Sandwich image"
                    } else {
                        logger.debug("Failed to load sandwich drawable")
                    }
                } else {
                    logger.debug("VPB disabled, skipping sandwich image")
                }
            }
        }

        phoneCallMonitor.start()
        mediaStateMonitor.start()

        // Keep the notification in sync with the controller's connection state.
        scope.launch {
            controller.state.collect {
                updateNotification(controller.isBikeConnected)
            }
        }

        scope.launch {
            navigation.state.collect { navState ->
                if (navState is NavigationState.Active) {
                    try {
                        logger.debug("Navigation active! Pushing rotating sandwich animation")
                        navigation.pushAnimatedImage(R.drawable.rotating_sandwich, quality = 90)
                        navigation.setRoadName("This is a test")
                    } catch (ex: Exception) {
                        logger.error("Failed to push image and/or set road name! ${ex.message}", ex)
                    }
                }
            }
        }

        navigation.onGoHomeRequested = {
            logger.debug("Navigation: CCU requested 'Go home'")
            sendNavRequestNotification("Go home clicked")
        }
        navigation.onGoToWorkRequested = {
            logger.debug("Navigation: CCU requested 'Go to work'")
            sendNavRequestNotification("Go to work clicked")
        }
    }

    /** Sends a fake Virtual Pit Board notification announcing which nav-screen button was tapped. */
    private fun sendNavRequestNotification(message: String) {
        if (!_vpbEnabled.value) return
        scope.launch {
            try {
                val image = buildVirtualPitBoardImage(message, Color.GREEN)
                val sent = controller.sendVirtualPitBoardMessage(image)
                if (sent) _vpbStatus.value = message
            } catch (t: Throwable) {
                logger.debug("sendNavRequestNotification failed: ${t.message}")
            }
        }
    }

    /**
     * Runs one onConnect step in isolation: a DDT timeout or other failure here is logged and
     * swallowed instead of aborting the remaining steps in the same onConnect job.
     */
    private suspend fun runOnConnectStep(
        label: String,
        block: suspend () -> Unit,
    ) {
        logger.debug("onConnect step: $label")
        try {
            block()
        } catch (t: Throwable) {
            logger.debug("onConnect step failed: $label: ${t.message}")
        }
    }

    /**
     * Builds a throwaway JPEG matching the reference SDK's "vehicle image" composite: a full
     * 600x306 black canvas (`VpbMessageFunctionStore.VEHICLE_IMAGE_WIDTH/HEIGHT`) with a 526x66
     * message strip (`BOARD_IMAGE_WIDTH/HEIGHT`) pasted at left=0, top=163. The CCU meter appears
     * to validate against these exact dimensions — a bare 526x66 image gets rejected with a retry.
     */
    private fun buildVirtualPitBoardImage(
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

        val out = java.io.ByteArrayOutputStream()
        vehicleImage.compress(Bitmap.CompressFormat.JPEG, 100, out)
        return out.toByteArray()
    }

    private fun buildTestVirtualPitBoardImage(text: String): ByteArray = buildVirtualPitBoardImage(text, Color.BLUE)

    private fun buildConnectedVirtualPitBoardImage(): ByteArray = buildVirtualPitBoardImage("Connected!", Color.GREEN)

    override fun onStartCommand(
        intent: Intent?,
        flags: Int,
        startId: Int,
    ): Int {
        when (intent?.action) {
            ACTION_DISCONNECT -> {
                controller.disconnect()
                stopSelf()
            }
        }
        // START_STICKY: if the process is killed, restart the service (but not the connection).
        return START_STICKY
    }

    override fun onDestroy() {
        phoneCallMonitor.stop()
        mediaStateMonitor.stop()
        navigation.deactivate()
        controller.disconnect()
        scope.cancel()
        super.onDestroy()
    }

    // ── Notification ──────────────────────────────────────────────────────────

    private fun createNotificationChannel() {
        val channel =
            NotificationChannel(
                CHANNEL_ID,
                "YammiC Connection",
                NotificationManager.IMPORTANCE_LOW, // low = no sound, appears in shade
            ).apply {
                description = "Keeps the CCU BLE connection alive in the background"
                setShowBadge(false)
            }
        getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
    }

    /** Call after the user grants READ_PHONE_STATE etc. at runtime so monitoring can start. */
    fun restartPhoneCallMonitor() {
        phoneCallMonitor.start()
    }

    fun updateNotification(connected: Boolean) {
        getSystemService(NotificationManager::class.java)
            .notify(NOTIFICATION_ID, buildNotification(connected))
    }

    private fun buildNotification(connected: Boolean): Notification {
        val openApp =
            PendingIntent.getActivity(
                this,
                0,
                Intent(this, MainActivity::class.java).apply {
                    flags = Intent.FLAG_ACTIVITY_SINGLE_TOP
                },
                PendingIntent.FLAG_IMMUTABLE,
            )
        val disconnect =
            PendingIntent.getService(
                this,
                0,
                Intent(this, BikeForegroundService::class.java).apply { action = ACTION_DISCONNECT },
                PendingIntent.FLAG_IMMUTABLE,
            )
        val text = if (connected) "Connected to CCU" else "Ready"
        return Notification
            .Builder(this, CHANNEL_ID)
            .setContentTitle("YammiC")
            .setContentText(text)
            .setSmallIcon(R.drawable.ic_launcher_foreground)
            .setContentIntent(openApp)
            .setOngoing(true)
            .addAction(
                Notification.Action
                    .Builder(
                        null,
                        "Disconnect",
                        disconnect,
                    ).build(),
            ).build()
    }

    companion object {
        const val ACTION_DISCONNECT = "co.cnewb.yammic.DISCONNECT"
        private const val CHANNEL_ID = "yammic_ble"
        private const val NOTIFICATION_ID = 1
    }
}
