using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CNCO.YccuSdk;
using CNCO.YccuSdk.Can;
using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Navigation;
using CNCO.YccuSdk.Screens;
using CNCO.YccuSdk.Yrc;

namespace YammiC.Services;

/// <summary>
/// Everything the pages show, captured at one instant. Immutable; the controller replaces it wholesale.
/// </summary>
public record BikeSnapshot
{
    public BikeDebugState Connection { get; init; } = new();
    public bool VpbEnabled { get; init; } = true;
    public bool NavigationEnabled { get; init; } = true;
    public string VpbStatus { get; init; } = "Idle";
    public YmcMediaMetaData? Media { get; init; }
    public NavigationState Navigation { get; init; } = new NavigationState.Idle();
}

/// <summary>
/// The only thing the pages know about the bike. Implemented over the SDK on real platforms and by
/// <see cref="FakeBikeController"/> where there is no Bluetooth.
/// </summary>
public interface IBikeController
{
    BikeSnapshot Snapshot { get; }

    /// <summary>
    /// Raised whenever <see cref="Snapshot"/> changes. May fire on any thread.
    /// </summary>
    event Action? Changed;

    /// <summary>
    /// True when connecting can work on this platform.
    /// </summary>
    bool CanConnect { get; }

    /// <summary>
    /// True when the platform still needs the user to grant notification access (Android music metadata).
    /// </summary>
    bool NeedsNotificationAccess { get; }

    bool MusicMetadataEnabled { get; set; }
    bool PhoneMetadataEnabled { get; set; }
    bool AutoReconnectEnabled { get; set; }

    /// <summary>
    /// Report a saved home and work address so the bike's "Go home" / "Go to work" buttons work.
    /// </summary>
    bool HomeAndWorkEnabled { get; set; }

    /// <summary>
    /// What the bike's navigation screen shows: the sandwich, the custom screen or the reels.
    /// </summary>
    NavMode NavigationMode { get; set; }

    /// <summary>
    /// How often and how heavy pictures go to the navigation screen: Normal, Low, or Auto (Low while the phone's battery saver is on).
    /// </summary>
    NavRefreshMode NavRefresh { get; set; }

    /// <summary>
    /// Whether this platform can play videos for Reels Mode.
    /// </summary>
    bool ReelsSupported { get; }

    /// <summary>
    /// Name of the chosen reels folder, or null if none.
    /// </summary>
    string? ReelsFolderName { get; }

    /// <summary>
    /// Chooses the folder of videos for Reels Mode (the URI from the system folder picker).
    /// </summary>
    void SetReelsFolder(string folderUri, string? displayName);

    /// <summary>
    /// Keep the Virtual Pit Board showing the custom VPB screen (off by default; it replaces the demo image and one-off messages).
    /// </summary>
    bool VpbScreenEnabled { get; set; }

    /// <summary>
    /// The saved screen for a display (the built-in one if none saved) as AXAML text.
    /// </summary>
    string GetScreenAxaml(ScreenTarget target);

    /// <summary>
    /// Checks and saves a screen and shows it on the bike. Returns the problems found (nothing is saved if there are any).
    /// </summary>
    IReadOnlyList<ScreenIssue> SaveScreenAxaml(ScreenTarget target, string axaml);

    /// <summary>
    /// Forgets the saved screen; the built-in one is used again.
    /// </summary>
    void ResetScreenDocument(ScreenTarget target);

    /// <summary>
    /// Ask the bike for live data (RPM, temperatures, ...). Adds continuous Bluetooth traffic; off by default.
    /// </summary>
    bool CanDataEnabled { get; set; }

    /// <summary>
    /// Which CAN IDs are requested (a subset of <c>CanSignals.KnownIds</c>); the default is the throttle only.
    /// </summary>
    IReadOnlyList<int> CanSignalIds { get; set; }

    /// <summary>
    /// Extra CAN IDs to ask for while exploring (any value up to 0xFFFF); the bike's answers appear in <see cref="CanRaw"/>.
    /// </summary>
    IReadOnlyList<int> CanExtraIds { get; set; }

    /// <summary>
    /// Asks the bike which servers and data IDs it supports and returns a text report. Needs a connection.
    /// </summary>
    Task<string> DiscoverSupportedSignalsAsync();

    /// <summary>
    /// The rider's saved ride modes (profiles), kept on the phone.
    /// </summary>
    ProfileLibrary ProfileLibrary { get; }

    /// <summary>
    /// The ride modes last read from the bike (empty until read).
    /// </summary>
    IReadOnlyList<YrcProfile> BikeProfiles { get; }

    /// <summary>
    /// Raised when <see cref="BikeProfiles"/> changed. May fire on any thread.
    /// </summary>
    event Action? RideModesChanged;

    /// <summary>
    /// Reads every ride mode from the bike; returns a line for display.
    /// </summary>
    Task<string> ReadRideModesAsync();

    /// <summary>
    /// Sends ride modes to the bike; returns a line for display.
    /// </summary>
    Task<string> SendRideModesAsync(IReadOnlyList<YrcProfile> profiles);

    /// <summary>
    /// The lap timer that reports laps to the bike, or null where there is no bike link.
    /// </summary>
    LapTimer? LapTimer { get; }

    /// <summary>
    /// Decoded bike data that is fresh right now.
    /// </summary>
    CanValues CanValues { get; }

    /// <summary>
    /// The latest bytes of every CAN ID we asked for, ordered by ID.
    /// </summary>
    IReadOnlyList<CanRawReading> CanRaw { get; }

    /// <summary>
    /// Raised when bike data arrived (at most about twice a second). May fire on any thread.
    /// </summary>
    event Action? CanChanged;

    void Connect();
    void Disconnect();
    void ClearLogs();
    void SaveCredentials(string ccuId, string passKey, string deviceName);
    void SetVpbEnabled(bool enabled);
    void SetNavigationEnabled(bool enabled);
    void OpenNotificationAccessSettings();

    /// <summary>
    /// Sends an image (PNG, JPEG, ...) to the Virtual Pit Board. False if it couldn't be shown (not connected, VPB off, rejected).
    /// </summary>
    Task<bool> SendVpbImageAsync(byte[] encodedImage);

    /// <summary>
    /// Clears the Virtual Pit Board.
    /// </summary>
    Task<bool> ClearVpbAsync();

    /// <summary>Shows a turn-by-turn instruction on the bike (needs the navigation link). False if the link is not up.</summary>
    bool SendTurnGuidance(CNCO.YccuSdk.Navigation.Protocol.TurnGuidance turn);

    /// <summary>Takes the turn-by-turn instruction off the bike.</summary>
    bool ClearTurnGuidance();
}

public enum ConnectButtonState { Idle, Connecting, Connected }

public static class BikeSnapshotExtensions
{
    public static ConnectButtonState GetConnectButtonState(this BikeDebugState state) =>
        state.ConnectionLabel == "Done" ? ConnectButtonState.Connected
        : state.IsBusy ? ConnectButtonState.Connecting
        : ConnectButtonState.Idle;
}
