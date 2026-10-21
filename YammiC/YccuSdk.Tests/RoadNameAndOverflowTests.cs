using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Navigation;
using CNCO.YccuSdk.Screens;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class RoadNameTests
{
    private static Func<string, bool> Deliver(List<string> sent) => text =>
    {
        sent.Add(text);
        return true;
    };

    private static ScreenContext Playing(string song = "Song") =>
        new(DateTime.Now, new YmcMediaMetaData("Artist", "Album", song, 200, YmcMediaPlaybackState.Playing, 1f, 5), false);

    private static ScreenDocument Doc(string? documentRoad, string? pageRoad)
    {
        var doc = new ScreenDocument { Target = ScreenTarget.Navigation, DefaultRoadName = documentRoad };
        doc.Pages.Add(new ScreenPage { Id = "main", Name = "Main", Xaml = "<ScreenCanvas />", RoadName = pageRoad });
        return doc;
    }

    [Fact]
    public void RoundTrips_OnTheDocumentAndOnAPage()
    {
        var doc = Doc("{song}", "{artist}");
        var back = ScreenDocument.Parse(doc.ToAxaml());
        Assert.Equal("{song}", back.DefaultRoadName);
        Assert.Equal("{artist}", back.Pages[0].RoadName);
    }

    [Fact]
    public void NotSet_StaysNull_AndSurvivesClone()
    {
        var back = ScreenDocument.Parse(Doc(null, null).ToAxaml());
        Assert.Null(back.DefaultRoadName);
        Assert.Null(back.Pages[0].RoadName);
        Assert.Equal("{song}", Doc("{song}", "x").Clone().DefaultRoadName);
        Assert.Equal("x", Doc("{song}", "x").Clone().Pages[0].RoadName);
    }

    [Fact]
    public void AnEmptyPageValue_IsKept_SoAPageCanBlankTheRoadName()
    {
        var back = ScreenDocument.Parse(Doc("{song}", "").ToAxaml());
        Assert.Equal("", back.Pages[0].RoadName);
    }

    [Fact]
    public void Controller_UsesThePageValue_ElseTheDocumentDefault_ElseNothing()
    {
        Assert.Equal("Song", new ScreenController(Doc("{song}", null)).RoadNameFor(Playing()));
        Assert.Equal("Artist", new ScreenController(Doc("{song}", "{artist}")).RoadNameFor(Playing()));
        Assert.Equal("", new ScreenController(Doc("{song}", "")).RoadNameFor(Playing()));
        Assert.Equal("", new ScreenController(Doc(null, null)).RoadNameFor(Playing()));
    }

    [Fact]
    public void Controller_MixesTextAndVariables()
    {
        Assert.Equal("Artist - Song", new ScreenController(Doc("{artist} - {song}", null)).RoadNameFor(Playing()));
    }

    [Fact]
    public void Sender_SendsOnlyWhenTheTextChanges()
    {
        var sent = new List<string>();
        var sender = new RoadNameSender(() => true, Deliver(sent));
        sender.Update("A");
        sender.Update("A");
        sender.Update("B");
        Assert.Equal(new[] { "A", "B" }, sent);
    }

    [Fact]
    public void Sender_WaitsUntilItCan_ThenSendsTheLatest()
    {
        var sent = new List<string>();
        var can = false;
        var sender = new RoadNameSender(() => can, Deliver(sent));
        sender.Update("A");
        Assert.Empty(sent);
        can = true;
        sender.Update("A");
        Assert.Equal(new[] { "A" }, sent);
    }

    [Fact]
    public void Sender_RetriesWhatDidNotGoThrough()
    {
        var attempts = new List<string>();
        var works = false;
        var sender = new RoadNameSender(() => true, text =>
        {
            attempts.Add(text);
            return works;
        });
        sender.Update("A");
        sender.Update("A");
        works = true;
        sender.Update("A");
        sender.Update("A");
        Assert.Equal(new[] { "A", "A", "A" }, attempts);
    }

    [Fact]
    public void Sender_Reset_SendsTheSameTextAgain()
    {
        var sent = new List<string>();
        var sender = new RoadNameSender(() => true, Deliver(sent));
        sender.Update("A");
        sender.Reset();
        sender.Update("A");
        Assert.Equal(new[] { "A", "A" }, sent);
    }
}

