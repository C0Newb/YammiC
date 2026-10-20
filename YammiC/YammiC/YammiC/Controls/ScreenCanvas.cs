using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CNCO.YccuSdk.Screens;
using SkiaSharp;

namespace YammiC.Controls;

/// <summary>
/// The drag-and-drop surface of the screen editor: shows a <see cref="ScreenLayout"/> exactly as the bike gets it (it runs
/// the SDK's own <see cref="ScreenRenderer"/>), with the selected element outlined. Drag an element to move it, drag one of
/// its corner squares to resize it. Changes are made straight on the <see cref="ScreenElement"/>s; <see cref="ElementChanged"/>
/// tells the owner to refresh its fields.
/// </summary>
public class ScreenCanvas : Control
{
    private const double HandleSize = 14;

    private enum Drag { None, Move, Resize }

    private ScreenLayout? _layout;
    private ScreenContext _context = new(DateTime.Now, null, false);
    private ScreenElement? _selected;
    private WriteableBitmap? _bitmap;
    private bool _dirty = true;

    private Drag _drag;
    private Point _dragStartDesign;
    private (float X, float Y, float W, float H) _dragStartRect;
    private int _resizeCorner;

    /// <summary>The user picked (or deselected, with null) an element.</summary>
    public event Action<ScreenElement?>? SelectionChanged;

    /// <summary>An element was moved or resized by dragging.</summary>
    public event Action<ScreenElement>? ElementChanged;

    public ScreenLayout? Layout
    {
        get => _layout;
        set
        {
            _layout = value;
            Refresh();
            InvalidateMeasure();
        }
    }

    public ScreenContext Context
    {
        get => _context;
        set
        {
            _context = value;
            Refresh();
        }
    }

