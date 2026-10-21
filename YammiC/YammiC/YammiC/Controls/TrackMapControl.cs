using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CNCO.YccuSdk.Tracks;

namespace YammiC.Controls;

public enum MapLineKind
{
    StartFinish,
    Sector,
    PitOut,
}

/// <summary>
/// A line being drawn on the map: either point may still be unset.
/// </summary>
public sealed class MapLine
{
    public string Label { get; set; } = "";
    public MapLineKind Kind { get; set; } = MapLineKind.Sector;

    /// <summary>
    /// 1-based number of a sector line, used for its badge and colour.
    /// </summary>
    public int Number { get; set; }

    public GeoPoint? A { get; set; }
    public GeoPoint? B { get; set; }
}

/// <summary>
/// An OpenStreetMap tile map for drawing the start/finish, sector and pit-out lines: drag to pan, pinch / wheel / buttons to zoom,
/// tap to place the next point of the active line, drag a point to move it. Tiles come from the public OSM server (online only).
/// </summary>
public sealed class TrackMapControl : Control
{
    private const double HandleRadius = 9;
    private const double HitRadius = 30;
    private const double MinZoom = 3;
    private const double MaxZoom = 20;
    private const int MaxTileLevel = 19;

    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim Downloads = new(4);
    private static readonly ConcurrentDictionary<(int Z, int X, int Y), Bitmap?> Tiles = new();
    private static readonly ConcurrentDictionary<(int Z, int X, int Y), bool> Loading = new();

    private static readonly Color Land = Color.FromRgb(0xF2, 0xEF, 0xE9);
    private static readonly Color StartFinishColour = Color.FromRgb(0x1B, 0x1B, 0x1F);
    private static readonly Color PitOutColour = Color.FromRgb(0xE0, 0x8A, 0x1E);
    private static readonly Color[] SectorColours =
    {
        Color.FromRgb(0x2D, 0x7F, 0xF9),
        Color.FromRgb(0x8E, 0x4C, 0xD6),
        Color.FromRgb(0x14, 0x9E, 0x86),
    };
    private static readonly Color MeColour = Color.FromRgb(0x1A, 0x73, 0xE8);

    private readonly Dictionary<int, Point> _touches = new();
    private HashSet<(int, int, int)> _visible = new();

    private GeoPoint _center = new(50.0, 8.0);
    private double _zoom = 16;
    private Point _pressedAt;
    private Point _last;
    private bool _moved;
    private bool _gesture;          // a pinch happened since the first finger went down: no taps until all are up
    private (MapLine Line, bool IsA)? _dragging;
    private bool _panning;
    private MapLine? _selected;
    private double _pinchStartDistance;
    private double _pinchStartZoom;
    private GeoPoint _pinchAnchor;

    public IList<MapLine> Lines { get; set; } = new List<MapLine>();

    /// <summary>
    /// Taps place the next unset point of this line.
    /// </summary>
    public MapLine? ActiveLine { get; set; }

