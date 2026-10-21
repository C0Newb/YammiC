using System.Globalization;
using System.Xml.Linq;

namespace CNCO.YccuSdk.Screens;

public enum EditKind
{
    Text,
    Image,
    Box,
    Progress,
    Other,
}

/// <summary>Where an Image element's picture comes from.</summary>
public enum ImageOrigin
{
    /// <summary>A picture kept inside the page.</summary>
    Embedded,

    /// <summary>The current track's album art.</summary>
    AlbumArt,
}

/// <summary>
/// One direct child of a page's root canvas, as the visual editor sees it. Property setters write straight into the XML
/// (<see cref="PageEditModel.ToXaml"/> returns the result). <c>ScreenBox</c> children are Text (holding a VarText), Image
/// (holding a ScreenImage) or Box (empty); <c>MediaProgress</c> is Progress; anything else is Other (position only).
/// </summary>
public sealed class EditElement
{
    internal EditElement(XElement node)
    {
        Node = node;
    }

    internal XElement Node { get; }

    private XElement? Inner => Node.Elements().FirstOrDefault();

    public EditKind Kind => Node.Name.LocalName switch
    {
        "ScreenBox" => Inner?.Name.LocalName switch { "VarText" => EditKind.Text, "ScreenImage" => EditKind.Image, _ => EditKind.Box },
        "MediaProgress" => EditKind.Progress,
        _ => EditKind.Other,
    };

