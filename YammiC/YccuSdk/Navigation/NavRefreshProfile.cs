namespace CNCO.YccuSdk.Navigation;

/// <summary>
/// How the navigation screen's refresh rate is chosen.
/// </summary>
public enum NavRefreshMode
{
    /// <summary>
    /// Normal, except Low while the phone's battery saver is on.
    /// </summary>
    Auto,

    Normal,

    Low,
}

/// <summary>
/// The numbers behind "how often and how heavy" pictures go to the bike. <see cref="Normal"/> is what the app always did;
/// <see cref="Low"/> trades smoothness and sharpness for less Bluetooth traffic and battery.
/// </summary>
/// <param name="Name">For the log and the settings text.</param>
/// <param name="SenderFps">Cap on pictures sent per second, whatever makes them.</param>
/// <param name="IdleResendFps">How often the last picture is repeated when nothing new comes in (keeps the feed looking alive).</param>
/// <param name="ReelsFps">Frames per second asked of a Reels video.</param>
/// <param name="ReelsJpegQuality">JPEG quality of Reels frames.</param>
/// <param name="ScreenJpegQuality">JPEG quality of the custom screens' pictures.</param>
/// <param name="ScreenTick">How often a custom screen checks whether it changed.</param>
/// <param name="ScreenFastTick">The same while the screen shows live bike data.</param>
/// <param name="ScrollStep">How often a screen with scrolling text is redrawn while music shows (each step moves the text a little).</param>
public sealed record NavRefreshProfile(
    string Name,
    int SenderFps,
    int IdleResendFps,
    int ReelsFps,
    int ReelsJpegQuality,
    int ScreenJpegQuality,
    TimeSpan ScreenTick,
    TimeSpan ScreenFastTick,
    TimeSpan ScrollStep)
{
    public static NavRefreshProfile Normal { get; } = new(
        "Normal", SenderFps: 15, IdleResendFps: 2, ReelsFps: 8, ReelsJpegQuality: 50, ScreenJpegQuality: 85,
        ScreenTick: TimeSpan.FromSeconds(1), ScreenFastTick: TimeSpan.FromMilliseconds(500),
        ScrollStep: TimeSpan.FromMilliseconds(150));

    // Pictures are only drawn when something changed, so checking often is cheap; the saving comes from smaller pictures
    // (less to draw, compress and send) and fewer repeats, not from reacting slowly.
    public static NavRefreshProfile Low { get; } = new(
        "Low", SenderFps: 10, IdleResendFps: 1, ReelsFps: 5, ReelsJpegQuality: 35, ScreenJpegQuality: 55,
        ScreenTick: TimeSpan.FromSeconds(1), ScreenFastTick: TimeSpan.FromSeconds(1),
        ScrollStep: TimeSpan.FromMilliseconds(300));

    /// <summary>
    /// The profile to use for <paramref name="mode"/> given whether the phone is in battery saver.
    /// </summary>
    public static NavRefreshProfile For(NavRefreshMode mode, bool batterySaver) => mode switch
    {
        NavRefreshMode.Low => Low,
        NavRefreshMode.Normal => Normal,
        _ => batterySaver ? Low : Normal,
    };

    /// <summary>
    /// Reads a saved mode name; anything unknown (or nothing) is <see cref="NavRefreshMode.Auto"/>.
    /// </summary>
    public static NavRefreshMode ParseMode(string? text) =>
        Enum.TryParse<NavRefreshMode>(text, out var mode) && Enum.IsDefined(mode) ? mode : NavRefreshMode.Auto;
}
