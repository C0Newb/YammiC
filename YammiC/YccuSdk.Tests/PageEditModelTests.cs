using CNCO.YccuSdk.Screens;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class PageEditModelTests
{
    private static PageEditModel DefaultNav() =>
        new(ScreenTarget.Navigation, ScreenLayoutMigration.ToDocument(ScreenDefaults.Navigation()).Pages[0].Xaml);

    private static PageEditModel DefaultVpb() =>
        new(ScreenTarget.VirtualPitBoard, ScreenDefaults.Document(ScreenTarget.VirtualPitBoard).Pages[0].Xaml);

    [Fact]
    public void ListsTheElementsOfAMigratedScreen()
    {
        var m = DefaultNav();
        Assert.Equal(new[] { "Clock", "Divider", "Play state", "Song", "Artist", "No music" }, m.Elements.Select(e => e.Name));
        var clock = m.Elements[0];
        Assert.Equal(EditKind.Text, clock.Kind);
        Assert.Equal("{time}", clock.Template);   // shown unescaped
        Assert.Equal(120f, clock.FontSize);
        Assert.True(clock.Bold);
        Assert.Equal(ScreenTextAlign.Center, clock.Align);
        Assert.Equal("#111111", clock.Color);
        Assert.Equal("#FFFFFF", clock.NightColor);
        Assert.Equal(EditKind.Box, m.Elements[1].Kind);
        Assert.Equal(ScreenShowWhen.Always, clock.ShowWhen);
        Assert.Equal(ScreenShowWhen.MusicPresent, m.Elements[3].ShowWhen);
        Assert.Equal("#F4F4F2", m.RootBackground);
        Assert.Equal("#000000", m.RootNightBackground);
    }

    [Fact]
    public void EditsWriteThroughToTheXaml()
    {
        var m = DefaultNav();
        var song = m.Elements[3];
        song.Template = "{artist} / {song}";
        song.FontSize = 20;
        song.Bold = false;
        song.Color = "#ABCDEF";
        song.NightColor = null;
        song.ShowWhen = ScreenShowWhen.MusicPlaying;

        var again = new PageEditModel(ScreenTarget.Navigation, m.ToXaml());
        var s = again.Elements[3];
        Assert.Equal("{artist} / {song}", s.Template);
        Assert.Equal(20f, s.FontSize);
        Assert.False(s.Bold);
        Assert.Equal("#ABCDEF", s.Color);
        Assert.Null(s.NightColor);
        Assert.Equal(ScreenShowWhen.MusicPlaying, s.ShowWhen);
        Assert.Empty(ScreenXamlValidator.Validate(m.ToXaml()));
    }

    [Fact]
    public void Template_StartingWithABrace_IsEscapedInTheXaml()
    {
        var m = DefaultNav();
        m.Elements[0].Template = "{time}";
        Assert.Contains("Template=\"{}{time}\"", m.ToXaml());
    }

    [Fact]
    public void SetBounds_ClampsToTheNavScreen()
    {
        var m = DefaultNav();
        var e = m.Elements[0];
        m.SetBounds(e, -50, -50, 9999, 9999);
        Assert.Equal((0f, 0f, 480f, 240f), (e.X, e.Y, e.Width, e.Height));
        m.SetBounds(e, 470, 230, 100, 100);
        Assert.Equal((380f, 140f, 100f, 100f), (e.X, e.Y, e.Width, e.Height));
    }

    [Fact]
    public void SetBounds_ClampsToTheVisibleVpbStrip()
    {
        var m = DefaultVpb();
        var e = m.Elements[0];
        m.SetBounds(e, 0, 0, 600, 300);
        Assert.Equal((0f, 163f, 526f, 66f), (e.X, e.Y, e.Width, e.Height));
    }

    [Fact]
    public void AddTextBoxProgressImage_AppendAndAreValid()
    {
        var m = DefaultNav();
        var text = m.AddText();
        var box = m.AddBox();
        var bar = m.AddProgress();
        var image = m.AddImage("QUJD");
        Assert.Equal(10, m.Elements.Count);
        Assert.Equal(new[] { EditKind.Text, EditKind.Box, EditKind.Progress, EditKind.Image }, new[] { text.Kind, box.Kind, bar.Kind, image.Kind });
        Assert.Empty(ScreenXamlValidator.Validate(m.ToXaml()));
        var area = ScreenSizes.Bounds(ScreenTarget.Navigation);
        Assert.All(m.Elements.Skip(6), e => Assert.True(e.X >= area.X && e.Y >= area.Y && e.X + e.Width <= area.X + area.Width && e.Y + e.Height <= area.Y + area.Height));
    }

    [Fact]
    public void ImageData_ReadsAndReplacesThePicture()
    {
        var m = DefaultNav();
        var image = m.AddImage("QUJD");
        Assert.Equal("QUJD", image.ImageData);
        image.ImageData = "REVG";
        Assert.Equal("REVG", new PageEditModel(ScreenTarget.Navigation, m.ToXaml()).Elements.Last().ImageData);
        Assert.Null(m.Elements[0].ImageData);       // a text element has none
    }

    [Fact]
    public void DeleteDuplicateAndReorder()
    {
        var m = DefaultNav();
        var copy = m.Duplicate(m.Elements[3]);
        Assert.Equal(7, m.Elements.Count);
        Assert.Equal("Song copy", copy.Name);
        Assert.Equal(m.Elements[3].Template, copy.Template);

        m.Delete(copy);
        Assert.Equal(6, m.Elements.Count);

        var first = m.Elements[0];
        m.Move(first, 1);
        Assert.Equal("Divider", m.Elements[0].Name);
        Assert.Equal("Clock", m.Elements[1].Name);
        m.Move(first, -5);                        // clamps at the back
        Assert.Equal("Clock", m.Elements[0].Name);
    }

    [Fact]
    public void UnknownContent_IsKeptAndListedAsOther()
    {
        const string xaml = """
            <y:ScreenCanvas xmlns="https://github.com/avaloniaui" xmlns:y="clr-namespace:CNCO.YammiC.Screens;assembly=YammiC" Width="480" Height="240">
              <Rectangle Canvas.Left="5" Canvas.Top="6" Width="7" Height="8" Fill="#123456" />
              <Grid />
            </y:ScreenCanvas>
            """;
        var m = new PageEditModel(ScreenTarget.Navigation, xaml);
        Assert.Equal(EditKind.Other, m.Elements[0].Kind);
        Assert.Equal((5f, 6f, 7f, 8f), (m.Elements[0].X, m.Elements[0].Y, m.Elements[0].Width, m.Elements[0].Height));
        Assert.Contains("<Grid", m.ToXaml());      // untouched
    }

    [Fact]
    public void BadXaml_Throws()
    {
        Assert.ThrowsAny<Exception>(() => new PageEditModel(ScreenTarget.Navigation, "<oops"));
    }
}

