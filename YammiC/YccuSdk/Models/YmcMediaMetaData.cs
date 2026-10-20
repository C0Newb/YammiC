namespace CNCO.YccuSdk.Models;

/// <summary>
/// Music metadata sent to the CCU once per second while connected.
/// </summary>
/// <param name="ArtistName">Track artist. Empty string when unknown or unavailable.</param>
/// <param name="AlbumName">Album name. Empty string when unknown or unavailable.</param>
/// <param name="SongName">
/// Track title. Empty string when unknown; may be a human-readable status message
/// (e.g. "No media playing", "Grant notification access in settings") when no real session is active.
/// </param>
/// <param name="DurationSeconds">Total track length in seconds. 0 when unknown or not applicable.</param>
/// <param name="PlaybackState">
/// Current playback status (playing, paused, fast-forwarding, etc.).
/// Use <see cref="YmcMediaPlaybackState.MediaDeviceUnavailable"/> when no media app is reachable at all.
/// </param>
/// <param name="PlaybackRate">
/// Speed multiplier relative to normal playback: 1.0 = normal speed, 2.0 = double speed, 0.5 = half speed.
/// Serialized over-the-wire as a fixed-point 16-bit integer (value x 10), so the CCU receives e.g. 10 for normal speed.
/// </param>
/// <param name="ElapsedSeconds">
/// Playback position in seconds from the start of the track.
/// Accounts for time elapsed since the last position update when the track is playing.
/// </param>
public record YmcMediaMetaData(
    string ArtistName,
    string AlbumName,
    string SongName,
    int DurationSeconds,
    YmcMediaPlaybackState PlaybackState,
    float PlaybackRate,
    int ElapsedSeconds);
