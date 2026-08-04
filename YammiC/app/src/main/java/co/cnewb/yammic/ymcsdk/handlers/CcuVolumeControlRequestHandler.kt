package co.cnewb.yammic.ymcsdk.handlers

import android.content.Context
import android.media.AudioManager
import co.cnewb.yammic.logging.Logger
import co.cnewb.yammic.ymcsdk.YmcConnectionContext
import co.cnewb.yammic.ymcsdk.models.BikeVolumeType
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import co.cnewb.yammic.ymcsdk.packets.app.AppPhoneVolumeLevelRequest
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuVolumeControlRequest

class CcuVolumeControlRequestHandler(
    context: Context,
    logger: Logger,
) : CcuRequestHandler<CcuVolumeControlRequest>() {
    private val log = logger.section("VolumeControl")
    private val audioManager: AudioManager =
        checkNotNull(context.getSystemService(AudioManager::class.java)) {
            "AudioManager not available"
        }

    override suspend fun invoke(
        packet: CcuVolumeControlRequest,
        packetHandler: YmcPacketHandler,
        connectionContext: YmcConnectionContext,
    ) {
        log.debug("action=${packet.action}")
        val affectedType =
            when (packet.action) {
                CcuVolumeControlRequest.Action.MediaUp -> {
                    adjustVolume(AudioManager.STREAM_MUSIC, AudioManager.ADJUST_RAISE)
                    BikeVolumeType.Media
                }

                CcuVolumeControlRequest.Action.MediaDown -> {
                    adjustVolume(AudioManager.STREAM_MUSIC, AudioManager.ADJUST_LOWER)
                    BikeVolumeType.Media
                }

                CcuVolumeControlRequest.Action.PhoneUp -> {
                    adjustVolume(AudioManager.STREAM_VOICE_CALL, AudioManager.ADJUST_RAISE)
                    BikeVolumeType.Phone
                }

                CcuVolumeControlRequest.Action.PhoneDown -> {
                    adjustVolume(AudioManager.STREAM_VOICE_CALL, AudioManager.ADJUST_LOWER)
                    BikeVolumeType.Phone
                }

                CcuVolumeControlRequest.Action.MuteMicrophone -> {
                    null
                }

                CcuVolumeControlRequest.Action.MuteSpeaker -> {
                    null
                }

                CcuVolumeControlRequest.Action.Unknown -> {
                    null
                }
            } ?: return

        val stream = if (affectedType == BikeVolumeType.Media) AudioManager.STREAM_MUSIC else AudioManager.STREAM_VOICE_CALL
        val pct = currentVolumePercent(stream)
        log.debug("reporting ${affectedType.name} volume=$pct%")
        AppPhoneVolumeLevelRequest(packetHandler, affectedType, pct).send(connectionContext)
    }

    private fun adjustVolume(
        stream: Int,
        direction: Int,
    ) {
        audioManager.adjustStreamVolume(stream, direction, AudioManager.FLAG_SHOW_UI)
    }

    private fun currentVolumePercent(stream: Int): Int {
        val current = audioManager.getStreamVolume(stream)
        val max = audioManager.getStreamMaxVolume(stream)
        return if (max > 0) (current.toFloat() / max * 100).toInt() else 0
    }
}
