using System.ComponentModel;
using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Screens;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class ScreenStateTests
{
    private static readonly DateTime Noon = new(2026, 10, 19, 12, 5, 9);

    private static ScreenContext Ctx(YmcMediaMetaData? media = null, bool night = false, DateTime? now = null) =>
        new(now ?? Noon, media, night);

    private static YmcMediaMetaData Song(YmcMediaPlaybackState state = YmcMediaPlaybackState.Playing, int elapsed = 83) =>
        new("Queen", "Opera", "Bohemian", 355, state, 1f, elapsed);

    [Fact]
    public void Apply_RaisesPropertyChangedForEverything()
    {
        var state = new ScreenState();
        string? raised = null;
        state.PropertyChanged += (_, e) => raised = e.PropertyName;
        state.Apply(ScreenSnapshot.Of(Ctx()));
        Assert.Equal(string.Empty, raised);
    }

    [Fact]
    public void NoMedia_ReportsNoMusic()
    {
        var state = new ScreenState();
        state.Apply(ScreenSnapshot.Of(Ctx()));
        Assert.True(state.NoMusic);
        Assert.False(state.HasMusic);
        Assert.Equal(0, state.Progress);
        Assert.Equal("", state.Song);
    }

    [Fact]
    public void Playing_ExposesTrackAndProgress()
    {
        var state = new ScreenState();
        state.Apply(ScreenSnapshot.Of(Ctx(Song())));
        Assert.True(state.HasMusic);
        Assert.True(state.IsPlaying);
        Assert.False(state.IsPaused);
        Assert.Equal("Bohemian", state.Song);
        Assert.Equal(83d / 355d, state.Progress, 3);
        Assert.Equal("1:23", state.Elapsed);
    }

    [Fact]
    public void Paused_IsPausedNotPlaying()
    {
        var state = new ScreenState();
        state.Apply(ScreenSnapshot.Of(Ctx(Song(YmcMediaPlaybackState.Paused))));
        Assert.True(state.IsPaused);
        Assert.False(state.IsPlaying);
    }

    [Fact]
    public void NightAndDay_AreOpposites()
    {
        var state = new ScreenState();
        state.Apply(ScreenSnapshot.Of(Ctx(night: true)));
        Assert.True(state.Night);
        Assert.False(state.Day);
    }

    [Fact]
    public void Resolve_KeepsTheTemplateLanguage()
    {
        var state = new ScreenState();
        state.Apply(ScreenSnapshot.Of(Ctx(Song())));
        Assert.Equal("Queen - Bohemian 12:05", state.Resolve("{artist} - {song} {time}"));
        Assert.Equal("{x}", state.Resolve("{{x}}"));
    }

    [Fact]
    public void Flags_DefaultFalseAndFollowSnapshot()
    {
        var state = new ScreenState();
        state.Apply(ScreenSnapshot.Of(Ctx()) with { Flags = new Dictionary<string, bool> { ["menu"] = true } });
        Assert.True(state.Flags["menu"]);
        Assert.False(state.Flags["other"]);
    }

    [Fact]
    public void Signature_IgnoresSecondsUnlessAsked()
    {
        var a = ScreenSnapshot.Of(Ctx(now: Noon));
        var b = ScreenSnapshot.Of(Ctx(now: Noon.AddSeconds(20)));
        Assert.Equal(a.Signature(false), b.Signature(false));
        Assert.NotEqual(a.Signature(true), b.Signature(true));
    }

    [Fact]
    public void Signature_ChangesWithSongNightAndPage()
    {
        var baseSnap = ScreenSnapshot.Of(Ctx(Song()));
        Assert.NotEqual(baseSnap.Signature(false), ScreenSnapshot.Of(Ctx(Song() with { SongName = "Other" })).Signature(false));
        Assert.NotEqual(baseSnap.Signature(false), ScreenSnapshot.Of(Ctx(Song(), night: true)).Signature(false));
        Assert.NotEqual(baseSnap.Signature(false), ScreenSnapshot.Of(Ctx(Song()), "page2", 1, 2).Signature(false));
    }
}

public class ScreenStateArtTests
{
    private static readonly DateTime Noon = new(2026, 10, 19, 12, 5, 9);

    private static ScreenSnapshot Snap(byte[]? art) => ScreenSnapshot.Of(new ScreenContext(Noon, null, false, 1f, null, art));

    [Fact]
    public void AlbumArt_IsExposedToPages()
    {
        var state = new ScreenState();
        state.Apply(Snap(null));
        Assert.Null(state.AlbumArt);
        Assert.False(state.HasAlbumArt);

        var art = new byte[] { 1, 2, 3 };
        state.Apply(Snap(art));
        Assert.Same(art, state.AlbumArt);
        Assert.True(state.HasAlbumArt);

        state.Apply(Snap(Array.Empty<byte>()));
        Assert.False(state.HasAlbumArt);   // empty means none
    }

    [Fact]
    public void Signature_ChangesWhenThePictureChanges()
    {
        var none = Snap(null).Signature(false);
        var a = Snap(new byte[] { 1, 2, 3, 4 }).Signature(false);
        var b = Snap(new byte[] { 1, 2, 3, 5 }).Signature(false);   // same size, different picture
        Assert.NotEqual(none, a);
        Assert.NotEqual(a, b);
        Assert.Equal(a, Snap(new byte[] { 1, 2, 3, 4 }).Signature(false));
    }
}
