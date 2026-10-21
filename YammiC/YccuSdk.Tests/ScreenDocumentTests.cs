using CNCO.YccuSdk.Screens;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class ScreenDocumentTests
{
    private const string Two = """
        <y:ScreenDocument xmlns="https://github.com/avaloniaui"
                          xmlns:y="clr-namespace:CNCO.YammiC.Screens;assembly=YammiC"
                          Target="VirtualPitBoard" ZoomIn="NextPage" ZoomOut="PreviousPage">
          <y:Page Id="main" Name="Main" ZoomIn="GoTo:second">
            <y:ScreenCanvas Background="#000000">
              <y:VarText Canvas.Left="0" Canvas.Top="163" Width="100" Height="20" Template="{}{time}" />
            </y:ScreenCanvas>
          </y:Page>
          <y:Page Id="second" Name="Second">
            <y:ScreenCanvas Background="#111111" />
          </y:Page>
        </y:ScreenDocument>
        """;

    [Fact]
    public void Parse_ReadsPagesTargetAndActions()
    {
        var doc = ScreenDocument.Parse(Two);
        Assert.Equal(ScreenTarget.VirtualPitBoard, doc.Target);
        Assert.Equal(new[] { "main", "second" }, doc.Pages.Select(p => p.Id));
        Assert.Equal("Main", doc.Pages[0].Name);
        Assert.Equal("GoTo:second", doc.Pages[0].Actions[ScreenEvent.ZoomIn]);
        Assert.Equal("NextPage", doc.DefaultActions[ScreenEvent.ZoomIn]);
        Assert.Equal("PreviousPage", doc.DefaultActions[ScreenEvent.ZoomOut]);
        Assert.False(doc.Pages[1].Actions.ContainsKey(ScreenEvent.ZoomIn));
    }

    [Fact]
    public void PageXaml_IsStandaloneAndKeepsNamespaces()
    {
        var page = ScreenDocument.Parse(Two).Pages[0];
        Assert.StartsWith("<y:ScreenCanvas", page.Xaml.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("clr-namespace:CNCO.YammiC.Screens;assembly=YammiC", page.Xaml);
        Assert.Contains("https://github.com/avaloniaui", page.Xaml);
        Assert.Empty(ScreenXamlValidator.Validate(page.Xaml));
    }

    [Fact]
    public void RoundTrip_PreservesPagesAndActions()
    {
        var first = ScreenDocument.Parse(Two);
        var again = ScreenDocument.Parse(first.ToAxaml());
        Assert.Equal(first.Target, again.Target);
        Assert.Equal(first.Pages.Select(p => p.Id), again.Pages.Select(p => p.Id));
        Assert.Equal(first.Pages[0].Actions[ScreenEvent.ZoomIn], again.Pages[0].Actions[ScreenEvent.ZoomIn]);
        Assert.Equal(first.DefaultActions[ScreenEvent.ZoomOut], again.DefaultActions[ScreenEvent.ZoomOut]);
        Assert.Contains("Template=\"{}{time}\"", again.Pages[0].Xaml);
    }

    [Fact]
    public void Parse_RejectsBadDocuments()
    {
        var e1 = Assert.Throws<ScreenDocumentException>(() => ScreenDocument.Parse("<y:ScreenDocument"));
        Assert.True(e1.Line >= 0);
        Assert.Throws<ScreenDocumentException>(() => ScreenDocument.Parse("<Canvas xmlns=\"https://github.com/avaloniaui\" />"));
        Assert.Throws<ScreenDocumentException>(() => ScreenDocument.Parse(Two.Replace("Id=\"second\"", "Id=\"main\"")));      // duplicate id
        Assert.Throws<ScreenDocumentException>(() => ScreenDocument.Parse(Two.Replace("Id=\"main\" ", "")));                  // missing id
        Assert.Throws<ScreenDocumentException>(() => ScreenDocument.Parse(
            "<y:ScreenDocument xmlns=\"https://github.com/avaloniaui\" xmlns:y=\"clr-namespace:CNCO.YammiC.Screens;assembly=YammiC\" />")); // no pages
        Assert.Throws<ScreenDocumentException>(() => ScreenDocument.Parse(Two.Replace("<y:ScreenCanvas Background=\"#111111\" />", "<y:ScreenCanvas /><y:ScreenCanvas />"))); // two roots
    }

    [Fact]
    public void UsesSeconds_OnlyWhenAFormatShowsThem()
    {
        Assert.True(new ScreenPage { Xaml = "<VarText Template=\"{}{time:HH:mm:ss}\" />" }.UsesSeconds);
        Assert.True(new ScreenPage { Xaml = "<TextBlock Text=\"{Binding Now, StringFormat={}{0:HH:mm:ss}}\" />" }.UsesSeconds);
        Assert.False(new ScreenPage { Xaml = "<MediaProgress Value=\"{Binding Progress}\" />" }.UsesSeconds); // "Progress" contains "ss"
        Assert.False(new ScreenPage { Xaml = "<VarText Template=\"{}{time}\" />" }.UsesSeconds);
    }

    [Fact]
    public void UsesCan_WhenABikeValueIsMentioned()
    {
        Assert.True(new ScreenPage { Xaml = "<TextBlock Text=\"{Binding Rpm}\" />" }.UsesCan);
        Assert.True(new ScreenPage { Xaml = "<VarText Template=\"{}{water:F}\" />" }.UsesCan);
        Assert.False(new ScreenPage { Xaml = "<VarText Template=\"{}{song}\" />" }.UsesCan);
    }
}