public class PageEditModelAlbumArtTests
{
    private static PageEditModel Blank() =>
        new(ScreenTarget.Navigation, ScreenDefaults.Document(ScreenTarget.VirtualPitBoard).Pages[0].Xaml);

    [Fact]
    public void AddAlbumArt_AddsAnImageThatShowsTheTracksPicture()
    {
        var m = new PageEditModel(ScreenTarget.Navigation, ScreenLayoutMigration.ToDocument(new ScreenLayout { Target = ScreenTarget.Navigation }).Pages[0].Xaml);
        var art = m.AddAlbumArt();
        Assert.Equal(EditKind.Image, art.Kind);
        Assert.Equal(ImageOrigin.AlbumArt, art.ImageOrigin);
        Assert.Empty(ScreenXamlValidator.Validate(m.ToXaml()));
        Assert.Equal(ImageOrigin.AlbumArt, new PageEditModel(ScreenTarget.Navigation, m.ToXaml()).Elements.Single().ImageOrigin);
    }

    [Fact]
    public void ImageOrigin_SwitchesBetweenEmbeddedAndAlbumArt()
    {
        var m = new PageEditModel(ScreenTarget.Navigation, ScreenLayoutMigration.ToDocument(new ScreenLayout { Target = ScreenTarget.Navigation }).Pages[0].Xaml);
        var image = m.AddImage("QUJD");
        Assert.Equal(ImageOrigin.Embedded, image.ImageOrigin);
        image.ImageOrigin = ImageOrigin.AlbumArt;
        Assert.Contains("Origin=\"AlbumArt\"", m.ToXaml());
        image.ImageOrigin = ImageOrigin.Embedded;
        Assert.DoesNotContain("Origin=", m.ToXaml());
        Assert.Equal("QUJD", image.ImageData);       // the embedded picture is kept while album art is selected
    }
}

public class PageEditModelProgressNightTests
{
    [Fact]
    public void ProgressBar_NightColoursMapToNightFillAndNightTrack()
    {
        var m = new PageEditModel(ScreenTarget.Navigation, ScreenLayoutMigration.ToDocument(new ScreenLayout { Target = ScreenTarget.Navigation }).Pages[0].Xaml);
        var bar = m.AddProgress();
        bar.NightColor = "#4FC3F7";
        bar.NightBackground = "#333333";

        var xaml = m.ToXaml();
        Assert.Contains("NightFill=\"#4FC3F7\"", xaml);
        Assert.Contains("NightTrack=\"#333333\"", xaml);
        Assert.Empty(ScreenXamlValidator.Validate(xaml));

        var again = new PageEditModel(ScreenTarget.Navigation, xaml).Elements.Single();
        Assert.Equal("#4FC3F7", again.NightColor);
        Assert.Equal("#333333", again.NightBackground);
    }
}