    public string Name
    {
        get => (string?)Node.Attribute("Tag") ?? "";
        set => Set(Node, "Tag", string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public float X { get => Get("Canvas.Left"); internal set => Set(Node, "Canvas.Left", Num(value)); }
    public float Y { get => Get("Canvas.Top"); internal set => Set(Node, "Canvas.Top", Num(value)); }
    public float Width { get => Get("Width", 100); internal set => Set(Node, "Width", Num(value)); }
    public float Height { get => Get("Height", 40); internal set => Set(Node, "Height", Num(value)); }

    public string Template
    {
        get
        {
            var raw = (string?)Inner?.Attribute("Template") ?? "";
            return raw.StartsWith("{}", StringComparison.Ordinal) ? raw[2..] : raw;
        }
        set
        {
            if (Inner is { } inner) Set(inner, "Template", value.StartsWith('{') ? "{}" + value : value);
        }
    }

    public float FontSize
    {
        get => float.TryParse((string?)Inner?.Attribute("FontSize"), CultureInfo.InvariantCulture, out var v) ? v : 32;
        set => Set(Inner, "FontSize", Num(Math.Max(value, 1)));
    }

    public bool Bold
    {
        get => (string?)Inner?.Attribute("FontWeight") == "Bold";
        set => Set(Inner, "FontWeight", value ? "Bold" : "Normal");
    }

    public ScreenTextAlign Align
    {
        get => Enum.TryParse<ScreenTextAlign>((string?)Inner?.Attribute("TextAlignment"), out var v) ? v : ScreenTextAlign.Left;
        set => Set(Inner, "TextAlignment", value.ToString());
    }

    public string Color
    {
        get => (string?)Inner?.Attribute("Foreground") ?? (string?)Node.Attribute("Fill") ?? "#FFFFFF";
        set
        {
            if (Kind == EditKind.Progress) Set(Node, "Fill", value);
            else Set(Inner, "Foreground", string.IsNullOrWhiteSpace(value) ? "#FFFFFF" : value.Trim());
        }
    }

    public string? NightColor
    {
        get => Kind == EditKind.Progress ? (string?)Node.Attribute("NightFill") : (string?)Inner?.Attribute("NightForeground");
        set
        {
            var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (Kind == EditKind.Progress) Set(Node, "NightFill", text);
            else Set(Inner, "NightForeground", text);
        }
    }

    public string? Background
    {
        get => (string?)Node.Attribute(Kind == EditKind.Progress ? "Track" : "Background");
        set => Set(Node, Kind == EditKind.Progress ? "Track" : "Background", string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public string? NightBackground
    {
        get => (string?)Node.Attribute(Kind == EditKind.Progress ? "NightTrack" : "NightBackground");
        set => Set(Node, Kind == EditKind.Progress ? "NightTrack" : "NightBackground", string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    /// <summary>The picture of an Image element as base64; null for anything else.</summary>
    public string? ImageData
    {
        get => Kind == EditKind.Image ? (string?)Inner?.Attribute("Data") : null;
        set
        {
            if (Kind == EditKind.Image) Set(Inner, "Data", value ?? "");
        }
    }

    public ImageOrigin ImageOrigin
    {
        get => Kind == EditKind.Image && (string?)Inner?.Attribute("Origin") == "AlbumArt" ? ImageOrigin.AlbumArt : ImageOrigin.Embedded;
        set
        {
            if (Kind == EditKind.Image) Set(Inner, "Origin", value == ImageOrigin.AlbumArt ? "AlbumArt" : null);
        }
    }

    public string Stretch
    {
        get => (string?)Inner?.Attribute("Stretch") ?? "Uniform";
        set => Set(Inner, "Stretch", value);
    }

    public ScreenShowWhen ShowWhen
    {
        get => ((string?)Node.Attribute("IsVisible")) switch
        {
            "{Binding HasMusic}" => ScreenShowWhen.MusicPresent,
            "{Binding IsPlaying}" => ScreenShowWhen.MusicPlaying,
            "{Binding IsPaused}" => ScreenShowWhen.MusicPaused,
            "{Binding NoMusic}" => ScreenShowWhen.NoMusic,
            "{Binding Day}" => ScreenShowWhen.Day,
            "{Binding Night}" => ScreenShowWhen.Night,
            _ => ScreenShowWhen.Always,
        };
        set => Set(Node, "IsVisible", value switch
        {
            ScreenShowWhen.MusicPresent => "{Binding HasMusic}",
            ScreenShowWhen.MusicPlaying => "{Binding IsPlaying}",
            ScreenShowWhen.MusicPaused => "{Binding IsPaused}",
            ScreenShowWhen.NoMusic => "{Binding NoMusic}",
            ScreenShowWhen.Day => "{Binding Day}",
            ScreenShowWhen.Night => "{Binding Night}",
            _ => null,
        });
    }

    private float Get(string attribute, float fallback = 0) =>
        float.TryParse((string?)Node.Attribute(attribute), CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static void Set(XElement? element, string attribute, string? value) => element?.SetAttributeValue(attribute, value);

    private static string Num(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>Edits one page's XAML as a list of elements. Positions are in the target's design space and are kept inside <see cref="ScreenSizes.Bounds"/>.</summary>
public sealed class PageEditModel
{
    private static readonly XNamespace W = ScreenDocument.WidgetNs;

    private readonly ScreenTarget _target;
    private readonly XElement _root;
    private readonly List<EditElement> _elements = new();

    /// <exception cref="System.Xml.XmlException">The XAML isn't well-formed.</exception>
    public PageEditModel(ScreenTarget target, string pageXaml)
    {
        _target = target;
        _root = XElement.Parse(pageXaml);
        foreach (var child in _root.Elements()) _elements.Add(new EditElement(child));
    }

    public IReadOnlyList<EditElement> Elements => _elements;

    public string RootBackground
    {
        get => (string?)_root.Attribute("Background") ?? "#000000";
        set => _root.SetAttributeValue("Background", string.IsNullOrWhiteSpace(value) ? "#000000" : value.Trim());
    }

    public string RootNightBackground
    {
        get => (string?)_root.Attribute("NightBackground") ?? "";
        set => _root.SetAttributeValue("NightBackground", string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public string ToXaml() => _root.ToString();

    public EditElement AddText()
    {
        var area = ScreenSizes.Bounds(_target);
        var height = Math.Min(48f, area.Height);
        var box = NewBox("Text", area.X + area.Width * 0.1f, area.Y + (area.Height - height) / 2f, area.Width * 0.8f, height);
        box.Add(new XElement(W + "VarText",
            new XAttribute("Template", "{}{time}"), new XAttribute("FontSize", "32"), new XAttribute("FontWeight", "Normal"),
            new XAttribute("Foreground", "#FFFFFF"), new XAttribute("TextAlignment", "Center"),
            new XAttribute("VerticalAlignment", "Center"), new XAttribute("TextTrimming", "CharacterEllipsis")));
        return Add(box);
    }

    public EditElement AddBox()
    {
        var area = ScreenSizes.Bounds(_target);
        var box = NewBox("Box", area.X + area.Width * 0.25f, area.Y + area.Height * 0.25f, area.Width * 0.5f, Math.Min(20f, area.Height));
        box.SetAttributeValue("Background", "#808080");
        return Add(box);
    }

    public EditElement AddProgress()
    {
        var area = ScreenSizes.Bounds(_target);
        var height = Math.Min(12f, area.Height);
        var bar = new XElement(W + "MediaProgress", new XAttribute("Tag", "Progress"),
            new XAttribute("Canvas.Left", Num(area.X + area.Width * 0.1f)), new XAttribute("Canvas.Top", Num(area.Y + area.Height / 2f)),
            new XAttribute("Width", Num(area.Width * 0.8f)), new XAttribute("Height", Num(height)),
            new XAttribute("Value", "{Binding Progress}"), new XAttribute("Fill", "#FFFFFF"), new XAttribute("Track", "#555555"));
        return Add(bar);
    }

    public EditElement AddImage(string base64)
    {
        var area = ScreenSizes.Bounds(_target);
        var box = NewBox("Image", area.X + area.Width * 0.25f, area.Y + area.Height * 0.2f, area.Width * 0.5f, area.Height * 0.6f);
        box.Add(new XElement(W + "ScreenImage", new XAttribute("Data", base64), new XAttribute("Stretch", "Uniform")));
        return Add(box);
    }

    /// <summary>Adds a picture that shows the current track's album art (nothing while the track has none).</summary>
    public EditElement AddAlbumArt()
    {
        var area = ScreenSizes.Bounds(_target);
        var side = Math.Min(area.Height * 0.8f, area.Width * 0.4f);
        var box = NewBox("Album art", area.X + area.Width * 0.55f, area.Y + (area.Height - side) / 2f, side, side);
        box.Add(new XElement(W + "ScreenImage", new XAttribute("Origin", "AlbumArt"), new XAttribute("Stretch", "UniformToFill")));
        return Add(box);
    }

    public EditElement Duplicate(EditElement element)
    {
        var copy = new XElement(element.Node);
        var added = Add(copy, after: element);
        added.Name = element.Name + " copy";
        SetBounds(added, added.X + 10, added.Y + 10, added.Width, added.Height);
        return added;
    }

    public void Delete(EditElement element)
    {
        element.Node.Remove();
        _elements.Remove(element);
    }

    /// <summary>Moves an element in the drawing order (positive = towards the front), stopping at either end.</summary>
    public void Move(EditElement element, int delta)
    {
        var index = _elements.IndexOf(element);
        if (index < 0) return;
        var target = Math.Clamp(index + delta, 0, _elements.Count - 1);
        if (target == index) return;

        _elements.RemoveAt(index);
        _elements.Insert(target, element);
        element.Node.Remove();
        if (target == 0) _root.AddFirst(element.Node);
        else _elements[target - 1].Node.AddAfterSelf(element.Node);
    }

    /// <summary>Sets position and size, moved and shrunk as needed to lie inside the area the bike shows.</summary>
    public void SetBounds(EditElement element, float x, float y, float width, float height)
    {
        var b = ScreenSizes.Bounds(_target);
        width = Math.Clamp(width, 1, b.Width);
        height = Math.Clamp(height, 1, b.Height);
        element.X = Math.Clamp(x, b.X, b.X + b.Width - width);
        element.Y = Math.Clamp(y, b.Y, b.Y + b.Height - height);
        element.Width = width;
        element.Height = height;
    }

    private XElement NewBox(string name, float x, float y, float width, float height) =>
        new(W + "ScreenBox", new XAttribute("Tag", name),
            new XAttribute("Canvas.Left", Num(x)), new XAttribute("Canvas.Top", Num(y)),
            new XAttribute("Width", Num(width)), new XAttribute("Height", Num(height)));

    private EditElement Add(XElement node, EditElement? after = null)
    {
        var element = new EditElement(node);
        if (after == null)
        {
            _root.Add(node);
            _elements.Add(element);
        }
        else
        {
            after.Node.AddAfterSelf(node);
            _elements.Insert(_elements.IndexOf(after) + 1, element);
        }
        return element;
    }

    private static string Num(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
