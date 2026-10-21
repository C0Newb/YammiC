using CNCO.YccuSdk.Models;

namespace CNCO.YccuSdk.Bike;

public enum YmcCallState
{
    Idle,
    Ringing,
    OffHook,
}

/// <summary>
/// Raw phone-call events. The platform side (e.g. the call monitor in CNCO.Unify.Device) implements this;
/// <see cref="YmcCallStateRelay"/> turns the events into CCU packets.
/// </summary>
public interface IYmcCallStateSource
{
    /// <summary>
    /// Raised when the call state changes. For <see cref="YmcCallState.Ringing"/> the incoming number is passed when the
    /// platform knows it (it may arrive on a separate event with a null/empty number - the relay remembers the last non-empty one).
    /// </summary>
    event Action<YmcCallState, string?>? CallStateChanged;

    /// <summary>
    /// Starts listening. Does nothing (and may be called again later) if the required permission isn't granted yet.
    /// </summary>
    void Start();

    void Stop();

    /// <summary>
    /// Display name for <paramref name="phoneNumber"/> from the contacts, or null.
    /// </summary>
    string? LookupContactName(string phoneNumber);

    /// <summary>
    /// The phone's own number; only used as a fallback when the incoming number is unknown.
    /// </summary>
    string? LineNumber { get; }
}

/// <summary>
/// Live media-session information. Implemented by the platform side (media monitor in CNCO.Unify.Device);
/// <see cref="YmcMediaStateRelay"/> turns changes into CCU packets.
/// </summary>
public interface IYmcMediaSource
{
    /// <summary>
    /// Current metadata, with a placeholder when no session is reachable (see <see cref="YmcMediaPlaybackState.MediaDeviceUnavailable"/>).
    /// </summary>
    YmcMediaMetaData GetMetadata();

    /// <summary>
    /// Raised when the active session, its playback state or its metadata changes.
    /// </summary>
    event Action? Changed;

    /// <summary>
    /// The current track's picture as encoded image bytes (a small JPEG/PNG), or null if there is none or the platform can't provide one.
    /// </summary>
    byte[]? GetArtwork() => null;

    void Start();

    void Stop();
}

/// <summary>
/// The phone's power saving mode (Battery Saver). Implemented by the platform side (power monitor in CNCO.Unify.Device); the
/// navigation screen drops to its Low refresh profile while it is on (see <see cref="Navigation.NavRefreshMode.Auto"/>).
/// </summary>
public interface IYmcPowerSource
{
    /// <summary>
    /// Whether the phone is in power saving mode right now.
    /// </summary>
    bool IsPowerSaveMode { get; }

    /// <summary>
    /// Raised, with the new value, when it changes.
    /// </summary>
    event Action<bool>? PowerSaveModeChanged;

    void Start();

    void Stop();
}
