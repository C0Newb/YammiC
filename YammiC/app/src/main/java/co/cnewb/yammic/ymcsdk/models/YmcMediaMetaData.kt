package co.cnewb.yammic.ymcsdk.models

/**
 * Music metadata sent to the CCU once per second while connected.
 *
 * @param artistName Track artist. Empty string when unknown or unavailable.
 * @param albumName Album name. Empty string when unknown or unavailable.
 * @param songName Track title. Empty string when unknown; may be a human-readable status
 *   message (e.g. "No media playing", "Grant notification access in settings") when no
 *   real session is active.
 * @param durationSeconds Total track length in seconds. 0 when unknown or not applicable.
 * @param playbackState Current playback status (playing, paused, fast-forwarding, etc.).
 *   Use [YmcMediaPlaybackState.MediaDeviceUnavailable] when no media app is reachable at all.
 * @param playbackRate Speed multiplier relative to normal playback: 1.0 = normal speed,
 *   2.0 = double speed, 0.5 = half speed. Serialized over-the-wire as a fixed-point
 *   16-bit integer (value × 10), so the CCU receives e.g. 10 for normal speed.
 * @param elapsedSeconds Playback position in seconds from the start of the track.
 *   Accounts for time elapsed since the last position update when the track is playing.
 */
class YmcMediaMetaData(
    val artistName: String,
    val albumName: String,
    val songName: String,
    val durationSeconds: Int,
    val playbackState: YmcMediaPlaybackState,
    val playbackRate: Float,
    val elapsedSeconds: Int,
)
