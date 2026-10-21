using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CNCO.YccuSdk.Screens;

namespace YammiC.Controls;

/// <summary>
/// The drag-and-drop surface of the screen editor: shows the picture the page renders to (supplied as <see cref="Preview"/>,
/// drawn by the same renderer the bike uses) with the page's elements outlined and the selected one marked. Drag an element to
/// move it, drag one of its corner squares to resize it. The control doesn't change anything itself: it raises
/// <see cref="BoundsDragged"/> and the owner applies the (clamped) result and calls <see cref="Refresh"/>.
/// </summary>
public class ScreenCanvas : Control
{
    private const double HandleSize = 14;

    private enum Drag { None, Move, Resize }

    private IReadOnlyList<EditElement>? _elements;
    private ScreenTarget _target;
    private EditElement? _selected;
    private Bitmap? _preview;

    private Drag _drag;
    private Point _dragStartDesign;
    private (float X, float Y, float W, float H) _dragStartRect;
    private int _resizeCorner;

    /// <summary>The user picked (or deselected, with null) an element.</summary>
    public event Action<EditElement?>? SelectionChanged;

    /// <summary>The user dragged the selected element to this position and size (design space). The owner applies it, clamped.</summary>
    public event Action<EditElement, float, float, float, float>? BoundsDragged;

    public IReadOnlyList<EditElement>? Elements
    {
        get => _elements;
        set
        {
            _elements = value;
            Refresh();
        }
    }

    public ScreenTarget Target
    {
        get => _target;
        set
        {
            _target = value;
            InvalidateMeasure();
            Refresh();
        }
    }

    /// <summary>The page rendered at design size; null until the first render finishes.</summary>
    public Bitmap? Preview
    {
        get => _preview;
        set
        {
            _preview = value;
            InvalidateVisual();
        }
    }

    public EditElement? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            InvalidateVisual();
        }
    }

    public void Refresh() => InvalidateVisual();

    // The part of the screen shown and editable: the visible strip on the pit board, everything on the navigation screen.
    private (float X, float Y, float Width, float Height) View => ScreenSizes.Bounds(_target);

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

        var view = View;
        var (scale, origin) = Fit();
        var destination = ToScreen(view.X, view.Y, view.Width, view.Height, scale, origin);

        if (_preview != null)
            context.DrawImage(_preview, new Rect(view.X, view.Y, view.Width, view.Height), destination);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Colors.Gray, 0.8), 1), destination);

        // Faint outlines so empty boxes (dividers, hidden-by-state text) can still be found.
        var faint = new Pen(new SolidColorBrush(Colors.Gray, 0.5), 1);
        foreach (var element in _elements ?? Array.Empty<EditElement>())
            context.DrawRectangle(null, faint, ToScreen(element.X, element.Y, element.Width, element.Height, scale, origin));

        if (_selected != null)
        {
            var rect = ToScreen(_selected.X, _selected.Y, _selected.Width, _selected.Height, scale, origin);
            context.DrawRectangle(null, new Pen(Brushes.DeepSkyBlue, 2), rect);
            foreach (var corner in Corners(rect))
                context.DrawRectangle(Brushes.White, new Pen(Brushes.DeepSkyBlue, 2), new Rect(corner.X - HandleSize / 2, corner.Y - HandleSize / 2, HandleSize, HandleSize));
        }
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
        if (_elements == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

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
        var hit = _elements.LastOrDefault(el =>
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

        float nx, ny, nw, nh;
        if (_drag == Drag.Move)
        {
            (nx, ny, nw, nh) = (x + dx, y + dy, w, h);
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
            (nx, ny, nw, nh) = (left, top, right - left, bottom - top);
        }

        BoundsDragged?.Invoke(_selected, nx, ny, nw, nh);
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
