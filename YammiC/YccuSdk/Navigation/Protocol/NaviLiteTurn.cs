using System.Text;

namespace CNCO.YccuSdk.Navigation.Protocol;

/// <summary>
/// The turn arrow shown for a manoeuvre. The numbers are the arrow ordinals the dashboard understands (the dashboard draws the
/// arrows itself; only the number is sent).
/// </summary>
public enum TurnIcon : byte
{
    Arriving = 0,
    ArrivingLeft = 1,
    ArrivingRight = 2,
    ViaPoint = 3,
    ViaPointLeft = 4,
    ViaPointRight = 5,
    KeepLeft = 6,
    KeepRight = 7,
    Continue = 8,
    DriveTo = 9,
    ExitLeft = 10,
    ExitRight = 11,
    ExitUnspecified = 12,
    Ferry = 13,
    Roundabout = 14,
    SharpLeft = 32,
    SharpRight = 33,
    Left = 34,
    Right = 35,
    UTurnLeft = 36,
    UTurnRight = 37,
    None = 69,
}

public enum DistanceUnit
{
    Metric,
    Imperial,
}

/// <summary>One turn-by-turn instruction: what to do, how far away it is and where.</summary>
public sealed record TurnGuidance(TurnIcon Icon, double DistanceMeters, string Road = "", DistanceUnit Units = DistanceUnit.Imperial)
{
    private const double MetersPerFoot = 0.3048;
    private const double MetersPerMile = 1609.344;

    /// <summary>The distance as a number and the unit text that goes with it ("500" "ft", "1.2" "mi", "300" "m", "2.5" "km").</summary>
    public (float Value, string Unit) Display()
    {
        if (Units == DistanceUnit.Imperial)
        {
            var feet = DistanceMeters / MetersPerFoot;
            // Feet up to a tenth of a mile, rounded to 50 so it reads like a sign (500 ft, not 487 ft).
            if (DistanceMeters < MetersPerMile / 10)
                return ((float)(feet < 100 ? Math.Round(feet / 10) * 10 : Math.Round(feet / 50) * 50), "ft");
            return ((float)Math.Round(DistanceMeters / MetersPerMile, DistanceMeters < MetersPerMile * 10 ? 1 : 0), "mi");
        }
        if (DistanceMeters < 1000)
            return ((float)(DistanceMeters < 100 ? Math.Round(DistanceMeters / 10) * 10 : Math.Round(DistanceMeters / 50) * 50), "m");
        return ((float)Math.Round(DistanceMeters / 1000, DistanceMeters < 10000 ? 1 : 0), "km");
    }

    /// <summary>"Turn right in 500 ft" style text, for logs and the test page.</summary>
    public string Describe()
    {
        var (value, unit) = Display();
        var what = Icon switch
        {
            TurnIcon.Left => "Turn left",
            TurnIcon.Right => "Turn right",
            TurnIcon.SharpLeft => "Turn sharp left",
            TurnIcon.SharpRight => "Turn sharp right",
            TurnIcon.KeepLeft => "Keep left",
            TurnIcon.KeepRight => "Keep right",
            TurnIcon.UTurnLeft or TurnIcon.UTurnRight => "Make a U-turn",
            TurnIcon.Roundabout => "Enter the roundabout",
            TurnIcon.ExitLeft => "Take the exit on the left",
            TurnIcon.ExitRight => "Take the exit on the right",
            TurnIcon.Arriving or TurnIcon.ArrivingLeft or TurnIcon.ArrivingRight => "Arrive",
            TurnIcon.Continue => "Continue",
            _ => Icon.ToString(),
        };
        return $"{what} in {value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} {unit}" + (Road.Length > 0 ? $" onto {Road}" : "");
    }
}

/// <summary>Builds the two turn-guidance messages the dashboards take (little-endian numbers, strings at most 144 bytes of UTF-8).</summary>
public static class NaviLiteTurn
{
    public const int NextTurnService = 4;
    public const int NavigationInfoService = 19;
    public const int MaxTextBytes = 144;
    public const int MaxLanes = 5;

    /// <summary>
    /// NEXT_TURN_DIST_UPDATE (service 4, the simpler dashboards): <c>[icon][distance f32][unit length][road length][unit][road]</c>.
    /// </summary>
    public static byte[] NextTurn(TurnGuidance turn)
    {
        var (value, unit) = turn.Display();
        var unitBytes = Text(unit);
        var roadBytes = Text(turn.Road);
        var payload = new List<byte> { (byte)turn.Icon };
        payload.AddRange(BitConverter.GetBytes(value));
        payload.Add((byte)unitBytes.Length);
        payload.Add((byte)roadBytes.Length);
        payload.AddRange(unitBytes);
        payload.AddRange(roadBytes);
        return payload.ToArray();
    }

    /// <summary>
    /// NAVIGATION_INFO_UPDATE (service 19, the 480x240 dashboards): the next turn plus what is left of the trip.
    /// <c>[icon][next distance f32][unit len][road len][remaining distance f32][remaining unit len][remaining minutes u32][lane count]
    /// [unit][road][remaining unit][lane bytes]</c>.
    /// </summary>
    public static byte[] NavigationInfo(TurnGuidance turn, double remainingMeters = 0, int remainingMinutes = 0, IReadOnlyList<byte>? lanes = null)
    {
        var (value, unit) = turn.Display();
        var remaining = (turn with { DistanceMeters = remainingMeters }).Display();
        var unitBytes = Text(unit);
        var roadBytes = Text(turn.Road);
        var remainingUnitBytes = Text(remaining.Unit);
        var laneBytes = (lanes ?? Array.Empty<byte>()).Take(MaxLanes).ToArray();

        var payload = new List<byte> { (byte)turn.Icon };
        payload.AddRange(BitConverter.GetBytes(value));
        payload.Add((byte)unitBytes.Length);
        payload.Add((byte)roadBytes.Length);
        payload.AddRange(BitConverter.GetBytes(remaining.Value));
        payload.Add((byte)remainingUnitBytes.Length);
        payload.AddRange(BitConverter.GetBytes((uint)Math.Max(0, remainingMinutes)));
        payload.Add((byte)laneBytes.Length);
        payload.AddRange(unitBytes);
        payload.AddRange(roadBytes);
        payload.AddRange(remainingUnitBytes);
        payload.AddRange(laneBytes);
        return payload.ToArray();
    }

    // Longer text is cut at a character boundary rather than refused.
    private static byte[] Text(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= MaxTextBytes) return bytes;
        var end = MaxTextBytes;
        while (end > 0 && (bytes[end] & 0xC0) == 0x80) end--;
        return bytes[..end];
    }
}

/// <summary>Which of the two messages a dashboard takes, from its part number.</summary>
public static class NaviLiteTurnModels
{
    /// <summary>
    /// The 480x240 dashboards (006-B4160, 006-B4920) take service 19, the older 006-B3952 takes service 4. An unknown number gets both:
    /// a dashboard ignores the message it does not use.
    /// </summary>
    public static (bool NextTurn, bool NavigationInfo) For(string? partNumber)
    {
        if (partNumber != null)
        {
            if (partNumber.StartsWith("006-B4160", StringComparison.Ordinal) || partNumber.StartsWith("006-B4920", StringComparison.Ordinal))
                return (false, true);
            if (partNumber.StartsWith("006-B3952", StringComparison.Ordinal))
                return (true, false);
        }
        return (true, true);
    }
}
