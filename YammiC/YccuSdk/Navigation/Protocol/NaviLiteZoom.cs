using System.Globalization;
using System.Text;

namespace CNCO.YccuSdk.Navigation.Protocol;

/// <summary>
/// MAP_ZOOM_LEVEL_UPDATE (service 14) payload: `[current][max][min][showLabel][scale text]`. The observed bytes
/// `07 19 06 00 "0.2 mi"` are current 7 of 6..25, label hidden. The CCU greys out its zoom buttons after a press until this
/// is sent back, so the phone has to answer every zoom request with the new level.
/// </summary>
internal static class NaviLiteZoom
{
    public const int DefaultLevel = 7;
    public const int MinLevel = 6;
    public const int MaxLevel = 25;

    // Each level is one navigation zoom step (1.5x), with level 7 the unzoomed view and "0.2 mi" across.
    private const double ZoomPerLevel = 1.5;
    private const double DefaultMiles = 0.2;

    public static int LevelFor(float zoom) =>
        Math.Clamp(DefaultLevel + (int)Math.Round(Math.Log(zoom) / Math.Log(ZoomPerLevel)), MinLevel, MaxLevel);

    public static string Label(float zoom)
    {
        var miles = DefaultMiles / zoom;
        if (miles >= 0.1) return miles.ToString("0.##", CultureInfo.InvariantCulture) + " mi";
        var feet = Math.Max((int)(Math.Round(miles * 5280 / 10) * 10), 10);
        return feet.ToString(CultureInfo.InvariantCulture) + " ft";
    }

    public static byte[] Payload(float zoom)
    {
        var label = Encoding.ASCII.GetBytes(Label(zoom));
        var payload = new byte[4 + label.Length];
        payload[0] = (byte)LevelFor(zoom);
        payload[1] = MaxLevel;
        payload[2] = MinLevel;
        payload[3] = 0;
        label.CopyTo(payload, 4);
        return payload;
    }
}
