using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Screens;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class MediaPositionTests
{
    private static YmcMediaMetaData Track(YmcMediaPlaybackState state, int elapsed, int duration = 200, float rate = 1f) =>
        new("Artist", "Album", "Song", duration, state, rate, elapsed);

    [Fact]
    public void Playing_MovesOnByTheTimeSinceTheSnapshot()
    {
        var later = Track(YmcMediaPlaybackState.Playing, 10).Advanced(TimeSpan.FromSeconds(7.5));
        Assert.Equal(17, later.ElapsedSeconds);
    }

    [Fact]
    public void Playing_FollowsThePlaybackRate()
    {
        var later = Track(YmcMediaPlaybackState.Playing, 10, rate: 2f).Advanced(TimeSpan.FromSeconds(5));
        Assert.Equal(20, later.ElapsedSeconds);
    }

    [Fact]
    public void Paused_StaysWhereItWas()
    {
        var later = Track(YmcMediaPlaybackState.Paused, 10).Advanced(TimeSpan.FromSeconds(30));
        Assert.Equal(10, later.ElapsedSeconds);
    }

    [Fact]
    public void Playing_NeverRunsPastTheEndOfTheTrack()
    {
        var later = Track(YmcMediaPlaybackState.Playing, 195, duration: 200).Advanced(TimeSpan.FromSeconds(60));
        Assert.Equal(200, later.ElapsedSeconds);
    }

    [Fact]
    public void Playing_WithUnknownLength_KeepsCounting()
    {
        var later = Track(YmcMediaPlaybackState.Playing, 5, duration: 0).Advanced(TimeSpan.FromSeconds(10));
        Assert.Equal(15, later.ElapsedSeconds);
    }

    [Fact]
    public void ANegativeGap_ChangesNothing()
    {
        var track = Track(YmcMediaPlaybackState.Playing, 10);
        Assert.Equal(10, track.Advanced(TimeSpan.FromSeconds(-3)).ElapsedSeconds);
    }

    [Fact]
    public void Page_ShowingTheTrackPosition_IsDetected()
    {
        Assert.True(new ScreenPage { Xaml = "<MediaProgress Value=\"{Binding Progress}\" />" }.UsesMediaPosition);
        Assert.True(new ScreenPage { Xaml = "<VarText Template=\"{}{elapsed} / {duration}\" />" }.UsesMediaPosition);
        Assert.False(new ScreenPage { Xaml = "<VarText Template=\"{}{song}\" />" }.UsesMediaPosition);
    }
}
