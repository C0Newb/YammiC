using CNCO.YccuSdk.Platform;
using CNCO.YccuSdk.Screens;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class NowPlayingDefaultTests
{
    private sealed class MemoryStore : IYmcKeyValueStore
    {
        public Dictionary<string, string> Values { get; } = new();
        public string? GetString(string key) => Values.TryGetValue(key, out var v) ? v : null;
        public void SetString(string key, string value) => Values[key] = value;
    }

    private static ScreenDocument Nav() => ScreenDefaults.Document(ScreenTarget.Navigation);

    [Fact]
    public void IsOnePageWithTheSkipButtons_AndIsValid()
    {
        var doc = Nav();
        Assert.Single(doc.Pages);
        Assert.Equal("Now playing", doc.Pages[0].Name);
        Assert.Equal("Media.Next", doc.DefaultActions[ScreenEvent.ZoomIn]);
        Assert.Equal("Media.Previous", doc.DefaultActions[ScreenEvent.ZoomOut]);
        Assert.Empty(ScreenXamlValidator.Validate(doc.Pages[0].Xaml));
        Assert.Equal(doc.DefaultActions[ScreenEvent.ZoomIn], ScreenDocument.Parse(doc.ToAxaml()).DefaultActions[ScreenEvent.ZoomIn]);
    }

    [Fact]
    public void ShowsArtOnTheRight_DetailsOnTheLeft_AndAProgressBar_NoClock()
    {
        var m = new PageEditModel(ScreenTarget.Navigation, Nav().Pages[0].Xaml);
        var art = Assert.Single(m.Elements, e => e.Kind == EditKind.Image);
        Assert.Equal(ImageOrigin.AlbumArt, art.ImageOrigin);
        Assert.True(art.X > 240 && art.Y < 120, "album art sits top / centre right");

        var bar = Assert.Single(m.Elements, e => e.Kind == EditKind.Progress);
        Assert.True(bar.Y > 180);

        var templates = m.Elements.Where(e => e.Kind == EditKind.Text).Select(e => e.Template).ToList();
        Assert.Contains("{song}", templates);
        Assert.Contains("{artist}", templates);
        Assert.Contains("{album}", templates);
        Assert.DoesNotContain("{time}", templates);
        Assert.All(m.Elements.Where(e => e.Kind == EditKind.Text && e.Template is "{song}" or "{artist}" or "{album}"), e => Assert.True(e.X < art.X));
    }

    [Fact]
    public void ArtShowsOnlyWithMusic_AndHasAPlaceholder_AndNothingPlayingHasItsOwnText()
    {
        var m = new PageEditModel(ScreenTarget.Navigation, Nav().Pages[0].Xaml);
        Assert.Contains(m.Elements, e => e.Kind == EditKind.Text && e.ShowWhen == ScreenShowWhen.NoMusic);
        Assert.Equal(ScreenShowWhen.MusicPresent, m.Elements.Single(e => e.Kind == EditKind.Progress).ShowWhen);
        Assert.Contains("♪", Nav().Pages[0].Xaml);   // shows through while the track has no picture
        Assert.Contains("Origin=\"AlbumArt\"", Nav().Pages[0].Xaml);
    }

    [Fact]
    public void HasNightColours_AndEverythingIsInsideTheScreen()
    {
        var m = new PageEditModel(ScreenTarget.Navigation, Nav().Pages[0].Xaml);
        Assert.NotEqual("", m.RootNightBackground);
        Assert.All(m.Elements.Where(e => e.Kind == EditKind.Text), e => Assert.NotNull(e.NightColor));
        Assert.All(m.Elements, e =>
        {
            Assert.True(e.X >= 0 && e.Y >= 0 && e.X + e.Width <= 480 && e.Y + e.Height <= 240, $"{e.Name} is inside 480x240");
        });
    }

    [Fact]
    public void Store_UsesItWhenNothingIsSaved_ButKeepsASavedLegacyLayout()
    {
        var memory = new MemoryStore();
        var store = new ScreenDocumentStore(memory);
        Assert.Equal("Now playing", store.Load(ScreenTarget.Navigation).Pages[0].Name);
        Assert.Equal("Media.Next", store.Load(ScreenTarget.Navigation).DefaultActions[ScreenEvent.ZoomIn]);

        var legacy = new ScreenLayout { Name = "Mine", Target = ScreenTarget.Navigation };
        memory.SetString("screen_navigation", ScreenLayoutStore.ToJson(legacy));
        Assert.Equal("Mine", store.Load(ScreenTarget.Navigation).Pages[0].Name);

        store.Reset(ScreenTarget.Navigation);
        Assert.Equal("Now playing", store.Load(ScreenTarget.Navigation).Pages[0].Name);
    }

    [Fact]
    public void ThePitBoardDefaultIsUnchanged()
    {
        var doc = ScreenDefaults.Document(ScreenTarget.VirtualPitBoard);
        Assert.Equal("Now playing strip", doc.Pages[0].Name);
        Assert.Empty(doc.DefaultActions);
    }
}
