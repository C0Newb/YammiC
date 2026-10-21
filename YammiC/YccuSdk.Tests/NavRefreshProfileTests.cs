using CNCO.YccuSdk.Navigation;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class NavRefreshProfileTests
{
    [Theory]
    [InlineData(NavRefreshMode.Auto, false, "Normal")]
    [InlineData(NavRefreshMode.Auto, true, "Low")]
    [InlineData(NavRefreshMode.Normal, true, "Normal")]
    [InlineData(NavRefreshMode.Normal, false, "Normal")]
    [InlineData(NavRefreshMode.Low, false, "Low")]
    [InlineData(NavRefreshMode.Low, true, "Low")]
    public void Mode_AndBatterySaver_PickTheProfile(NavRefreshMode mode, bool batterySaver, string expected)
    {
        Assert.Equal(expected, NavRefreshProfile.For(mode, batterySaver).Name);
    }

    [Fact]
    public void Normal_IsWhatTheAppDidBefore()
    {
        var p = NavRefreshProfile.Normal;
        Assert.Equal((15, 2, 8, 50, 85), (p.SenderFps, p.IdleResendFps, p.ReelsFps, p.ReelsJpegQuality, p.ScreenJpegQuality));
        Assert.Equal(TimeSpan.FromSeconds(1), p.ScreenTick);
        Assert.Equal(TimeSpan.FromMilliseconds(500), p.ScreenFastTick);
    }

    [Fact]
    public void Low_IsCheaperOnEveryCount()
    {
        var n = NavRefreshProfile.Normal;
        var l = NavRefreshProfile.Low;
        Assert.True(l.SenderFps < n.SenderFps);
        Assert.True(l.IdleResendFps < n.IdleResendFps);
        Assert.True(l.ReelsFps < n.ReelsFps);
        Assert.True(l.ReelsJpegQuality < n.ReelsJpegQuality);
        Assert.True(l.ScreenJpegQuality < n.ScreenJpegQuality);
        Assert.True(l.ScreenFastTick >= n.ScreenFastTick);
        Assert.True(l.IdleResendFps >= 1 && l.SenderFps >= 1);   // never stops the feed
    }

    [Fact]
    public void Low_SavesPowerByShrinkingPicturesNotByLagging()
    {
        // Pictures are only drawn when something changed, so polling is cheap; the cost is in each picture's size.
        var l = NavRefreshProfile.Low;
        Assert.True(l.ScreenTick <= TimeSpan.FromSeconds(1));   // a button press or song change shows within a second
        Assert.True(l.SenderFps >= 8);                           // and isn't held back by the sender's cap
        Assert.True(l.ScreenJpegQuality <= 60);
    }

    [Theory]
    [InlineData(null, NavRefreshMode.Auto)]
    [InlineData("", NavRefreshMode.Auto)]
    [InlineData("junk", NavRefreshMode.Auto)]
    [InlineData("Low", NavRefreshMode.Low)]
    [InlineData("Normal", NavRefreshMode.Normal)]
    [InlineData("Auto", NavRefreshMode.Auto)]
    [InlineData("7", NavRefreshMode.Auto)]
    public void SavedText_ParsesWithAutoAsTheDefault(string? text, NavRefreshMode expected)
    {
        Assert.Equal(expected, NavRefreshProfile.ParseMode(text));
    }
}
