package co.cnewb.yammic.ymcsdk.handlers

import android.content.Context
import android.media.AudioManager
import co.cnewb.yammic.ymcsdk.YmcConnectionContext
import co.cnewb.yammic.ymcsdk.models.BikeVolumeType
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import co.cnewb.yammic.ymcsdk.packets.app.AppPhoneVolumeLevelRequest
import co.cnewb.yammic.ymcsdk.packets.app.AppVolumeControllableResponse
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuVolumeControllableRequest

/**
 * Handles [CcuVolumeControllableRequest] by confirming volume control is supported,
 * then immediately reporting the current media volume so the CCU display initialises correctly.
 */
class CcuVolumeControllableRequestHandler(
    private val context: Context,
) : CcuRequestHandler<CcuVolumeControllableRequest>() {
    private val audioManager = context.getSystemService(AudioManager::class.java)

    override suspend fun invoke(
        packet: CcuVolumeControllableRequest,
        packetHandler: YmcPacketHandler,
        connectionContext: YmcConnectionContext,
    ) {
        AppVolumeControllableResponse(packetHandler, canControl = true).send(connectionContext)
        // Media volume
        AppPhoneVolumeLevelRequest(packetHandler, BikeVolumeType.Media, currentMediaVolume()).send(connectionContext)

        // Phone (call) volume
        AppPhoneVolumeLevelRequest(packetHandler, BikeVolumeType.Phone, currentPhoneVolume()).send(connectionContext)
    }

    private fun currentMediaVolume(): Int {
        val current = audioManager.getStreamVolume(AudioManager.STREAM_MUSIC)
        val max = audioManager.getStreamMaxVolume(AudioManager.STREAM_MUSIC)
        return if (max > 0) (current.toFloat() / max * 100).toInt() else 0
    }

    private fun currentPhoneVolume(): Int {
        val current = audioManager.getStreamVolume(AudioManager.STREAM_VOICE_CALL)
        val max = audioManager.getStreamMaxVolume(AudioManager.STREAM_VOICE_CALL)
        return if (max > 0) (current.toFloat() / max * 100).toInt() else 0
    }
}
