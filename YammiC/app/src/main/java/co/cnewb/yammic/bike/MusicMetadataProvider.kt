package co.cnewb.yammic.bike

import android.content.ComponentName
import android.content.Context
import android.media.MediaMetadata
import android.media.session.MediaController
import android.media.session.MediaSessionManager
import android.media.session.PlaybackState
import android.os.SystemClock
import co.cnewb.yammic.ymcsdk.models.YmcMediaMetaData
import co.cnewb.yammic.ymcsdk.models.YmcMediaPlaybackState
import co.cnewb.yammic.ymcsdk.providers.YmcMusicMetadataProvider

/**
 * Reads live music metadata from the phone's active media session.
 *
 * [notificationListenerComponent] should be the [ComponentName] of a registered
 * [android.service.notification.NotificationListenerService] for which the user has granted
 * notification access. Without it, [getMetadata] returns a placeholder track prompting the user
 * to grant access rather than [YmcMediaPlaybackState.MediaDeviceUnavailable].
 */
class MusicMetadataProvider(
    context: Context,
    private val notificationListenerComponent: ComponentName? = null,
) : YmcMusicMetadataProvider {
    private val mediaSessionManager = context.getSystemService(MediaSessionManager::class.java)

    /**
     * [MediaSessionManager.getActiveSessions] does not guarantee that the actually-playing
     * session is first in the list — a backgrounded app's stale paused session can rank ahead
     * of the one currently producing audio. Prefer a session reporting [PlaybackState.STATE_PLAYING]
     * so we don't dispatch commands to (and report metadata from) the wrong app.
     */
    fun getActiveController(): MediaController? =
        try {
            val sessions = mediaSessionManager?.getActiveSessions(notificationListenerComponent) ?: emptyList()
            sessions.firstOrNull { it.playbackState?.state == PlaybackState.STATE_PLAYING } ?: sessions.firstOrNull()
        } catch (_: SecurityException) {
            null
        }

    override fun getMetadata(): YmcMediaMetaData {
        val controller =
            try {
                getActiveController()
            } catch (_: SecurityException) {
                return YmcMediaMetaData(
                    artistName = "YammiC",
                    albumName = "N/A",
                    songName = "Grant notification access in settings",
                    durationSeconds = 0,
                    playbackState = YmcMediaPlaybackState.Paused,
                    playbackRate = 0f,
                    elapsedSeconds = 0,
                )
            }

        val metadata = controller?.metadata
        val state = controller?.playbackState

        if (metadata == null && state == null) {
            return YmcMediaMetaData(
                artistName = "",
                albumName = "",
                songName = "",
                durationSeconds = 0,
                playbackState = YmcMediaPlaybackState.MediaDeviceUnavailable,
                playbackRate = 0f,
                elapsedSeconds = 0,
            )
        }

        // Compute elapsed time accounting for playback since the last state update.
        val elapsedMs =
            if (state?.state == PlaybackState.STATE_PLAYING) {
                (state.position + (SystemClock.elapsedRealtime() - state.lastPositionUpdateTime))
                    .coerceAtLeast(0L)
            } else {
                state?.position?.coerceAtLeast(0L) ?: 0L
            }

        return YmcMediaMetaData(
            artistName = metadata?.getString(MediaMetadata.METADATA_KEY_ARTIST) ?: "",
            albumName = metadata?.getString(MediaMetadata.METADATA_KEY_ALBUM) ?: "",
            songName = metadata?.getString(MediaMetadata.METADATA_KEY_TITLE) ?: "",
            durationSeconds = ((metadata?.getLong(MediaMetadata.METADATA_KEY_DURATION) ?: 0L) / 1000L).toInt(),
            playbackState = mapPlaybackState(state?.state),
            playbackRate = state?.playbackSpeed ?: 1.0f,
            elapsedSeconds = (elapsedMs / 1000L).toInt(),
        )
    }

    private fun mapPlaybackState(state: Int?): YmcMediaPlaybackState =
        when (state) {
            PlaybackState.STATE_PLAYING -> YmcMediaPlaybackState.Playing
            PlaybackState.STATE_PAUSED, PlaybackState.STATE_STOPPED -> YmcMediaPlaybackState.Paused
            PlaybackState.STATE_FAST_FORWARDING -> YmcMediaPlaybackState.FastForwarding
            PlaybackState.STATE_REWINDING -> YmcMediaPlaybackState.Rewinding
            else -> YmcMediaPlaybackState.Paused
        }
}
