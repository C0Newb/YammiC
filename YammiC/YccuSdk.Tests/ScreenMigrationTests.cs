using System.Xml.Linq;
using CNCO.YccuSdk.Platform;
using CNCO.YccuSdk.Screens;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class ScreenMigrationTests
{
    private sealed class MemoryStore : IYmcKeyValueStore
    {
        public Dictionary<string, string> Values { get; } = new();
        public string? GetString(string key) => Values.TryGetValue(key, out var v) ? v : null;
        public void SetString(string key, string value) => Values[key] = value;
    }

    private static XElement Root(ScreenDocument doc) => XElement.Parse(doc.Pages[0].Xaml);

    [Fact]
    public void DefaultNavigation_MigratesToAValidDocument()
    {
        var doc = ScreenLayoutMigration.ToDocument(ScreenDefaults.Navigation());
        Assert.Equal(ScreenTarget.Navigation, doc.Target);
        Assert.Single(doc.Pages);
        Assert.Equal("main", doc.Pages[0].Id);
        Assert.Empty(ScreenXamlValidator.Validate(doc.Pages[0].Xaml));
        Assert.Equal(6, Root(doc).Elements().Count());
        var reparsed = ScreenDocument.Parse(doc.ToAxaml());
        Assert.Empty(ScreenXamlValidator.Validate(reparsed.Pages[0].Xaml));
        Assert.Equal(6, XElement.Parse(reparsed.Pages[0].Xaml).Elements().Count());
    }

    [Fact]
    public void Clock_KeepsTemplateColoursAndNightVariants()
    {
        var root = Root(ScreenLayoutMigration.ToDocument(ScreenDefaults.Navigation()));
        Assert.Equal("#F4F4F2", (string?)root.Attribute("Background"));
        Assert.Equal("#000000", (string?)root.Attribute("NightBackground"));

        var clock = root.Elements().First();
        Assert.Equal("Clock", (string?)clock.Attribute("Tag"));
        var text = clock.Elements().Single();
        Assert.Equal("{}{time}", (string?)text.Attribute("Template"));
        Assert.Equal("#111111", (string?)text.Attribute("Foreground"));
        Assert.Equal("#FFFFFF", (string?)text.Attribute("NightForeground"));
        Assert.Equal("Bold", (string?)text.Attribute("FontWeight"));
        Assert.Equal("Center", (string?)text.Attribute("TextAlignment"));
    }

    [Fact]
    public void ShowWhen_BecomesAnIsVisibleBinding()
    {
        var root = Root(ScreenLayoutMigration.ToDocument(ScreenDefaults.Navigation()));
        var song = root.Elements().First(e => (string?)e.Attribute("Tag") == "Song");
        Assert.Equal("{Binding HasMusic}", (string?)song.Attribute("IsVisible"));
        var none = root.Elements().First(e => (string?)e.Attribute("Tag") == "No music");
        Assert.Equal("{Binding NoMusic}", (string?)none.Attribute("IsVisible"));
        var clock = root.Elements().First();
        Assert.Null(clock.Attribute("IsVisible"));
    }

    [Fact]
    public void Divider_IsABoxWithoutText()
    {
        var divider = Root(ScreenLayoutMigration.ToDocument(ScreenDefaults.Navigation())).Elements().First(e => (string?)e.Attribute("Tag") == "Divider");
        Assert.Empty(divider.Elements());
        Assert.Equal("#C8C8C4", (string?)divider.Attribute("Background"));
        Assert.Equal("#3C4043", (string?)divider.Attribute("NightBackground"));
    }

    [Fact]
    public void Positions_UseTheInvariantCulture()
    {
        var layout = new ScreenLayout { Target = ScreenTarget.VirtualPitBoard };
        layout.Elements.Add(new ScreenElement { Name = "A", Kind = ScreenElementKind.Text, Text = "x", X = 1.5f, Y = 163.25f, Width = 100.5f, Height = 20 });
        var box = Root(ScreenLayoutMigration.ToDocument(layout)).Elements().Single();
        Assert.Equal("1.5", (string?)box.Attribute("Canvas.Left"));
        Assert.Equal("163.25", (string?)box.Attribute("Canvas.Top"));
    }

    [Fact]
    public void Image_BecomesScreenImageWithStretch()
    {
        var layout = new ScreenLayout { Target = ScreenTarget.Navigation };
        layout.Elements.Add(new ScreenElement { Kind = ScreenElementKind.Image, ImageData = "QUJD", Fit = ScreenImageFit.Cover });
        var image = Root(ScreenLayoutMigration.ToDocument(layout)).Elements().Single().Elements().Single();
        Assert.Equal("ScreenImage", image.Name.LocalName);
        Assert.Equal("QUJD", (string?)image.Attribute("Data"));
        Assert.Equal("UniformToFill", (string?)image.Attribute("Stretch"));
    }

    [Fact]
    public void LiteralBraces_AndFormats_SurviveTheEscape()
    {
        var layout = new ScreenLayout { Target = ScreenTarget.Navigation };
        layout.Elements.Add(new ScreenElement { Kind = ScreenElementKind.Text, Text = "{{x}} {time:HH:mm:ss} {rpm}%" });
        var text = Root(ScreenLayoutMigration.ToDocument(layout)).Elements().Single().Elements().Single();
        Assert.Equal("{}{{x}} {time:HH:mm:ss} {rpm}%", (string?)text.Attribute("Template"));
        Assert.True(ScreenLayoutMigration.ToDocument(layout).Pages[0].UsesSeconds);
        Assert.True(ScreenLayoutMigration.ToDocument(layout).Pages[0].UsesCan);
    }

    [Fact]
    public void DuplicateAndOddNames_StillValid()
    {
        var layout = new ScreenLayout { Target = ScreenTarget.Navigation };
        layout.Elements.Add(new ScreenElement { Name = "A \"quoted\" <name>", Kind = ScreenElementKind.Text, Text = "x" });
        layout.Elements.Add(new ScreenElement { Name = "A \"quoted\" <name>", Kind = ScreenElementKind.Text, Text = "y" });
        var doc = ScreenLayoutMigration.ToDocument(layout);
        Assert.Empty(ScreenXamlValidator.Validate(doc.Pages[0].Xaml));
        var reparsed = XElement.Parse(ScreenDocument.Parse(doc.ToAxaml()).Pages[0].Xaml);
        Assert.Equal(2, reparsed.Elements().Count());
        Assert.All(reparsed.Elements(), e => Assert.Equal("A \"quoted\" <name>", (string?)e.Attribute("Tag")));
    }

    [Fact]
    public void Store_FallsBackToDefaultThenRoundTripsThenResets()
    {
        var memory = new MemoryStore();
        var store = new ScreenDocumentStore(memory);

        var first = store.Load(ScreenTarget.Navigation);
        Assert.Equal("main", first.Pages[0].Id);

        first.Pages.Add(new ScreenPage { Id = "extra", Name = "Extra", Xaml = first.Pages[0].Xaml });
        store.Save(ScreenTarget.Navigation, first);
        Assert.Equal(new[] { "main", "extra" }, store.Load(ScreenTarget.Navigation).Pages.Select(p => p.Id));
        Assert.Single(store.Load(ScreenTarget.VirtualPitBoard).Pages);

        store.Reset(ScreenTarget.Navigation);
        Assert.Single(store.Load(ScreenTarget.Navigation).Pages);
    }

    [Fact]
    public void Store_MigratesALegacyJsonLayout_AndIgnoresCorruptSavedText()
    {
        var memory = new MemoryStore();
        var legacy = new ScreenLayout { Name = "Mine", Target = ScreenTarget.Navigation, Background = "#123456" };
        legacy.Elements.Add(new ScreenElement { Name = "Hello", Kind = ScreenElementKind.Text, Text = "hi" });
        memory.SetString("screen_navigation", ScreenLayoutStore.ToJson(legacy));

        var store = new ScreenDocumentStore(memory);
        var migrated = store.Load(ScreenTarget.Navigation);
        Assert.Equal("Mine", migrated.Pages[0].Name);
        Assert.Equal("#123456", (string?)Root(migrated).Attribute("Background"));

        memory.SetString("screen_doc_navigation", "<<not xml");
        Assert.Equal("Mine", store.Load(ScreenTarget.Navigation).Pages[0].Name); // corrupt document ignored, legacy used
    }
}
