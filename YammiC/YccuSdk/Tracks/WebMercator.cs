namespace CNCO.YccuSdk.Tracks;

/// <summary>Slippy-map (OpenStreetMap style) tile maths: 256-pixel tiles, the whole world is 256 * 2^zoom pixels wide.</summary>
public static class WebMercator
{
    public const int TileSize = 256;
    public const double MaxLatitude = 85.0511287798;

    public static double WorldSize(double zoom) => TileSize * Math.Pow(2, zoom);

    /// <summary>World pixel position of a point at the given zoom (origin top-left of the world).</summary>
    public static (double X, double Y) ToPixel(GeoPoint p, double zoom)
    {
        var size = WorldSize(zoom);
        var lat = Math.Clamp(p.Lat, -MaxLatitude, MaxLatitude) * Math.PI / 180.0;
        var x = (p.Lon + 180.0) / 360.0 * size;
        var y = (1.0 - Math.Log(Math.Tan(lat) + 1.0 / Math.Cos(lat)) / Math.PI) / 2.0 * size;
        return (x, y);
    }

    public static GeoPoint FromPixel(double x, double y, double zoom)
    {
        var size = WorldSize(zoom);
        var lon = x / size * 360.0 - 180.0;
        var n = Math.PI - 2.0 * Math.PI * y / size;
        var lat = 180.0 / Math.PI * Math.Atan(0.5 * (Math.Exp(n) - Math.Exp(-n)));
        return new GeoPoint(lat, lon);
    }
}