public class OverflowTests
{
    [Fact]
    public void Marquee_HoldsAtTheStart_ThenScrolls_ThenHoldsAtTheEnd()
    {
        const double overflow = 80;
        Assert.Equal(0, ScreenMarquee.Offset(overflow, TimeSpan.Zero));
        Assert.Equal(0, ScreenMarquee.Offset(overflow, ScreenMarquee.StartPause));
        var half = ScreenMarquee.Offset(overflow, ScreenMarquee.StartPause + TimeSpan.FromSeconds(1));
        Assert.InRange(half, 1, overflow - 1);
        var travel = TimeSpan.FromSeconds(overflow / ScreenMarquee.PixelsPerSecond);
        Assert.Equal(overflow, ScreenMarquee.Offset(overflow, ScreenMarquee.StartPause + travel + TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void Marquee_RestartsAfterTheEndHold()
    {
        const double overflow = 80;
        var travel = TimeSpan.FromSeconds(overflow / ScreenMarquee.PixelsPerSecond);
        var cycle = ScreenMarquee.StartPause + travel + ScreenMarquee.EndPause;
        Assert.Equal(0, ScreenMarquee.Offset(overflow, cycle + TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public void Marquee_NeverMovesTextThatFits()
    {
        Assert.Equal(0, ScreenMarquee.Offset(0, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, ScreenMarquee.Offset(-12, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Marquee_NeverMovesBackwards()
    {
        var last = 0.0;
        for (var ms = 0; ms < 20000; ms += 50)
        {
            var offset = ScreenMarquee.Offset(120, TimeSpan.FromMilliseconds(ms));
            if (ms < 3000) Assert.True(offset >= last - 0.001);
            last = offset;
        }
    }

    [Fact]
    public void Page_WithScrollingText_IsDetected()
    {
        Assert.True(new ScreenPage { Xaml = "<VarText Template=\"{}{album}\" Overflow=\"Scroll\" />" }.UsesScroll);
        Assert.False(new ScreenPage { Xaml = "<VarText Template=\"{}{album}\" Overflow=\"Wrap\" />" }.UsesScroll);
        Assert.False(new ScreenPage { Xaml = "<VarText Template=\"{}{album}\" />" }.UsesScroll);
    }

    [Theory]
    [InlineData("Trim")]
    [InlineData("Wrap")]
    [InlineData("Scroll")]
    public void Validator_AllowsTheOverflowAttribute(string value)
    {
        var xaml = $"<ScreenCanvas xmlns=\"{ScreenDocument.AvaloniaNs}\" xmlns:y=\"{ScreenDocument.WidgetNs}\"><y:VarText Template=\"x\" Overflow=\"{value}\" /></ScreenCanvas>";
        Assert.Empty(ScreenXamlValidator.Validate(xaml));
    }

    private static ScreenSnapshot At(int millis, bool playing)
    {
        var media = playing ? new YmcMediaMetaData("A", "B", "S", 200, YmcMediaPlaybackState.Playing, 1f, 5) : null;
        var now = new DateTime(2026, 1, 1, 12, 0, 0).AddMilliseconds(millis);
        return ScreenSnapshot.Of(new ScreenContext(now, media, false));
    }

    [Fact]
    public void Signature_ChangesWithTheScrollStep_OnlyWhileMusicShows()
    {
        var step = TimeSpan.FromMilliseconds(200);
        Assert.NotEqual(At(0, true).Signature(false, step), At(250, true).Signature(false, step));
        Assert.Equal(At(0, true).Signature(false, step), At(50, true).Signature(false, step));
        Assert.Equal(At(0, false).Signature(false, step), At(250, false).Signature(false, step));
        Assert.Equal(At(0, true).Signature(false), At(250, true).Signature(false));
    }

    [Fact]
    public void Profiles_ScrollSlowerOnLow()
    {
        Assert.True(NavRefreshProfile.Low.ScrollStep > NavRefreshProfile.Normal.ScrollStep);
        Assert.True(NavRefreshProfile.Normal.ScrollStep >= TimeSpan.FromMilliseconds(100));
    }
}

public class TextOverflowEditTests
{
    private static PageEditModel Model() => new(ScreenTarget.Navigation, ScreenDefaults.Document(ScreenTarget.Navigation).Pages[0].Xaml);

    [Fact]
    public void Artist_ReadsAsWrap_FromTheDefaultScreen()
    {
        var artist = Model().Elements.Single(e => e.Template == "{artist}");
        Assert.Equal(ScreenTextOverflow.Wrap, artist.Overflow);
    }

    [Fact]
    public void SettingScroll_WritesTheAttribute_AndTheValidatorStillAcceptsIt()
    {
        var model = Model();
        model.Elements.Single(e => e.Template == "{album}").Overflow = ScreenTextOverflow.Scroll;
        var xaml = model.ToXaml();
        Assert.Contains("Overflow=\"Scroll\"", xaml);
        Assert.Empty(ScreenXamlValidator.Validate(xaml));
        Assert.True(new ScreenPage { Xaml = xaml }.UsesScroll);
    }

    [Fact]
    public void SettingTrim_RemovesTheAttribute()
    {
        var model = Model();
        model.Elements.Single(e => e.Template == "{artist}").Overflow = ScreenTextOverflow.Trim;
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(model.ToXaml(), "Overflow=\"Wrap\"").Count);
        Assert.DoesNotContain("Overflow=\"Trim\"", model.ToXaml());
    }

    [Fact]
    public void ANewTextElement_DefaultsToTrim()
    {
        var model = Model();
        var added = model.AddText();
        Assert.Equal(ScreenTextOverflow.Trim, added.Overflow);
    }
}
