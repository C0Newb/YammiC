using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CNCO.YccuSdk.Tracks;

namespace YammiC.Controls;

/// <summary>A line being drawn on the map: either point may still be unset.</summary>
public sealed class MapLine
{
    public string Label { get; set; } = "";
    public bool IsStartFinish { get; set; }
    public GeoPoint? A { get; set; }
    public GeoPoint? B { get; set; }
}

/// <summary>
/// An OpenStreetMap tile map for drawing the start/finish and sector lines: drag to pan, wheel or buttons to zoom, tap to place
/// the next point of the active line, drag a point to move it. Tiles come from the public OSM server (online only).
/// </summary>
public sealed class TrackMapControl : Control
{
    private const double HandleRadius = 12;
    private const double HitRadius = 30;
    private const int MinZoom = 3;
    private const int MaxZoom = 19;

    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<(int Z, int X, int Y), Bitmap?> Tiles = new();
    private static readonly ConcurrentDictionary<(int Z, int X, int Y), bool> Loading = new();

    private GeoPoint _center = new(50.0, 8.0);
    private int _zoom = 16;
    private Point _pressedAt;
    private Point _last;
    private bool _moved;
    private (MapLine Line, bool IsA)? _dragging;
    private bool _panning;

    public IList<MapLine> Lines { get; set; } = new List<MapLine>();

    /// <summary>Taps place the next unset point of this line.</summary>
    public MapLine? ActiveLine { get; set; }

    /// <summary>The rider's position, drawn as a dot.</summary>
    public GeoPoint? Here { get; set; }

    /// <summary>Raised when a point was placed or moved.</summary>
    public event Action? LinesChanged;

    public GeoPoint Center
    {
        get => _center;
        set
        {
            _center = value;
            InvalidateVisual();
        }
    }

