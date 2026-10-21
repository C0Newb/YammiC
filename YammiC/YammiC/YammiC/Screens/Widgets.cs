using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CNCO.YccuSdk.Screens;

namespace CNCO.YammiC.Screens;

/// <summary>
/// Remembers a control's day brush and swaps in a night brush when the state says it's night.
/// </summary>
internal sealed class NightPair
{
    private readonly AvaloniaObject _owner;
    private readonly StyledProperty<IBrush?> _property;
    private IBrush? _day;
    private bool _applying;

    public NightPair(AvaloniaObject owner, StyledProperty<IBrush?> property)
    {
        _owner = owner;
        _property = property;
    }

    public IBrush? Night { get; set; }

    /// <summary>
    /// Call when the brush property changes: anything we didn't set ourselves is the new day brush.
    /// </summary>
    public void Capture()
    {
        if (!_applying) _day = _owner.GetValue(_property);
    }

    public void Apply(bool night)
    {
        _applying = true;
        try
        {
            _owner.SetValue(_property, night && Night != null ? Night : _day);
        }
        finally
        {
            _applying = false;
        }
    }
}

/// <summary>
/// Text with <c>{variables}</c> (see <see cref="ScreenVariables"/>): <c>&lt;y:VarText Template="{}{artist} - {song}"/&gt;</c>.
/// </summary>
public class VarText : TextBlock
{
    public static readonly StyledProperty<string?> TemplateProperty = AvaloniaProperty.Register<VarText, string?>(nameof(Template));
    public static readonly StyledProperty<IBrush?> NightForegroundProperty = AvaloniaProperty.Register<VarText, IBrush?>(nameof(NightForeground));
    public static readonly StyledProperty<ScreenTextOverflow> OverflowProperty = AvaloniaProperty.Register<VarText, ScreenTextOverflow>(nameof(Overflow));

    private readonly NightPair _foreground;
    private DateTime _scrollSince = DateTime.Now;
    private ScreenState? _state;

    static VarText()
    {
        TemplateProperty.Changed.AddClassHandler<VarText>((c, _) => c.Refresh());
        ForegroundProperty.Changed.AddClassHandler<VarText>((c, _) => c._foreground.Capture());
        NightForegroundProperty.Changed.AddClassHandler<VarText>((c, e) =>
        {
            c._foreground.Night = e.NewValue as IBrush;
            c.Refresh();
        });
        OverflowProperty.Changed.AddClassHandler<VarText>((c, _) => c.ApplyOverflow());
    }