    public ScreenElement? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            InvalidateVisual();
        }
    }

    /// <summary>Re-renders after the layout or context changed.</summary>
    public void Refresh()
    {
        _dirty = true;
        InvalidateVisual();
    }

    private (int Width, int Height) DesignSize => _layout == null ? (480, 240) : ScreenSizes.DesignSize(_layout.Target);

    // The part of the screen shown and editable: the visible strip on the pit board, everything on the navigation screen.
    private (float X, float Y, float Width, float Height) View => _layout == null ? (0, 0, 480, 240) : ScreenSizes.Bounds(_layout.Target);

    protected override Size MeasureOverride(Size availableSize)
    {
        // Keep the screen's aspect ratio at whatever width we are given.
        var view = View;
        var width = double.IsInfinity(availableSize.Width) ? view.Width : availableSize.Width;
        return new Size(width, Math.Max(width * view.Height / view.Width, 72));
    }

    private (double Scale, Point Origin) Fit()
    {
        // The origin is where design (0, 0) lands, so the view region ends up centred and every conversion stays linear.
        var view = View;
        var scale = Math.Min(Bounds.Width / view.Width, Bounds.Height / view.Height);
        return (scale, new Point(
            (Bounds.Width - view.Width * scale) / 2 - view.X * scale,
            (Bounds.Height - view.Height * scale) / 2 - view.Y * scale));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_layout == null) return;

        var (designWidth, designHeight) = DesignSize; // the bitmap is the whole screen; only the view part is shown
        var (scale, origin) = Fit();
        var view = View;
        var destination = ToScreen(view.X, view.Y, view.Width, view.Height, scale, origin);

        if (_dirty || _bitmap == null)
        {
            _bitmap?.Dispose();
            _bitmap = RenderToBitmap(designWidth * 2, designHeight * 2);
            _dirty = false;
        }
        context.DrawImage(_bitmap, new Rect(view.X * 2, view.Y * 2, view.Width * 2, view.Height * 2), destination);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Colors.Gray, 0.8), 1), destination);

        // Faint outlines so empty boxes (dividers, hidden-by-state text) can still be found.
        var faint = new Pen(new SolidColorBrush(Colors.Gray, 0.5), 1);
        foreach (var element in _layout.Elements)
            context.DrawRectangle(null, faint, ToScreen(element.X, element.Y, element.Width, element.Height, scale, origin));

        if (_selected != null)
        {
            var rect = ToScreen(_selected.X, _selected.Y, _selected.Width, _selected.Height, scale, origin);
            context.DrawRectangle(null, new Pen(Brushes.DeepSkyBlue, 2), rect);
            foreach (var corner in Corners(rect))
                context.DrawRectangle(Brushes.White, new Pen(Brushes.DeepSkyBlue, 2), new Rect(corner.X - HandleSize / 2, corner.Y - HandleSize / 2, HandleSize, HandleSize));
        }
    }

    private WriteableBitmap RenderToBitmap(int width, int height)
    {
        using var rendered = ScreenRenderer.Render(_layout!, _context, width, height);
        using var bgra = rendered.ColorType == SKColorType.Bgra8888 ? null : rendered.Copy(SKColorType.Bgra8888);
        var source = bgra ?? rendered;

        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var frame = bitmap.Lock();
        var rowBytes = Math.Min(source.RowBytes, frame.RowBytes);
        var row = new byte[rowBytes];
        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(source.GetPixels() + y * source.RowBytes, row, 0, rowBytes);
            Marshal.Copy(row, 0, frame.Address + y * frame.RowBytes, rowBytes);
        }
        return bitmap;
    }

    private static Rect ToScreen(float x, float y, float w, float h, double scale, Point origin) =>
        new(origin.X + x * scale, origin.Y + y * scale, w * scale, h * scale);

    private static Point[] Corners(Rect r) => new[] { r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft };

    // -- Dragging ----------------------------------------------------------------

    private Point ToDesign(Point screen)
    {
        var (scale, origin) = Fit();
        return new Point((screen.X - origin.X) / scale, (screen.Y - origin.Y) / scale);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_layout == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // This control handles the raw pointer itself. Without this, Avalonia's touch gesture recognizers (hold, scroll, the
        // navigation page's swipe-back) take the pointer over after a moment and the drag dies.
        e.PreventGestureRecognition();

        var position = e.GetPosition(this);
        var (scale, origin) = Fit();

        // A corner of the selected element starts a resize.
        if (_selected != null)
        {
            var rect = ToScreen(_selected.X, _selected.Y, _selected.Width, _selected.Height, scale, origin);
            var corners = Corners(rect);
            for (var i = 0; i < corners.Length; i++)
            {
                if (Math.Abs(position.X - corners[i].X) <= HandleSize && Math.Abs(position.Y - corners[i].Y) <= HandleSize)
                {
                    BeginDrag(e, Drag.Resize, position);
                    _resizeCorner = i;
                    return;
                }
            }
        }

        // Otherwise pick the topmost element under the pointer (or nothing).
        var design = ToDesign(position);
        var hit = _layout.Elements.LastOrDefault(el =>
            design.X >= el.X && design.X <= el.X + el.Width && design.Y >= el.Y && design.Y <= el.Y + el.Height);
        if (hit != _selected)
        {
            _selected = hit;
            SelectionChanged?.Invoke(hit);
            InvalidateVisual();
        }
        if (hit != null) BeginDrag(e, Drag.Move, position);
        e.Handled = true;
    }

    private void BeginDrag(PointerPressedEventArgs e, Drag kind, Point position)
    {
        _drag = kind;
        _dragStartDesign = ToDesign(position);
        _dragStartRect = (_selected!.X, _selected.Y, _selected.Width, _selected.Height);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag == Drag.None || _selected == null) return;

        // If something still took the capture away mid-drag, take it back so the drag keeps following the finger.
        if (e.Pointer.Captured != this) e.Pointer.Capture(this);

        var now = ToDesign(e.GetPosition(this));
        var dx = (float)Math.Round(now.X - _dragStartDesign.X);
        var dy = (float)Math.Round(now.Y - _dragStartDesign.Y);
        var (x, y, w, h) = _dragStartRect;

        if (_drag == Drag.Move)
        {
            _selected.X = x + dx;
            _selected.Y = y + dy;
            ScreenSizes.ClampToBounds(_selected, _layout!.Target);
        }
        else
        {
            // Corners: 0 top-left, 1 top-right, 2 bottom-right, 3 bottom-left. The opposite corner stays put.
            var left = x;
            var top = y;
            var right = x + w;
            var bottom = y + h;
            if (_resizeCorner is 0 or 3) left = Math.Min(x + dx, right - 4);
            else right = Math.Max(x + w + dx, left + 4);
            if (_resizeCorner is 0 or 1) top = Math.Min(y + dy, bottom - 4);
            else bottom = Math.Max(y + h + dy, top + 4);
            // Never past the shown area.
            var area = View;
            left = Math.Max(left, area.X);
            top = Math.Max(top, area.Y);
            right = Math.Min(right, area.X + area.Width);
            bottom = Math.Min(bottom, area.Y + area.Height);
            _selected.X = left;
            _selected.Y = top;
            _selected.Width = Math.Max(right - left, 1);
            _selected.Height = Math.Max(bottom - top, 1);
        }

        Refresh();
        ElementChanged?.Invoke(_selected);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == Drag.None) return;
        _drag = Drag.None;
        e.Pointer.Capture(null);
    }
}
