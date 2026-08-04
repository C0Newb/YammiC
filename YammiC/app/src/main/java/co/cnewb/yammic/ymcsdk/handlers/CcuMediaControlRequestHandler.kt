package co.cnewb.yammic.ymcsdk.handlers

import android.content.Context
import android.media.AudioManager
import android.media.session.MediaController
import android.view.KeyEvent
import co.cnewb.yammic.logging.Logger
import co.cnewb.yammic.ymcsdk.YmcConnectionContext
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuMusicControlRequest

class CcuMediaControlRequestHandler(
    context: Context,
    logger: Logger,
    private val activeController: () -> MediaController?,
) : CcuRequestHandler<CcuMusicControlRequest>() {
    private val log = logger.section("MediaControl")
    private val audioManager = context.getSystemService(AudioManager::class.java)

    override suspend fun invoke(
        packet: CcuMusicControlRequest,
        packetHandler: YmcPacketHandler,
        connectionContext: YmcConnectionContext,
    ) {
        log.debug("action=${packet.action}")
        val controls = activeController()?.transportControls
        if (controls != null) {
            log.debug("dispatching via TransportControls")
            when (packet.action) {
                CcuMusicControlRequest.Action.Play -> controls.play()
                CcuMusicControlRequest.Action.Pause -> controls.pause()
                CcuMusicControlRequest.Action.NextTrack -> controls.skipToNext()
                CcuMusicControlRequest.Action.PreviousTrack -> controls.skipToPrevious()
                CcuMusicControlRequest.Action.Unknown -> Unit
            }
        } else {
            log.debug("no active controller, falling back to key event")
            when (packet.action) {
                CcuMusicControlRequest.Action.Play -> dispatchMediaKey(KeyEvent.KEYCODE_MEDIA_PLAY)
                CcuMusicControlRequest.Action.Pause -> dispatchMediaKey(KeyEvent.KEYCODE_MEDIA_PAUSE)
                CcuMusicControlRequest.Action.NextTrack -> dispatchMediaKey(KeyEvent.KEYCODE_MEDIA_NEXT)
                CcuMusicControlRequest.Action.PreviousTrack -> dispatchMediaKey(KeyEvent.KEYCODE_MEDIA_PREVIOUS)
                CcuMusicControlRequest.Action.Unknown -> Unit
            }
        }
    }

    private fun dispatchMediaKey(keyCode: Int) {
        audioManager?.dispatchMediaKeyEvent(KeyEvent(KeyEvent.ACTION_DOWN, keyCode))
        audioManager?.dispatchMediaKeyEvent(KeyEvent(KeyEvent.ACTION_UP, keyCode))
    }
}
