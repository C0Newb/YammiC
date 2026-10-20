namespace CNCO.YccuSdk.Screens;

/// <summary>Which of the bike's displays a layout is for.</summary>
public enum ScreenTarget
{
    /// <summary>The navigation screen (480x240; the actual display may be 480x234, see <see cref="ScreenSizes"/>).</summary>
    Navigation,

    /// <summary>The Virtual Pit Board (600x306).</summary>
    VirtualPitBoard,
}

public enum ScreenElementKind
{
    Text,
    Image,
}

/// <summary>When an element is drawn. Lets one layout hold e.g. both a "now playing" and a "nothing playing" text.</summary>
public enum ScreenShowWhen
{
    Always,
    MusicPresent,
    MusicPlaying,
    MusicPaused,
    NoMusic,
    Day,
    Night,
}

public enum ScreenTextAlign
{
    Left,
    Center,
    Right,
}

public enum ScreenImageFit
{
    /// <summary>Whole image visible, aspect ratio kept (may leave empty space).</summary>
    Contain,

    /// <summary>Box filled, aspect ratio kept (edges cropped).</summary>
    Cover,

    /// <summary>Box filled, image stretched.</summary>
    Stretch,
}

/// <summary>
/// One thing on a screen: a text box or an image, positioned in the layout's design space
/// (<see cref="ScreenSizes.DesignSize"/>). A text box with an empty <see cref="Text"/> and a
/// <see cref="Background"/> is how a plain filled rectangle (divider, panel) is made.
/// </summary>
public class ScreenElement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public ScreenElementKind Kind { get; set; }
    public ScreenShowWhen ShowWhen { get; set; }

    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 100;
    public float Height { get; set; } = 40;

    // -- Text -------------------------------------------------------------------

    /// <summary>Text with <c>{variables}</c>, see <see cref="ScreenVariables"/>.</summary>
    public string Text { get; set; } = "";

    public float FontSize { get; set; } = 32;
    public bool Bold { get; set; }
    public ScreenTextAlign Align { get; set; }

    /// <summary>Text colour as #RRGGBB or #AARRGGBB.</summary>
    public string Color { get; set; } = "#FFFFFF";

    /// <summary>Text colour at night; the day <see cref="Color"/> is used when null/empty.</summary>
    public string? NightColor { get; set; }

    /// <summary>Fill behind the box (also used for image letterboxing); none when null/empty.</summary>
    public string? Background { get; set; }

    public string? NightBackground { get; set; }

    // -- Image ------------------------------------------------------------------

    /// <summary>The image file (any common format) as base64, kept inside the layout so it can be exported as one file.</summary>
    public string? ImageData { get; set; }

    public ScreenImageFit Fit { get; set; }

    public ScreenElement Clone() => (ScreenElement)MemberwiseClone();
}

/// <summary>A whole screen: a background and a back-to-front list of elements.</summary>
public class ScreenLayout
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Custom";
    public ScreenTarget Target { get; set; }

    public string Background { get; set; } = "#000000";
    public string? NightBackground { get; set; }

    public List<ScreenElement> Elements { get; set; } = new();

    public ScreenLayout Clone() => new()
    {
        Version = Version,
        Name = Name,
        Target = Target,
        Background = Background,
        NightBackground = NightBackground,
        Elements = Elements.Select(e => e.Clone()).ToList(),
    };
}

/// <summary>Design-space and display sizes for each target.</summary>
public static class ScreenSizes
{
    /// <summary>
    /// Coordinates in a <see cref="ScreenLayout"/> are in this space; the renderer scales it to the real image size.
    /// </summary>
    public static (int Width, int Height) DesignSize(ScreenTarget target) => target switch
    {
        ScreenTarget.Navigation => (480, 240),
        _ => (600, 306),
    };

    /// <summary>
    /// The part of the VPB canvas that the CCU actually shows: a 526x66 strip at (0, 163) on the 600x306 canvas
    /// (see docs/vpb.md). Anything outside it is probably not visible. Null for the navigation screen.
    /// </summary>
    public static (float X, float Y, float Width, float Height)? VisibleArea(ScreenTarget target) =>
        target == ScreenTarget.VirtualPitBoard ? (0, 163, 526, 66) : null;

    /// <summary>
    /// The region elements may occupy and the editor shows: the visible strip on the pit board, the whole screen on the
    /// navigation display. Anything outside it would be invisible on the bike.
    /// </summary>
    public static (float X, float Y, float Width, float Height) Bounds(ScreenTarget target)
    {
        if (VisibleArea(target) is { } area) return area;
        var (width, height) = DesignSize(target);
        return (0, 0, width, height);
    }

    /// <summary>
    /// Moves and, if needed, shrinks <paramref name="element"/> so it lies inside <see cref="Bounds"/>. The size is kept when
    /// it fits; otherwise it is cut down to the bounds.
    /// </summary>
    public static void ClampToBounds(ScreenElement element, ScreenTarget target)
    {
        var b = Bounds(target);
        element.Width = Math.Clamp(element.Width, 1, b.Width);
        element.Height = Math.Clamp(element.Height, 1, b.Height);
        element.X = Math.Clamp(element.X, b.X, b.X + b.Width - element.Width);
        element.Y = Math.Clamp(element.Y, b.Y, b.Y + b.Height - element.Height);
    }
}
