namespace CNCO.YccuSdk.Screens;

/// <summary>The layouts used until the user saves their own.</summary>
public static class ScreenDefaults
{
    public static ScreenLayout For(ScreenTarget target) =>
        target == ScreenTarget.Navigation ? Navigation() : VirtualPitBoard();

    /// <summary>The built-in screen for <paramref name="target"/> as a document: the navigation display gets the now-playing page.</summary>
    public static ScreenDocument Document(ScreenTarget target) =>
        target == ScreenTarget.Navigation ? ScreenDocument.Parse(NowPlayingAxaml) : ScreenLayoutMigration.ToDocument(For(target));

    /// <summary>
    /// Track details on the left, album art on the right (a note shows through while the track has none), a progress bar along
    /// the bottom, and "No music playing" when nothing plays. The zoom buttons skip tracks. Day and night colours.
    /// </summary>
    private const string NowPlayingAxaml = """
        <y:ScreenDocument xmlns="https://github.com/avaloniaui"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                          xmlns:y="clr-namespace:CNCO.YammiC.Screens;assembly=YammiC"
                          Target="Navigation" ZoomIn="Media.Next" ZoomOut="Media.Previous">
          <y:Page Id="main" Name="Now playing">
            <y:ScreenCanvas Width="480" Height="240" Background="#F4F4F2" NightBackground="#000000">
              <y:ScreenBox Tag="Art placeholder" Canvas.Left="262" Canvas.Top="22" Width="196" Height="196"
                           Background="#E0E0DC" NightBackground="#202124" IsVisible="{Binding HasMusic}">
                <y:VarText Template="♪" FontSize="96" Foreground="#B8B8B2" NightForeground="#3C4043"
                           TextAlignment="Center" VerticalAlignment="Center" />
              </y:ScreenBox>
              <y:ScreenBox Tag="Album art" Canvas.Left="262" Canvas.Top="22" Width="196" Height="196" IsVisible="{Binding HasMusic}">
                <y:ScreenImage Origin="AlbumArt" Stretch="UniformToFill" />
              </y:ScreenBox>
              <y:ScreenBox Tag="Song" Canvas.Left="20" Canvas.Top="30" Width="230" Height="84" IsVisible="{Binding HasMusic}">
                <y:VarText Template="{}{song}" FontSize="30" FontWeight="Bold" Foreground="#111111" NightForeground="#FFFFFF"
                           TextAlignment="Left" TextWrapping="Wrap" MaxLines="2" TextTrimming="CharacterEllipsis" VerticalAlignment="Top" />
              </y:ScreenBox>
              <y:ScreenBox Tag="Artist" Canvas.Left="20" Canvas.Top="120" Width="230" Height="32" IsVisible="{Binding HasMusic}">
                <y:VarText Template="{}{artist}" FontSize="22" FontWeight="Normal" Foreground="#5F6368" NightForeground="#9AA0A6"
                           TextAlignment="Left" TextTrimming="CharacterEllipsis" VerticalAlignment="Center" />
              </y:ScreenBox>
              <y:ScreenBox Tag="Album" Canvas.Left="20" Canvas.Top="152" Width="230" Height="28" IsVisible="{Binding HasMusic}">
                <y:VarText Template="{}{album}" FontSize="18" FontWeight="Normal" Foreground="#80868B" NightForeground="#7C8086"
                           TextAlignment="Left" TextTrimming="CharacterEllipsis" VerticalAlignment="Center" />
              </y:ScreenBox>
              <y:ScreenBox Tag="Time" Canvas.Left="20" Canvas.Top="186" Width="230" Height="24" IsVisible="{Binding HasMusic}">
                <y:VarText Template="{}{elapsed} / {duration}" FontSize="18" FontWeight="Normal" Foreground="#5F6368" NightForeground="#9AA0A6"
                           TextAlignment="Left" VerticalAlignment="Center" />
              </y:ScreenBox>
              <y:MediaProgress Tag="Progress" Canvas.Left="20" Canvas.Top="220" Width="440" Height="8" CornerRadius="4"
                               Value="{Binding Progress}" Fill="#0B6BCB" NightFill="#4FC3F7" Track="#D5D5D0" NightTrack="#3C4043"
                               IsVisible="{Binding HasMusic}" />
              <y:ScreenBox Tag="No music" Canvas.Left="20" Canvas.Top="90" Width="440" Height="60" IsVisible="{Binding NoMusic}">
                <y:VarText Template="No music playing" FontSize="32" FontWeight="Bold" Foreground="#5F6368" NightForeground="#9AA0A6"
                           TextAlignment="Center" VerticalAlignment="Center" />
              </y:ScreenBox>
            </y:ScreenCanvas>
          </y:Page>
        </y:ScreenDocument>
        """;

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
