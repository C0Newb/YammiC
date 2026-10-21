using CNCO.YccuSdk.Screens;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class ScreenControllerTests
{
    private sealed class FakeMedia : IScreenMediaActions
    {
        public List<string> Calls { get; } = new();
        public void PlayPause() => Calls.Add("playpause");
        public void Next() => Calls.Add("next");
        public void Previous() => Calls.Add("previous");
    }

    private static ScreenDocument Doc(params string[] ids)
    {
        var doc = new ScreenDocument { Target = ScreenTarget.Navigation };
        foreach (var id in ids) doc.Pages.Add(new ScreenPage { Id = id, Name = id, Xaml = "<ScreenCanvas />" });
        return doc;
    }

    private static ScreenContext Ctx() => new(DateTime.Now, null, false);

    [Theory]
    [InlineData("NextPage", ScreenActionKind.NextPage, "", "")]
    [InlineData("previouspage", ScreenActionKind.PreviousPage, "", "")]
    [InlineData("GoTo:player", ScreenActionKind.GoTo, "player", "")]
    [InlineData("Media.PlayPause", ScreenActionKind.MediaPlayPause, "", "")]
    [InlineData("Media.Next", ScreenActionKind.MediaNext, "", "")]
    [InlineData("Media.Previous", ScreenActionKind.MediaPrevious, "", "")]
    [InlineData("Toggle:menu", ScreenActionKind.Toggle, "menu", "")]
    [InlineData("Set:speed=fast", ScreenActionKind.Set, "speed", "fast")]
    [InlineData("None", ScreenActionKind.None, "", "")]
    public void Parse_KnownActions(string text, ScreenActionKind kind, string arg, string value)
    {
        Assert.True(ScreenAction.TryParse(text, out var action));
        Assert.Equal(new ScreenAction(kind, arg, value), action);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Explode")]
    [InlineData("GoTo:")]
    [InlineData("Set:novalue")]
    public void Parse_RejectsJunk(string? text) => Assert.False(ScreenAction.TryParse(text, out _));

    [Fact]
    public void Pages_NextAndPreviousWrap()
    {
        var doc = Doc("a", "b", "c");
        doc.DefaultActions[ScreenEvent.ZoomIn] = "NextPage";
        doc.DefaultActions[ScreenEvent.ZoomOut] = "PreviousPage";
        var c = new ScreenController(doc);
        Assert.Equal("a", c.CurrentPage.Id);
        Assert.True(c.Handle(ScreenEvent.ZoomIn));
        Assert.Equal("b", c.CurrentPage.Id);
        c.Handle(ScreenEvent.ZoomIn); c.Handle(ScreenEvent.ZoomIn);
        Assert.Equal("a", c.CurrentPage.Id);
        c.Handle(ScreenEvent.ZoomOut);
        Assert.Equal("c", c.CurrentPage.Id);
    }

    [Fact]
    public void PageAction_BeatsDefault_AndGoToWorks()
    {
        var doc = Doc("a", "b");
        doc.DefaultActions[ScreenEvent.ZoomIn] = "NextPage";
        doc.Pages[0].Actions[ScreenEvent.ZoomIn] = "GoTo:b";
        var c = new ScreenController(doc);
        c.Handle(ScreenEvent.ZoomIn);
        Assert.Equal("b", c.CurrentPage.Id);
    }

    [Fact]
    public void GoToUnknownPage_IsIgnored_AndNoActionMeansUnhandled()
    {
        var doc = Doc("a");
        doc.Pages[0].Actions[ScreenEvent.ZoomIn] = "GoTo:nope";
        var c = new ScreenController(doc);
        Assert.True(c.Handle(ScreenEvent.ZoomIn));     // there was an action, it just had nowhere to go
        Assert.Equal("a", c.CurrentPage.Id);
        Assert.False(c.Handle(ScreenEvent.ZoomOut));   // no action at all
    }

    [Fact]
    public void SinglePage_NextPageStaysPut()
    {
        var doc = Doc("only");
        doc.DefaultActions[ScreenEvent.ZoomIn] = "NextPage";
        var c = new ScreenController(doc);
        c.Handle(ScreenEvent.ZoomIn);
        Assert.Equal("only", c.CurrentPage.Id);
    }

    [Fact]
    public void NoneAction_ConsumesTheEventAndDoesNothing()
    {
        var doc = Doc("a", "b");
        doc.DefaultActions[ScreenEvent.ZoomIn] = "NextPage";
        doc.Pages[0].Actions[ScreenEvent.ZoomIn] = "None";
        var c = new ScreenController(doc);
        Assert.True(c.Handle(ScreenEvent.ZoomIn));
        Assert.Equal("a", c.CurrentPage.Id);
    }

    [Fact]
    public void MediaActions_CallThrough()
    {
        var doc = Doc("a");
        doc.DefaultActions[ScreenEvent.ZoomIn] = "Media.Next";
        doc.DefaultActions[ScreenEvent.ZoomOut] = "Media.PlayPause";
        var media = new FakeMedia();
        var c = new ScreenController(doc, media);
        c.Handle(ScreenEvent.ZoomIn);
        c.Handle(ScreenEvent.ZoomOut);
        Assert.Equal(new[] { "next", "playpause" }, media.Calls);
    }

    [Fact]
    public void ToggleAndSet_AppearInTheSnapshot()
    {
        var doc = Doc("a");
        doc.DefaultActions[ScreenEvent.ZoomIn] = "Toggle:menu";
        doc.DefaultActions[ScreenEvent.ZoomOut] = "Set:mode=fast";
        var c = new ScreenController(doc);
        c.Handle(ScreenEvent.ZoomIn);
        c.Handle(ScreenEvent.ZoomOut);
        var snap = c.Snapshot(Ctx());
        Assert.True(snap.Flags["menu"]);
        Assert.Equal("fast", snap.Vars["mode"]);
        c.Handle(ScreenEvent.ZoomIn);
        Assert.False(c.Snapshot(Ctx()).Flags["menu"]);
    }

    [Fact]
    public void Changed_FiresOnPageChange_AndSetDocumentKeepsPageById()
    {
        var c = new ScreenController(Doc("a", "b"));
        var changes = 0;
        c.Changed += () => changes++;
        c.GoTo("b");
        Assert.Equal(1, changes);
        c.GoTo("b");
        Assert.Equal(1, changes);                     // not a change

        c.SetDocument(Doc("x", "b", "c"));
        Assert.Equal("b", c.CurrentPage.Id);          // same id still exists
        c.SetDocument(Doc("x", "y"));
        Assert.Equal("x", c.CurrentPage.Id);          // gone: back to the first
    }

    [Fact]
    public void Snapshot_CarriesPagePosition()
    {
        var c = new ScreenController(Doc("a", "b", "c"));
        c.GoTo("c");
        var snap = c.Snapshot(Ctx());
        Assert.Equal(("c", 2, 3), (snap.PageId, snap.PageIndex, snap.PageCount));
    }
}
