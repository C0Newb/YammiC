namespace CNCO.YccuSdk.Screens;

/// <summary>The layouts used until the user saves their own.</summary>
public static class ScreenDefaults
{
    public static ScreenLayout For(ScreenTarget target) =>
        target == ScreenTarget.Navigation ? Navigation() : VirtualPitBoard();

    /// <summary>Clock, divider, and now-playing (or "No music playing"). Day and night colours.</summary>
    public static ScreenLayout Navigation() => new()
    {
        Name = "Clock and music",
        Target = ScreenTarget.Navigation,
        Background = "#F4F4F2",
        NightBackground = "#000000",
        Elements =
        {
            new ScreenElement
            {
                Name = "Clock", Kind = ScreenElementKind.Text, Text = "{time}",
                X = 0, Y = 0, Width = 480, Height = 144, FontSize = 120, Bold = true, Align = ScreenTextAlign.Center,
                Color = "#111111", NightColor = "#FFFFFF",
            },
            new ScreenElement
            {
                Name = "Divider", Kind = ScreenElementKind.Text, Text = "",
                X = 20, Y = 143, Width = 440, Height = 2, Background = "#C8C8C4", NightBackground = "#3C4043",
            },
            new ScreenElement
            {
                Name = "Play state", Kind = ScreenElementKind.Text, Text = "{playicon}", ShowWhen = ScreenShowWhen.MusicPresent,
                X = 20, Y = 158, Width = 40, Height = 64, FontSize = 30, Align = ScreenTextAlign.Left,
                Color = "#0B6BCB", NightColor = "#4FC3F7",
            },
            new ScreenElement
            {
                Name = "Song", Kind = ScreenElementKind.Text, Text = "{song}", ShowWhen = ScreenShowWhen.MusicPresent,
                X = 64, Y = 150, Width = 396, Height = 44, FontSize = 32, Bold = true,
                Color = "#111111", NightColor = "#FFFFFF",
            },
            new ScreenElement
            {
                Name = "Artist", Kind = ScreenElementKind.Text, Text = "{artist}", ShowWhen = ScreenShowWhen.MusicPresent,
                X = 64, Y = 188, Width = 396, Height = 36, FontSize = 25,
                Color = "#5F6368", NightColor = "#9AA0A6",
            },
            new ScreenElement
            {
                Name = "No music", Kind = ScreenElementKind.Text, Text = "No music playing", ShowWhen = ScreenShowWhen.NoMusic,
                X = 20, Y = 158, Width = 440, Height = 64, FontSize = 32, Bold = true,
                Color = "#5F6368", NightColor = "#9AA0A6",
            },
        },
    };

    /// <summary>
    /// A song / artist line in the strip the CCU shows (the 526x66 area at (0, 163)); falls back to the clock
    /// when nothing is playing.
    /// </summary>
    public static ScreenLayout VirtualPitBoard() => new()
    {
        Name = "Now playing strip",
        Target = ScreenTarget.VirtualPitBoard,
        Background = "#000000",
        Elements =
        {
            new ScreenElement
            {
                Name = "Strip", Kind = ScreenElementKind.Text, Text = "{artist} - {song}", ShowWhen = ScreenShowWhen.MusicPresent,
                X = 0, Y = 163, Width = 526, Height = 66, FontSize = 36, Align = ScreenTextAlign.Center,
                Color = "#FFFFFF", Background = "#1565C0",
            },
            new ScreenElement
            {
                Name = "Clock strip", Kind = ScreenElementKind.Text, Text = "{time}", ShowWhen = ScreenShowWhen.NoMusic,
                X = 0, Y = 163, Width = 526, Height = 66, FontSize = 44, Bold = true, Align = ScreenTextAlign.Center,
                Color = "#FFFFFF", Background = "#37474F",
            },
        },
    };
}