    public int Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, MinZoom, MaxZoom);
            InvalidateVisual();
        }
    }

    public void ZoomBy(int delta) => Zoom = _zoom + delta;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // The OSM tile usage policy asks for an identifying user agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YammiC-TrackEditor/1.0");
        return client;
    }

    private (double X, double Y) CenterPixel => WebMercator.ToPixel(_center, _zoom);

    private Point ToScreen(GeoPoint p)
    {
        var (cx, cy) = CenterPixel;
        var (x, y) = WebMercator.ToPixel(p, _zoom);
        return new Point(Bounds.Width / 2 + (x - cx), Bounds.Height / 2 + (y - cy));
    }

    private GeoPoint FromScreen(Point p)
    {
        var (cx, cy) = CenterPixel;
        return WebMercator.FromPixel(cx + (p.X - Bounds.Width / 2), cy + (p.Y - Bounds.Height / 2), _zoom);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xE8)), bounds);
        using var clip = context.PushClip(bounds);

        DrawTiles(context);
        foreach (var line in Lines) DrawLine(context, line);
        if (Here is { } here)
        {
            var p = ToScreen(here);
            context.DrawEllipse(Brushes.White, null, p, 9, 9);
            context.DrawEllipse(Brushes.DodgerBlue, null, p, 6, 6);
        }
        DrawText(context, "© OpenStreetMap contributors", new Point(Bounds.Width - 4, Bounds.Height - 4), 10, alignRight: true, alignBottom: true);
    }

    private void DrawTiles(DrawingContext context)
    {
        var (cx, cy) = CenterPixel;
        var left = cx - Bounds.Width / 2;
        var top = cy - Bounds.Height / 2;
        var tilesPerSide = 1 << _zoom;
        var firstX = (int)Math.Floor(left / WebMercator.TileSize);
        var lastX = (int)Math.Floor((left + Bounds.Width) / WebMercator.TileSize);
        var firstY = Math.Max(0, (int)Math.Floor(top / WebMercator.TileSize));
        var lastY = Math.Min(tilesPerSide - 1, (int)Math.Floor((top + Bounds.Height) / WebMercator.TileSize));

        for (var ty = firstY; ty <= lastY; ty++)
        {
            for (var tx = firstX; tx <= lastX; tx++)
            {
                var wrapped = ((tx % tilesPerSide) + tilesPerSide) % tilesPerSide;
                var dest = new Rect(tx * WebMercator.TileSize - left, ty * WebMercator.TileSize - top, WebMercator.TileSize, WebMercator.TileSize);
                var tile = GetTile(_zoom, wrapped, ty);
                if (tile != null) context.DrawImage(tile, new Rect(tile.Size), dest);
            }
        }
    }

    private Bitmap? GetTile(int z, int x, int y)
    {
        var key = (z, x, y);
        if (Tiles.TryGetValue(key, out var cached)) return cached;
        if (Loading.TryAdd(key, true)) _ = LoadTileAsync(key);
        return null;
    }

    private async Task LoadTileAsync((int Z, int X, int Y) key)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync($"https://tile.openstreetmap.org/{key.Z}/{key.X}/{key.Y}.png").ConfigureAwait(false);
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            if (Tiles.Count > 400) Tiles.Clear();
            Tiles[key] = bitmap;
        }
        catch (Exception)
        {
            // Offline or refused: leave the tile empty (retried when it scrolls into view again).
        }
        finally
        {
            Loading.TryRemove(key, out _);
        }
        Dispatcher.UIThread.Post(InvalidateVisual);
    }

    private void DrawLine(DrawingContext context, MapLine line)
    {
        var colour = line.IsStartFinish ? Colors.Crimson : Colors.DarkOrange;
        var brush = new SolidColorBrush(colour);
        if (line.A is { } a && line.B is { } b)
        {
            context.DrawLine(new Pen(Brushes.White, 7), ToScreen(a), ToScreen(b));
            context.DrawLine(new Pen(brush, 4), ToScreen(a), ToScreen(b));
        }
        foreach (var point in new[] { line.A, line.B })
        {
            if (point is not { } p) continue;
            var s = ToScreen(p);
            context.DrawEllipse(Brushes.White, new Pen(brush, 3), s, HandleRadius, HandleRadius);
        }
        if (line.A is { } first && line.B is { } second)
        {
            var middle = ToScreen(GeoMath.Midpoint(first, second));
            DrawText(context, line.Label, new Point(middle.X + 10, middle.Y - 24), 14, alignRight: false, alignBottom: false, brush);
        }
        else if (line.A is { } only)
        {
            var s = ToScreen(only);
            DrawText(context, line.Label, new Point(s.X + 14, s.Y - 24), 14, alignRight: false, alignBottom: false, brush);
        }
    }

    private static void DrawText(DrawingContext context, string text, Point at, double size, bool alignRight, bool alignBottom, IBrush? brush = null)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, size, brush ?? Brushes.Black);
        var origin = new Point(alignRight ? at.X - formatted.Width : at.X, alignBottom ? at.Y - formatted.Height : at.Y);
        var box = new Rect(origin.X - 3, origin.Y - 1, formatted.Width + 6, formatted.Height + 2);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), null, box);
        context.DrawText(formatted, origin);
    }

    // -- Input -----------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // Keep scroll containers and gesture recognisers from taking the pointer from us (the same fix as the screen editor).
        e.PreventGestureRecognition();
        e.Pointer.Capture(this);
        _pressedAt = _last = e.GetPosition(this);
        _moved = false;
        _dragging = HitHandle(_pressedAt);
        _panning = _dragging == null;
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured != this) return;
        var now = e.GetPosition(this);
        if (!_moved && Distance(now, _pressedAt) > 8) _moved = true;

        if (_dragging is { } drag && _moved)
        {
            var point = FromScreen(now);
            if (drag.IsA) drag.Line.A = point;
            else drag.Line.B = point;
            LinesChanged?.Invoke();
            InvalidateVisual();
        }
        else if (_panning && _moved)
        {
            var (cx, cy) = CenterPixel;
            _center = WebMercator.FromPixel(cx - (now.X - _last.X), cy - (now.Y - _last.Y), _zoom);
            InvalidateVisual();
        }
        _last = now;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        if (!_moved && ActiveLine is { } line)
        {
            var point = FromScreen(e.GetPosition(this));
            if (line.A == null) line.A = point;
            else if (line.B == null) line.B = point;
            if (line.A != null && line.B != null) ActiveLine = null;
            LinesChanged?.Invoke();
            InvalidateVisual();
        }
        _dragging = null;
        _panning = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomBy(e.Delta.Y > 0 ? 1 : -1);
        e.Handled = true;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private (MapLine Line, bool IsA)? HitHandle(Point at)
    {
        (MapLine, bool)? best = null;
        var bestDistance = HitRadius;
        foreach (var line in Lines)
        {
            foreach (var (point, isA) in new[] { (line.A, true), (line.B, false) })
            {
                if (point is not { } p) continue;
                var distance = Distance(ToScreen(p), at);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = (line, isA);
                }
            }
        }
        return best;
    }
}
