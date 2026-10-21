namespace CNCO.YccuSdk.Tracks;

public readonly record struct GeoPoint(double Lat, double Lon);

/// <summary>Where a movement segment crossed a line: <see cref="T"/> is the fraction along the movement, <see cref="Side"/> is +1/-1 for the side it came from.</summary>
public readonly record struct Crossing(double T, int Side);

public static class GeoMath
{
    private const double EarthRadius = 6371000.0;

    public static double Distance(GeoPoint a, GeoPoint b)
    {
        var (x, y) = Project(b, a);
        return Math.Sqrt(x * x + y * y);
    }

    public static GeoPoint Midpoint(GeoPoint a, GeoPoint b) => new((a.Lat + b.Lat) / 2, (a.Lon + b.Lon) / 2);

    /// <summary>Metres east and north of <paramref name="origin"/> (flat approximation, fine over a track).</summary>
    public static (double X, double Y) Project(GeoPoint p, GeoPoint origin)
    {
        var rad = Math.PI / 180.0;
        var x = (p.Lon - origin.Lon) * rad * Math.Cos(origin.Lat * rad) * EarthRadius;
        var y = (p.Lat - origin.Lat) * rad * EarthRadius;
        return (x, y);
    }

    /// <summary>
    /// Finds where the movement <paramref name="from"/>→<paramref name="to"/> crosses the line <paramref name="a"/>–<paramref name="b"/>
    /// (a segment, not an infinite line). Null when it does not cross.
    /// </summary>
    public static Crossing? Cross(GeoPoint from, GeoPoint to, GeoPoint a, GeoPoint b)
    {
        var (ax, ay) = Project(a, from);
        var (bx, by) = Project(b, from);
        var (tx, ty) = Project(to, from);

        // Movement p + t*r (p is the origin), line q + u*s.
        var rx = tx;
        var ry = ty;
        var sx = bx - ax;
        var sy = by - ay;
        var denominator = rx * sy - ry * sx;
        if (Math.Abs(denominator) < 1e-9) return null;

        var t = (ax * sy - ay * sx) / denominator;
        var u = (ax * ry - ay * rx) / denominator;
        if (t < 0 || t > 1 || u < 0 || u > 1) return null;

        // Which side of the line the movement came from: sign of the cross product of the line direction and the start offset.
        var side = Math.Sign(sx * (0 - ay) - sy * (0 - ax));
        return new Crossing(t, side == 0 ? 1 : side);
    }
}
