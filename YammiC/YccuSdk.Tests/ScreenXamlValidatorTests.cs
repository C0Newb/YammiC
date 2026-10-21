using CNCO.YccuSdk.Screens;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class ScreenXamlValidatorTests
{
    private const string Head = "<y:ScreenCanvas xmlns=\"https://github.com/avaloniaui\" xmlns:y=\"clr-namespace:CNCO.YammiC.Screens;assembly=YammiC\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" ";

    private static IReadOnlyList<ScreenIssue> Check(string inner) => ScreenXamlValidator.Validate(Head + ">" + inner + "</y:ScreenCanvas>");

    [Fact]
    public void AcceptsTheWidgetSet()
    {
        Assert.Empty(Check("""
            <y:ScreenBox Canvas.Left="1" Canvas.Top="2" Width="30" Height="20" Background="#FF0000" NightBackground="#00FF00" IsVisible="{Binding HasMusic}" Tag="Clock">
              <y:VarText Template="{}{time:HH:mm}" FontSize="20" FontWeight="Bold" TextAlignment="Center" Foreground="#FFFFFF" />
            </y:ScreenBox>
            <TextBlock Text="{Binding Now, StringFormat={}{0:HH:mm}}" />
            <y:MediaProgress Value="{Binding Progress}" Fill="#FFF" Track="#333" />
            <Rectangle Fill="#123456" Width="5" Height="5" />
            <Grid RowDefinitions="*,*"><Border /></Grid>
            """));
    }

    [Theory]
    [InlineData("<Button />")]
    [InlineData("<Image Source=\"file:///x.png\" />")]
    [InlineData("<Border><Border.Background><SolidColorBrush /></Border.Background></Border>")]
    [InlineData("<TextBlock Text=\"{StaticResource Foo}\" />")]
    [InlineData("<TextBlock Text=\"{x:Static sys:Environment.MachineName}\" />")]
    [InlineData("<TextBlock Text=\"{Binding Song, Converter={StaticResource C}}\" />")]
    [InlineData("<TextBlock Text=\"{Binding Source={x:Static Foo.Bar}}\" />")]
    [InlineData("<TextBlock Text=\"{Binding Song}{Binding Artist}\" />")]
    [InlineData("<TextBlock Tooltip.Tip=\"x\" />")]
    [InlineData("<TextBlock Foo=\"1\" />")]
    public void RejectsEverythingElse(string inner)
    {
        Assert.NotEmpty(Check(inner));
    }

    [Fact]
    public void MalformedXml_ReportsALine()
    {
        var issues = ScreenXamlValidator.Validate("<y:ScreenCanvas\n  xmlns:y=\"a\">\n<oops>\n</y:ScreenCanvas>");
        Assert.Single(issues);
        Assert.True(issues[0].Line > 0);
    }

    [Fact]
    public void IssueText_NamesTheLine()
    {
        var issues = Check("\n<Button />");
        Assert.StartsWith("line ", issues[0].ToString());
    }
}

public class ScreenImageOriginTests
{
    [Fact]
    public void AlbumArtImage_IsAllowed()
    {
        const string xaml = """
            <y:ScreenCanvas xmlns="https://github.com/avaloniaui" xmlns:y="clr-namespace:CNCO.YammiC.Screens;assembly=YammiC">
              <y:ScreenBox Canvas.Left="0" Canvas.Top="0" Width="10" Height="10">
                <y:ScreenImage Origin="AlbumArt" Stretch="UniformToFill" />
              </y:ScreenBox>
            </y:ScreenCanvas>
            """;
        Assert.Empty(ScreenXamlValidator.Validate(xaml));
    }
}