    public VarText()
    {
        _foreground = new NightPair(this, ForegroundProperty);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public string? Template
    {
        get => GetValue(TemplateProperty);
        set => SetValue(TemplateProperty, value);
    }

    public IBrush? NightForeground
    {
        get => GetValue(NightForegroundProperty);
        set => SetValue(NightForegroundProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_state != null) _state.PropertyChanged -= OnStateChanged;
        _state = DataContext as ScreenState;
        if (_state != null) _state.PropertyChanged += OnStateChanged;
        Refresh();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    /// <summary>
    /// Trim keeps whatever TextWrapping/TextTrimming say; Wrap and Scroll set them (and win over them).
    /// </summary>
    public ScreenTextOverflow Overflow
    {
        get => GetValue(OverflowProperty);
        set => SetValue(OverflowProperty, value);
    }

    private void ApplyOverflow()
    {
        if (Overflow == ScreenTextOverflow.Wrap)
        {
            TextWrapping = TextWrapping.Wrap;
            if (TextTrimming == null || TextTrimming == TextTrimming.None) TextTrimming = TextTrimming.CharacterEllipsis;
        }
        else if (Overflow == ScreenTextOverflow.Scroll)
        {
            TextWrapping = TextWrapping.NoWrap;
            TextTrimming = TextTrimming.None;
        }
        _scrollSince = Now();
        InvalidateVisual();
    }

    private DateTime Now() => _state?.Now ?? DateTime.Now;

    private void Refresh()
    {
        var text = _state == null ? Template : _state.Resolve(Template);
        if (text != Text)
        {
            Text = text;
            _scrollSince = Now(); // new words start from the beginning
        }
        _foreground.Apply(_state?.Night == true);
        UpdateScroll();
    }

    // Slides the text left by the marquee's offset. The box around it (ScreenBox) clips what slides out of view.
    private void UpdateScroll()
    {
        if (Overflow != ScreenTextOverflow.Scroll || string.IsNullOrEmpty(Text) || Bounds.Width <= 0)
        {
            RenderTransform = null;
            return;
        }

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var measured = new FormattedText(Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, FontSize, Foreground);
        var overflow = measured.Width - (Bounds.Width - Padding.Left - Padding.Right);
        var offset = ScreenMarquee.Offset(overflow, Now() - _scrollSince);
        RenderTransform = offset > 0 ? new TranslateTransform(-offset, 0) : null;
    }
}

/// <summary>
/// A filled rectangle (optionally with a night fill) that holds one child; used for every text/image element.
/// </summary>
public class ScreenBox : Border
{
    public static readonly StyledProperty<IBrush?> NightBackgroundProperty = AvaloniaProperty.Register<ScreenBox, IBrush?>(nameof(NightBackground));

    private readonly NightPair _background;
    private ScreenState? _state;

    static ScreenBox()
    {
        BackgroundProperty.Changed.AddClassHandler<ScreenBox>((c, _) => c._background.Capture());
        NightBackgroundProperty.Changed.AddClassHandler<ScreenBox>((c, e) =>
        {
            c._background.Night = e.NewValue as IBrush;
            c.Apply();
        });
    }

    public ScreenBox()
    {
        _background = new NightPair(this, BackgroundProperty);
        ClipToBounds = true; // scrolling text slides out of its box, not over the rest of the screen
    }

    protected override Type StyleKeyOverride => typeof(Border);

    public IBrush? NightBackground
    {
        get => GetValue(NightBackgroundProperty);
        set => SetValue(NightBackgroundProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_state != null) _state.PropertyChanged -= OnStateChanged;
        _state = DataContext as ScreenState;
        if (_state != null) _state.PropertyChanged += OnStateChanged;
        Apply();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    private void Apply() => _background.Apply(_state?.Night == true);
}

/// <summary>
/// The root of a page: a Canvas with a day and a night background.
/// </summary>
public class ScreenCanvas : Canvas
{
    public static readonly StyledProperty<IBrush?> NightBackgroundProperty = AvaloniaProperty.Register<ScreenCanvas, IBrush?>(nameof(NightBackground));

    private readonly NightPair _background;
    private ScreenState? _state;

    static ScreenCanvas()
    {
        BackgroundProperty.Changed.AddClassHandler<ScreenCanvas>((c, _) => c._background.Capture());
        NightBackgroundProperty.Changed.AddClassHandler<ScreenCanvas>((c, e) =>
        {
            c._background.Night = e.NewValue as IBrush;
            c.Apply();
        });
    }

    public ScreenCanvas()
    {
        _background = new NightPair(this, BackgroundProperty);
    }

    protected override Type StyleKeyOverride => typeof(Canvas);

    public IBrush? NightBackground
    {
        get => GetValue(NightBackgroundProperty);
        set => SetValue(NightBackgroundProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_state != null) _state.PropertyChanged -= OnStateChanged;
        _state = DataContext as ScreenState;
        if (_state != null) _state.PropertyChanged += OnStateChanged;
        Apply();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    private void Apply() => _background.Apply(_state?.Night == true);
}

/// <summary>
/// Where a <see cref="ScreenImage"/> gets its picture.
/// </summary>
public enum ScreenImageOrigin
{
    /// <summary>
    /// The base64 picture kept in <see cref="ScreenImage.Data"/>.
    /// </summary>
    Embedded,

    /// <summary>
    /// The current track's album art (nothing is drawn while the track has none).
    /// </summary>
    AlbumArt,
}

/// <summary>
/// A picture: either one kept inside the page as base64 (so a screen is one self-contained file) or, with
/// <c>Origin="AlbumArt"</c>, the current track's album art.
/// </summary>
public class ScreenImage : Image
{
    public static readonly StyledProperty<string?> DataProperty = AvaloniaProperty.Register<ScreenImage, string?>(nameof(Data));
    public static readonly StyledProperty<ScreenImageOrigin> OriginProperty = AvaloniaProperty.Register<ScreenImage, ScreenImageOrigin>(nameof(Origin));

    private ScreenState? _state;
    private byte[]? _shownArt;

    static ScreenImage()
    {
        DataProperty.Changed.AddClassHandler<ScreenImage>((c, _) => c.Refresh(force: true));
        OriginProperty.Changed.AddClassHandler<ScreenImage>((c, _) => c.Refresh(force: true));
    }

    protected override Type StyleKeyOverride => typeof(Image);

    public string? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public ScreenImageOrigin Origin
    {
        get => GetValue(OriginProperty);
        set => SetValue(OriginProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_state != null) _state.PropertyChanged -= OnStateChanged;
        _state = DataContext as ScreenState;
        if (_state != null) _state.PropertyChanged += OnStateChanged;
        Refresh(force: true);
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Refresh(force: false);

    private void Refresh(bool force)
    {
        if (Origin == ScreenImageOrigin.AlbumArt)
        {
            // The state raises "everything changed" on each update; only decode when the track's picture really is a new one.
            var art = _state?.AlbumArt;
            if (!force && ReferenceEquals(art, _shownArt)) return;
            _shownArt = art;
            SetPicture(art != null && art.Length > 0 ? art : null);
        }
        else
        {
            _shownArt = null;
            if (!force) return;
            SetPicture(string.IsNullOrWhiteSpace(Data) ? null : Convert.FromBase64String(Data));
        }
    }

    private void SetPicture(byte[]? encoded)
    {
        var old = Source as Bitmap;
        try
        {
            Source = encoded == null ? null : new Bitmap(new MemoryStream(encoded));
        }
        catch (Exception)
        {
            Source = null; // a broken picture shows nothing rather than failing the page
        }
        old?.Dispose();
    }
}

/// <summary>
/// A horizontal bar filled to <see cref="Value"/> (0..1); bind it to <c>{Binding Progress}</c>. Has night colours like the other widgets.
/// </summary>
public class MediaProgress : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<MediaProgress, double>(nameof(Value));
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<MediaProgress, IBrush?>(nameof(Fill), Brushes.White);
    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<MediaProgress, IBrush?>(nameof(Track), Brushes.Gray);
    public static readonly StyledProperty<IBrush?> NightFillProperty = AvaloniaProperty.Register<MediaProgress, IBrush?>(nameof(NightFill));
    public static readonly StyledProperty<IBrush?> NightTrackProperty = AvaloniaProperty.Register<MediaProgress, IBrush?>(nameof(NightTrack));
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty = AvaloniaProperty.Register<MediaProgress, CornerRadius>(nameof(CornerRadius));

    private readonly NightPair _fill;
    private readonly NightPair _track;
    private ScreenState? _state;

    static MediaProgress()
    {
        AffectsRender<MediaProgress>(ValueProperty, FillProperty, TrackProperty, CornerRadiusProperty);
        FillProperty.Changed.AddClassHandler<MediaProgress>((c, _) => c._fill.Capture());
        TrackProperty.Changed.AddClassHandler<MediaProgress>((c, _) => c._track.Capture());
        NightFillProperty.Changed.AddClassHandler<MediaProgress>((c, e) =>
        {
            c._fill.Night = e.NewValue as IBrush;
            c.Apply();
        });
        NightTrackProperty.Changed.AddClassHandler<MediaProgress>((c, e) =>
        {
            c._track.Night = e.NewValue as IBrush;
            c.Apply();
        });
    }

    public MediaProgress()
    {
        _fill = new NightPair(this, FillProperty);
        _track = new NightPair(this, TrackProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public IBrush? NightFill
    {
        get => GetValue(NightFillProperty);
        set => SetValue(NightFillProperty, value);
    }

    public IBrush? NightTrack
    {
        get => GetValue(NightTrackProperty);
        set => SetValue(NightTrackProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_state != null) _state.PropertyChanged -= OnStateChanged;
        _state = DataContext as ScreenState;
        if (_state != null) _state.PropertyChanged += OnStateChanged;
        Apply();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    private void Apply()
    {
        var night = _state?.Night == true;
        _fill.Apply(night);
        _track.Apply(night);
    }

    public override void Render(DrawingContext context)
    {
        var all = new Rect(Bounds.Size);
        var radius = CornerRadius.TopLeft;
        context.DrawRectangle(Track, null, all, radius, radius);
        var fill = new Rect(0, 0, all.Width * Math.Clamp(Value, 0, 1), all.Height);
        if (fill.Width > 0) context.DrawRectangle(Fill, null, fill, radius, radius);
    }
}
