using System;
using CNCO.Unify.Device;
using CNCO.Unify.Device.Media;
using CNCO.Unify.Device.Power;
using CNCO.Unify.Device.Telephony;
using CNCO.YccuSdk.Bike;
using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Packets.Ccu;
using CNCO.YccuSdk.Platform;
using UnifyCallState = CNCO.Unify.Device.Telephony.CallState;

namespace YammiC.Android;

/// <summary>
/// Feeds the SDK's <see cref="BikeService"/> from the Unify.Device call state monitor.
/// </summary>
internal sealed class UnifyCallStateSource : IYmcCallStateSource
{
    private readonly ICallStateMonitor _monitor = DeviceRuntime.CallStateMonitor;

    public UnifyCallStateSource()
    {
        _monitor.CallStateChanged += (state, number) => CallStateChanged?.Invoke(Map(state), number);
    }

    public event Action<YmcCallState, string?>? CallStateChanged;

    public string? LineNumber => _monitor.LineNumber;

    public void Start() => _monitor.Start();

    public void Stop() => _monitor.Stop();

    public string? LookupContactName(string phoneNumber) => _monitor.LookupContactName(phoneNumber);

    private static YmcCallState Map(UnifyCallState state) => state switch
    {
        UnifyCallState.Ringing => YmcCallState.Ringing,
        UnifyCallState.OffHook => YmcCallState.OffHook,
        _ => YmcCallState.Idle,
    };
}

/// <summary>
/// Feeds the SDK's <see cref="BikeService"/> from the Unify.Device media session monitor, and handles the
/// handlebar media buttons through the same monitor.
/// </summary>
internal sealed class UnifyMediaSource : IYmcMediaSource, IYmcMediaController
{
    private readonly IMediaSessionMonitor _monitor = DeviceRuntime.MediaSessionMonitor;

    public UnifyMediaSource()
    {
        _monitor.Changed += () => Changed?.Invoke();
    }

    public event Action? Changed;

    public void Start() => _monitor.Start();

    public void Stop() => _monitor.Stop();

    public byte[]? GetArtwork() => _monitor.GetArtwork();

    public YmcMediaMetaData GetMetadata()
    {
        var session = _monitor.GetCurrent();
        switch (session.Availability)
        {
            case MediaSessionAvailability.AccessRequired:
                return new YmcMediaMetaData(
                    ArtistName: "YammiC",
                    AlbumName: "N/A",
                    SongName: "Grant notification access in settings",
                    DurationSeconds: 0,
                    PlaybackState: YmcMediaPlaybackState.Paused,
                    PlaybackRate: 0f,
                    ElapsedSeconds: 0);

            case MediaSessionAvailability.NoSession:
                return new YmcMediaMetaData(
                    ArtistName: "",
                    AlbumName: "",
                    SongName: "",
                    DurationSeconds: 0,
                    PlaybackState: YmcMediaPlaybackState.MediaDeviceUnavailable,
                    PlaybackRate: 0f,
                    ElapsedSeconds: 0);

            default:
                return new YmcMediaMetaData(
                    ArtistName: session.Artist,
                    AlbumName: session.Album,
                    SongName: session.Title,
                    DurationSeconds: (int)session.Duration.TotalSeconds,
                    PlaybackState: session.PlaybackState switch
                    {
                        MediaPlaybackState.Playing => YmcMediaPlaybackState.Playing,
                        MediaPlaybackState.FastForwarding => YmcMediaPlaybackState.FastForwarding,
                        MediaPlaybackState.Rewinding => YmcMediaPlaybackState.Rewinding,
                        _ => YmcMediaPlaybackState.Paused,
                    },
                    PlaybackRate: session.PlaybackRate,
                    ElapsedSeconds: (int)session.Position.TotalSeconds);
        }
    }

    public bool TryTransportControl(CcuMusicControlRequest.Action action) =>
        Map(action) is { } mapped && _monitor.TrySendTransportAction(mapped);

    public void DispatchMediaKey(CcuMusicControlRequest.Action action)
    {
        if (Map(action) is { } mapped) _monitor.SendMediaKey(mapped);
    }

    private static MediaTransportAction? Map(CcuMusicControlRequest.Action action) => action switch
    {
        CcuMusicControlRequest.Action.Play => MediaTransportAction.Play,
        CcuMusicControlRequest.Action.Pause => MediaTransportAction.Pause,
        CcuMusicControlRequest.Action.NextTrack => MediaTransportAction.NextTrack,
        CcuMusicControlRequest.Action.PreviousTrack => MediaTransportAction.PreviousTrack,
        _ => null,
    };
}

/// <summary>
/// Feeds the SDK's <see cref="BikeService"/> from the Unify.Device power monitor (Battery Saver).
/// </summary>
internal sealed class UnifyPowerSource : IYmcPowerSource
{
    private readonly IPowerMonitor _monitor = DeviceRuntime.PowerMonitor;

    public UnifyPowerSource()
    {
        _monitor.PowerSaveModeChanged += on => PowerSaveModeChanged?.Invoke(on);
    }

    public bool IsPowerSaveMode => _monitor.IsPowerSaveMode;

    public event Action<bool>? PowerSaveModeChanged;

    public void Start() => _monitor.Start();

    public void Stop() => _monitor.Stop();
}
