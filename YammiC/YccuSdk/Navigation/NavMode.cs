namespace CNCO.YccuSdk.Navigation;

/// <summary>
/// What the bike's navigation screen shows.
/// </summary>
public enum NavMode
{
    /// <summary>
    /// The demo image / animation (the sandwich).
    /// </summary>
    Sandwich,

    /// <summary>
    /// The custom screen from the screen editor (clock, music, ...).
    /// </summary>
    Ui,

    /// <summary>
    /// Videos from the reels folder, scrolled with the zoom buttons, with sound on the phone.
    /// </summary>
    Reels,
}
