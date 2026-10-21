using System.Globalization;
using System.Xml.Linq;

namespace CNCO.YccuSdk.Screens;

/// <summary>Turns an old <see cref="ScreenLayout"/> (the Skia screen builder's format) into a one-page <see cref="ScreenDocument"/>.</summary>
public static class ScreenLayoutMigration
{
    private static readonly XNamespace W = ScreenDocument.WidgetNs;

    public static ScreenDocument ToDocument(ScreenLayout layout)
    {
        var (width, height) = ScreenSizes.DesignSize(layout.Target);
        var root = new XElement(W + "ScreenCanvas",
            new XAttribute("xmlns", ScreenDocument.AvaloniaNs),
            new XAttribute(XNamespace.Xmlns + "x", ScreenDocument.XamlNs),
            new XAttribute(XNamespace.Xmlns + "y", ScreenDocument.WidgetNs),
            new XAttribute("Width", Num(width)),
            new XAttribute("Height", Num(height)),
            new XAttribute("Background", layout.Background));
        if (!string.IsNullOrWhiteSpace(layout.NightBackground)) root.SetAttributeValue("NightBackground", layout.NightBackground);

        foreach (var element in layout.Elements) root.Add(ConvertElement(element));

        var document = new ScreenDocument { Target = layout.Target };
        document.Pages.Add(new ScreenPage { Id = "main", Name = string.IsNullOrWhiteSpace(layout.Name) ? "Main" : layout.Name, Xaml = root.ToString() });
        return document;
    }

    private static XElement ConvertElement(ScreenElement e)
    {
        var box = new XElement(W + "ScreenBox",
            new XAttribute("Canvas.Left", Num(e.X)),
            new XAttribute("Canvas.Top", Num(e.Y)),
            new XAttribute("Width", Num(e.Width)),
            new XAttribute("Height", Num(e.Height)));
        if (!string.IsNullOrWhiteSpace(e.Name)) box.SetAttributeValue("Tag", e.Name);
        if (!string.IsNullOrWhiteSpace(e.Background)) box.SetAttributeValue("Background", e.Background);
        if (!string.IsNullOrWhiteSpace(e.NightBackground)) box.SetAttributeValue("NightBackground", e.NightBackground);
        var visible = VisibleBinding(e.ShowWhen);
        if (visible != null) box.SetAttributeValue("IsVisible", visible);

        if (e.Kind == ScreenElementKind.Image)
        {
            box.Add(new XElement(W + "ScreenImage",
                new XAttribute("Data", e.ImageData ?? ""),
                new XAttribute("Stretch", e.Fit switch { ScreenImageFit.Cover => "UniformToFill", ScreenImageFit.Stretch => "Fill", _ => "Uniform" })));
        }
        else if (!string.IsNullOrEmpty(e.Text))
        {
            var text = new XElement(W + "VarText",
                new XAttribute("Template", e.Text.StartsWith('{') ? "{}" + e.Text : e.Text),
                new XAttribute("FontSize", Num(e.FontSize)),
                new XAttribute("FontWeight", e.Bold ? "Bold" : "Normal"),
                new XAttribute("Foreground", e.Color),
                new XAttribute("TextAlignment", e.Align.ToString()),
                new XAttribute("VerticalAlignment", "Center"),
                new XAttribute("TextTrimming", "CharacterEllipsis"));
            if (!string.IsNullOrWhiteSpace(e.NightColor)) text.SetAttributeValue("NightForeground", e.NightColor);
            box.Add(text);
        }
        return box;
    }

    private static string? VisibleBinding(ScreenShowWhen when) => when switch
    {
        ScreenShowWhen.MusicPresent => "{Binding HasMusic}",
        ScreenShowWhen.MusicPlaying => "{Binding IsPlaying}",
        ScreenShowWhen.MusicPaused => "{Binding IsPaused}",
        ScreenShowWhen.NoMusic => "{Binding NoMusic}",
        ScreenShowWhen.Day => "{Binding Day}",
        ScreenShowWhen.Night => "{Binding Night}",
        _ => null,
    };

    private static string Num(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
