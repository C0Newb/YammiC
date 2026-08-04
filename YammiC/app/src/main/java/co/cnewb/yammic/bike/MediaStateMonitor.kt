package co.cnewb.yammic.bike

import android.content.ComponentName
import android.content.Context
import android.media.MediaMetadata
import android.media.session.MediaController
import android.media.session.MediaSessionManager
import android.media.session.PlaybackState
import android.os.Handler
import android.os.Looper
import co.cnewb.yammic.logging.Logger
import co.cnewb.yammic.ymcsdk.YmcMessageBroker
import co.cnewb.yammic.ymcsdk.models.YmcMediaMetaData
import co.cnewb.yammic.ymcsdk.packets.app.AppMusicMetaDataResponse
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlin.time.Duration.Companion.milliseconds

class MediaStateMonitor(
    private val context: Context,
    private val scope: CoroutineScope,
    private val logger: Logger,
    private val getBroker: () -> YmcMessageBroker?,
    private val notificationListenerComponent: ComponentName? = null,
) {
    private val mediaSessionManager = context.getSystemService(MediaSessionManager::class.java)
    private val handler = Handler(Looper.getMainLooper())
    private val musicMetadataProvider = MusicMetadataProvider(context, notificationListenerComponent)

    private val _latestMetadata = MutableStateFlow<YmcMediaMetaData?>(null)
    val latestMetadata = _latestMetadata.asStateFlow()

    private var observedController: MediaController? = null
    private var observedCallback: MediaController.Callback? = null
    private var pendingPushJob: Job? = null
    private val activeSessionsListener =
        MediaSessionManager.OnActiveSessionsChangedListener { rebindObservedController() }

    private var isListening = false

    fun start() {
        if (isListening) return
        try {
            mediaSessionManager?.addOnActiveSessionsChangedListener(
                activeSessionsListener,
                notificationListenerComponent,
                handler,
            )
        } catch (_: SecurityException) {
        }
        rebindObservedController()
        isListening = true
    }

    fun stop() {
        if (!isListening) return
        try {
            mediaSessionManager?.removeOnActiveSessionsChangedListener(activeSessionsListener)
        } catch (_: SecurityException) {
        }
        observedController?.let { controller -> observedCallback?.let(controller::unregisterCallback) }
        observedController = null
        observedCallback = null
        pendingPushJob?.cancel()
        pendingPushJob = null
        isListening = false
    }

    private fun rebindObservedController() {
        val newController = musicMetadataProvider.getActiveController()
        if (newController?.sessionToken == observedController?.sessionToken) return

        observedController?.let { controller -> observedCallback?.let(controller::unregisterCallback) }
        observedController = newController

        if (newController == null) {
            observedCallback = null
            return
        }

        val callback =
            object : MediaController.Callback() {
                override fun onPlaybackStateChanged(state: PlaybackState?) = pushMetadata()

                override fun onMetadataChanged(metadata: MediaMetadata?) = pushMetadata()

                override fun onSessionDestroyed() = rebindObservedController()
            }
        observedCallback = callback
        newController.registerCallback(callback, handler)
        pushMetadata()
    }

    /** Sends the current media metadata immediately, e.g. right after connecting. */
    fun pushMetadata() {
        // Update the UI-facing snapshot immediately; the BLE send below is separately
        // debounced and re-reads metadata after the delay so its own behavior is unchanged.
        _latestMetadata.value = musicMetadataProvider.getMetadata()
        // MediaController callbacks can fire several times in quick succession for a single
        // logical state change (e.g. buffering -> paused -> playing). Cancel any not-yet-sent
        // push and debounce so only the settled state goes out, instead of flooding the shared
        // BLE write queue with stale, rapidly-flapping states.
        pendingPushJob?.cancel()
        pendingPushJob =
            scope.launch {
                try {
                    delay(200.milliseconds)
                    val broker = getBroker() ?: return@launch
                    broker.sendMusicMetaDataResponse(musicMetadataProvider.getMetadata())
                } catch (t: Throwable) {
                    // Silently ignore transient BLE errors; the next state change will retry.
                }
            }
    }
}
