using SkiaSharp;

namespace CNCO.YccuSdk.Reels;

/// <summary>
/// A video in the reels folder. <see cref="Uri"/> is whatever the platform needs to open it.
/// </summary>
public sealed record ReelItem(string Uri, string Name);

/// <summary>
/// The platform half of Reels Mode: finding the videos in the chosen folder and playing one with its sound through the
/// phone while handing back its pictures. Video decoding is platform specific (Skia cannot decode video), so each platform
/// supplies this; <see cref="ReelsController"/> does the rest.
/// </summary>
public interface IReelsPlatform
{
    /// <summary>
    /// Called when the user picks a folder, so the platform can keep access to it (e.g. a persistable permission).
    /// </summary>
    void RememberFolder(string folderUri);

    /// <summary>
    /// The videos directly inside the folder, sorted by name. Empty if the folder can't be read.
    /// </summary>
    Task<IReadOnlyList<ReelItem>> ListVideosAsync(string folderUri);

    /// <summary>
    /// Plays <paramref name="item"/> on a loop with sound on the phone, replacing whatever was playing, and calls
    /// <paramref name="onFrame"/> with upright pictures at no more than about <paramref name="maxFps"/> a second. The callback owns
    /// (and must dispose) each bitmap. <paramref name="onError"/> gets a message if the video can't be played.
    /// </summary>
    void Play(ReelItem item, int maxFps, Action<SKBitmap> onFrame, Action<string> onError);

    /// <summary>
    /// Stops playback, silences the sound and releases the player. Safe to call when nothing is playing.
    /// </summary>
    void Stop();
}