    /// <summary>The line being looked at or edited: drawn with a halo. Placing a line, or touching its handle or body, selects it.</summary>
    public MapLine? SelectedLine
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            SelectionChanged?.Invoke();
            InvalidateVisual();
        }
    }

    public event Action? SelectionChanged;

    /// <summary>
    /// The rider's position.
    /// </summary>
    public GeoPoint? Here { get; set; }

    /// <summary>
    /// Direction of travel in degrees, drawn as a pointer on the position marker while moving.
    /// </summary>
    public double? HereBearing { get; set; }

    /// <summary>
    /// Raised when a point was placed or moved.
    /// </summary>
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

    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, MinZoom, MaxZoom);
            InvalidateVisual();
        }
    }

    public void ZoomBy(double delta) => Zoom = _zoom + delta;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // The OSM tile usage policy asks for an identifying user agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YammiC-TrackEditor/1.0");
        return client;
    }

    // Tiles are fetched at an integer level; between levels they are drawn scaled (1x to 2x), so zooming is continuous.
    private int TileLevel => Math.Min(MaxTileLevel, (int)Math.Floor(_zoom));

    private double Scale => Math.Pow(2, _zoom - TileLevel);

    private Point ToScreen(GeoPoint p) => ToScreen(p, _center, _zoom);

    private Point ToScreen(GeoPoint p, GeoPoint center, double zoom)
    {
        var level = Math.Min(MaxTileLevel, (int)Math.Floor(zoom));
        var scale = Math.Pow(2, zoom - level);
        var (cx, cy) = WebMercator.ToPixel(center, level);
        var (x, y) = WebMercator.ToPixel(p, level);
        return new Point(Bounds.Width / 2 + (x - cx) * scale, Bounds.Height / 2 + (y - cy) * scale);
    }

    private GeoPoint FromScreen(Point p)
    {
        var (cx, cy) = WebMercator.ToPixel(_center, TileLevel);
        var scale = Scale;
        return WebMercator.FromPixel(cx + (p.X - Bounds.Width / 2) / scale, cy + (p.Y - Bounds.Height / 2) / scale, TileLevel);
    }

    /// <summary>
    /// Puts <paramref name="anchor"/> under the screen point <paramref name="screen"/> at the current zoom.
    /// </summary>
    private void PinTo(GeoPoint anchor, Point screen)
    {
        var (ax, ay) = WebMercator.ToPixel(anchor, TileLevel);
        var scale = Scale;
        _center = WebMercator.FromPixel(ax - (screen.X - Bounds.Width / 2) / scale, ay - (screen.Y - Bounds.Height / 2) / scale, TileLevel);
    }

    // -- Drawing ---------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(Land), bounds);
        using var clip = context.PushClip(bounds);

        DrawTiles(context);
        foreach (var line in Lines.Where(l => l.Kind != MapLineKind.StartFinish)) DrawLine(context, line);
        foreach (var line in Lines.Where(l => l.Kind == MapLineKind.StartFinish)) DrawLine(context, line);
        if (Here != null) DrawMe(context, ToScreen(Here.Value));
        DrawScaleBar(context);
        DrawChip(context, "© OpenStreetMap contributors", new Point(Bounds.Width - 6, Bounds.Height - 6), 10, alignRight: true, alignBottom: true, Colors.DimGray);
    }

    private void DrawTiles(DrawingContext context)
    {
        var level = TileLevel;
        var scale = Scale;
        var (cx, cy) = WebMercator.ToPixel(_center, level);
        var left = cx - Bounds.Width / 2 / scale;
        var top = cy - Bounds.Height / 2 / scale;
        var tilesPerSide = 1 << level;
        var size = WebMercator.TileSize * scale;
        var firstX = (int)Math.Floor(left / WebMercator.TileSize);
        var lastX = (int)Math.Floor((left + Bounds.Width / scale) / WebMercator.TileSize);
        var firstY = Math.Max(0, (int)Math.Floor(top / WebMercator.TileSize));
        var lastY = Math.Min(tilesPerSide - 1, (int)Math.Floor((top + Bounds.Height / scale) / WebMercator.TileSize));

        var visible = new HashSet<(int, int, int)>();
        for (var ty = firstY; ty <= lastY; ty++)
        {
            for (var tx = firstX; tx <= lastX; tx++)
            {
                var wrapped = ((tx % tilesPerSide) + tilesPerSide) % tilesPerSide;
                var dest = new Rect((tx * WebMercator.TileSize - left) * scale, (ty * WebMercator.TileSize - top) * scale, size + 0.5, size + 0.5);
                visible.Add((level, wrapped, ty));
                var tile = GetTile(level, wrapped, ty);
                if (tile != null) context.DrawImage(tile, new Rect(tile.Size), dest);
                else DrawParentTile(context, level, wrapped, ty, dest);
            }
        }
        _visible = visible;
    }

    // While a tile loads, show the blurry piece of the tile one or two levels up so the map never has holes.
    private static void DrawParentTile(DrawingContext context, int level, int x, int y, Rect dest)
    {
        for (var up = 1; up <= 2 && level - up >= 0; up++)
        {
            var key = (level - up, x >> up, y >> up);
            if (!Tiles.TryGetValue(key, out var parent) || parent == null) continue;
            var parts = 1 << up;
            var piece = new Size(parent.Size.Width / parts, parent.Size.Height / parts);
            var source = new Rect((x & (parts - 1)) * piece.Width, (y & (parts - 1)) * piece.Height, piece.Width, piece.Height);
            context.DrawImage(parent, source, dest);
            return;
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
            await Downloads.WaitAsync().ConfigureAwait(false);
            try
            {
                // Queued while panning past: skip tiles that are no longer on screen (they load when scrolled back to).
                if (!_visible.Contains(key)) return;
                var bytes = await Http.GetByteArrayAsync($"https://tile.openstreetmap.org/{key.Z}/{key.X}/{key.Y}.png").ConfigureAwait(false);
                using var stream = new MemoryStream(bytes);
                var bitmap = new Bitmap(stream);
                if (Tiles.Count > 600) Tiles.Clear();
                Tiles[key] = bitmap;
            }
            finally
            {
                Downloads.Release();
            }
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

    private static Color ColourOf(MapLine line) => line.Kind switch
    {
        MapLineKind.StartFinish => StartFinishColour,
        MapLineKind.PitOut => PitOutColour,
        _ => SectorColours[Math.Max(0, line.Number - 1) % SectorColours.Length],
    };

    private static string BadgeOf(MapLine line) => line.Kind switch
    {
        MapLineKind.StartFinish => "S/F",
        MapLineKind.PitOut => "PIT",
        _ => line.Number.ToString(CultureInfo.InvariantCulture),
    };

    private void DrawLine(DrawingContext context, MapLine line)
    {
        var colour = ColourOf(line);
        var brush = new SolidColorBrush(colour);
        var isActive = ReferenceEquals(line, ActiveLine);
        var isSelected = ReferenceEquals(line, _selected) || isActive;
        // With a line selected, the others step back a little so the selection reads at a glance.
        using var fade = context.PushOpacity(_selected == null || isSelected ? 1.0 : 0.6);

        if (line.A != null && line.B != null)
        {
            var from = ToScreen(line.A.Value);
            var to = ToScreen(line.B.Value);
            if (isSelected)
            {
                // A wide soft halo in the line's own colour, and a thin dark outline so it shows on any map.
                context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(70, colour.R, colour.G, colour.B)), 26, lineCap: PenLineCap.Round), from, to);
                context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(150, colour.R, colour.G, colour.B)), 14, lineCap: PenLineCap.Round), from, to);
            }
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), 8, lineCap: PenLineCap.Round), from, to);
            switch (line.Kind)
            {
                case MapLineKind.StartFinish:
                    // A chequered strip.
                    context.DrawLine(new Pen(brush, 5, lineCap: PenLineCap.Flat), from, to);
                    context.DrawLine(new Pen(Brushes.White, 5, new DashStyle(new double[] { 1, 1 }, 0), PenLineCap.Flat), from, to);
                    break;
                case MapLineKind.PitOut:
                    context.DrawLine(new Pen(brush, 4, new DashStyle(new double[] { 2, 1.4 }, 0), PenLineCap.Flat), from, to);
                    break;
                default:
                    context.DrawLine(new Pen(brush, 4, lineCap: PenLineCap.Round), from, to);
                    break;
            }

            DrawDirection(context, from, to, colour);
            DrawBadge(context, line, from, to, colour, isSelected);
        }

        foreach (var point in new[] { line.A, line.B })
        {
            if (point == null) continue;
            DrawHandle(context, ToScreen(point.Value), colour, isSelected);
        }

        if (line.A != null && line.B == null)
        {
            var only = ToScreen(line.A.Value);
            DrawChip(context, line.Label, new Point(only.X + 14, only.Y - 26), 12, alignRight: false, alignBottom: false, colour);
        }
    }

    private static void DrawHandle(DrawingContext context, Point at, Color colour, bool selected)
    {
        var radius = selected ? HandleRadius + 3 : HandleRadius;
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(48, 0, 0, 0)), null, new Point(at.X, at.Y + 1.5), radius + 1.5, radius + 1.5);
        context.DrawEllipse(Brushes.White, null, at, radius, radius);
        context.DrawEllipse(new SolidColorBrush(colour), null, at, radius - 3, radius - 3);
        if (selected) context.DrawEllipse(null, new Pen(new SolidColorBrush(colour), 2.5), at, radius + 5, radius + 5);
    }

    // An arrow across the middle of the line: the way the line has to be crossed (to the right of A to B, which is how the bike
    // side counts it).
    private static void DrawDirection(DrawingContext context, Point from, Point to, Color colour)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 30) return;
        var middle = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        var arrow = new Vector(-dy / length, dx / length);
        var side = new Vector(-arrow.Y, arrow.X);
        var tail = middle - arrow * 16;
        var tip = middle + arrow * 16;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(tail, false);
            g.LineTo(tip);
            g.EndFigure(false);
            g.BeginFigure(tip - arrow * 9 + side * 7, false);
            g.LineTo(tip);
            g.LineTo(tip - arrow * 9 - side * 7);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(Brushes.White, 6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(colour), 3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }

    // A small round badge beside the middle of the line: "S/F", the sector number or "PIT".
    private void DrawBadge(DrawingContext context, MapLine line, Point from, Point to, Color colour, bool selected)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 24) return;
        var middle = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        // Offset to the side (up on screen) so it does not cover the line.
        var nx = -dy / length;
        var ny = dx / length;
        if (ny > 0)
        {
            nx = -nx;
            ny = -ny;
        }
        // On the line, a quarter of the way along (the arrow sits at the middle).
        var at = new Point(from.X + dx * 0.25, from.Y + dy * 0.25);
        var text = Format(BadgeOf(line), 12, Brushes.White, FontWeight.Bold);
        var width = Math.Max(24, text.Width + 14);
        if (selected) width += 8;
        var box = new Rect(at.X - width / 2, at.Y - 11, width, 22);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(48, 0, 0, 0)), null, box.Translate(new Vector(0, 1.5)), 11, 11);
        context.DrawRectangle(new SolidColorBrush(colour), new Pen(Brushes.White, 2), box, 11, 11);
        context.DrawText(text, new Point(box.X + (box.Width - text.Width) / 2, box.Y + (box.Height - text.Height) / 2));
    }

    private void DrawMe(DrawingContext context, Point at)
    {
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(40, MeColour.R, MeColour.G, MeColour.B)), null, at, 22, 22);
        if (HereBearing != null)
        {
            var radians = HereBearing.Value * Math.PI / 180.0;
            var forward = new Vector(Math.Sin(radians), -Math.Cos(radians));
            var side = new Vector(-forward.Y, forward.X);
            var tip = at + forward * 19;
            var left = at + forward * 9 + side * 6;
            var right = at + forward * 9 - side * 6;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(tip, true);
                g.LineTo(left);
                g.LineTo(right);
                g.EndFigure(true);
            }
            context.DrawGeometry(new SolidColorBrush(MeColour), new Pen(Brushes.White, 1.5, lineJoin: PenLineJoin.Round), geometry);
        }
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), null, new Point(at.X, at.Y + 1.5), 9.5, 9.5);
        context.DrawEllipse(Brushes.White, null, at, 9, 9);
        context.DrawEllipse(new SolidColorBrush(MeColour), null, at, 6.5, 6.5);
    }

    private void DrawScaleBar(DrawingContext context)
    {
        var metresPerPixel = 156543.03392 * Math.Cos(_center.Lat * Math.PI / 180.0) / Math.Pow(2, _zoom);
        if (metresPerPixel <= 0) return;
        var steps = new double[] { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000 };
        var metres = steps.LastOrDefault(s => s / metresPerPixel <= 110);
        if (metres == 0) metres = steps[0];
        var width = metres / metresPerPixel;
        var label = metres >= 1000 ? $"{metres / 1000:0.#} km" : $"{metres:0} m";
        var origin = new Point(10, Bounds.Height - 14);
        var pen = new Pen(Brushes.White, 5, lineCap: PenLineCap.Round);
        var ink = new Pen(Brushes.Black, 2, lineCap: PenLineCap.Round);
        foreach (var p in new[] { pen, ink })
        {
            context.DrawLine(p, origin, new Point(origin.X + width, origin.Y));
            context.DrawLine(p, origin, new Point(origin.X, origin.Y - 5));
            context.DrawLine(p, new Point(origin.X + width, origin.Y), new Point(origin.X + width, origin.Y - 5));
        }
        var text = Format(label, 11, Brushes.Black, FontWeight.SemiBold);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), null, new Rect(origin.X, origin.Y - 24, text.Width + 8, text.Height + 2), 4, 4);
        context.DrawText(text, new Point(origin.X + 4, origin.Y - 23));
    }

    private static FormattedText Format(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);

    private static void DrawChip(DrawingContext context, string text, Point at, double size, bool alignRight, bool alignBottom, Color ink)
    {
        var formatted = Format(text, size, new SolidColorBrush(ink), FontWeight.SemiBold);
        var origin = new Point(alignRight ? at.X - formatted.Width - 4 : at.X, alignBottom ? at.Y - formatted.Height - 1 : at.Y);
        var box = new Rect(origin.X - 4, origin.Y - 1, formatted.Width + 8, formatted.Height + 2);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)), null, box, 4, 4);
        context.DrawText(formatted, origin);
    }

    // -- Input -----------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // Keep scroll containers and gesture recognisers from taking the pointer from us (the same fix as the screen editor).
        e.PreventGestureRecognition();
        e.Pointer.Capture(this);
        var at = e.GetPosition(this);
        _touches[e.Pointer.Id] = at;
        e.Handled = true;

        if (_touches.Count == 1)
        {
            _pressedAt = _last = at;
            _moved = false;
            _gesture = false;
            _dragging = HitHandle(at);
            if (_dragging is { } grabbed) SelectedLine = grabbed.Line;
            _panning = _dragging == null;
        }
        else if (_touches.Count == 2)
        {
            // A second finger turns whatever was going on into a pinch.
            _gesture = true;
            _moved = true;
            _dragging = null;
            _panning = false;
            BeginPinch();
        }
    }

    private void BeginPinch()
    {
        var points = _touches.Values.Take(2).ToArray();
        _pinchStartDistance = Math.Max(1, Distance(points[0], points[1]));
        _pinchStartZoom = _zoom;
        _pinchAnchor = FromScreen(Mid(points[0], points[1]));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured != this || !_touches.ContainsKey(e.Pointer.Id)) return;
        var now = e.GetPosition(this);
        _touches[e.Pointer.Id] = now;

        if (_touches.Count >= 2)
        {
            var points = _touches.Values.Take(2).ToArray();
            _zoom = Math.Clamp(_pinchStartZoom + Math.Log2(Math.Max(1, Distance(points[0], points[1])) / _pinchStartDistance), MinZoom, MaxZoom);
            PinTo(_pinchAnchor, Mid(points[0], points[1]));
            InvalidateVisual();
            return;
        }

        if (!_moved && Distance(now, _pressedAt) > 8) _moved = true;

        if (_dragging != null && _moved)
        {
            var drag = _dragging.Value;
            var point = FromScreen(now);
            if (drag.IsA) drag.Line.A = point;
            else drag.Line.B = point;
            LinesChanged?.Invoke();
            InvalidateVisual();
        }
        else if (_panning && _moved)
        {
            var scale = Scale;
            var (cx, cy) = WebMercator.ToPixel(_center, TileLevel);
            _center = WebMercator.FromPixel(cx - (now.X - _last.X) / scale, cy - (now.Y - _last.Y) / scale, TileLevel);
            InvalidateVisual();
        }
        _last = now;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        var wasLast = _touches.Count <= 1;
        _touches.Remove(e.Pointer.Id);

        if (!wasLast)
        {
            // One finger left after a pinch: carry on panning from where it is, without a jump.
            if (_touches.Count == 1)
            {
                _last = _touches.Values.First();
                _panning = true;
            }
            return;
        }

        var line = ActiveLine;
        if (!_moved && !_gesture && line == null)
        {
            // A tap on a line's body selects it.
            if (HitLine(e.GetPosition(this)) is { } tapped) SelectedLine = tapped;
        }
        if (!_moved && !_gesture && line != null)
        {
            SelectedLine = line;
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

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _touches.Remove(e.Pointer.Id);
        if (_touches.Count == 0)
        {
            _dragging = null;
            _panning = false;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        // Zoom around the cursor, in half steps (a touchpad sends many small deltas).
        var at = e.GetPosition(this);
        var anchor = FromScreen(at);
        _zoom = Math.Clamp(_zoom + Math.Clamp(e.Delta.Y, -1, 1) * 0.5, MinZoom, MaxZoom);
        PinTo(anchor, at);
        InvalidateVisual();
        e.Handled = true;
    }

    private static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private MapLine? HitLine(Point at)
    {
        MapLine? best = null;
        var bestDistance = 22.0;
        foreach (var line in Lines)
        {
            if (line.A == null || line.B == null) continue;
            var d = DistanceToSegment(at, ToScreen(line.A.Value), ToScreen(line.B.Value));
            if (d < bestDistance)
            {
                bestDistance = d;
                best = line;
            }
        }
        return best;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return Distance(p, new Point(a.X + t * dx, a.Y + t * dy));
    }

    private (MapLine Line, bool IsA)? HitHandle(Point at)
    {
        (MapLine, bool)? best = null;
        var bestDistance = HitRadius;
        foreach (var line in Lines)
        {
            foreach (var (point, isA) in new[] { (line.A, true), (line.B, false) })
            {
                if (point == null) continue;
                var distance = Distance(ToScreen(point.Value), at);
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
