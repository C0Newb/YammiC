using System.Globalization;
using System.Text;
using CNCO.YccuSdk.Can;
using CNCO.YccuSdk.Models;

namespace CNCO.YccuSdk.Screens;

/// <summary>
/// What a screen can show: the moment it is drawn for.
/// </summary>
/// <param name="Now">Local time.</param>
/// <param name="Media">Current music, or null for none.</param>
/// <param name="Night">Dark theme.</param>
/// <param name="Zoom">Magnification of the elements about the screen centre (1 = as designed); set by the bike's zoom buttons on the navigation screen.</param>
/// <param name="Can">Live bike data, or null when it isn't being collected (the variables then show a dash).</param>
/// <param name="AlbumArt">The current track's picture as encoded image bytes (JPEG/PNG), or null when there isn't one.</param>
public record ScreenContext(DateTime Now, YmcMediaMetaData? Media, bool Night, float Zoom = 1f, CanValues? Can = null, byte[]? AlbumArt = null)
{
    /// <summary>
    /// True when a real track is known (not the placeholder statuses, which have no state).
    /// </summary>
    public bool HasMusic =>
        Media != null && Media.PlaybackState != YmcMediaPlaybackState.MediaDeviceUnavailable && !string.IsNullOrWhiteSpace(Media.SongName);

    public bool IsPlaying => HasMusic && Media!.PlaybackState == YmcMediaPlaybackState.Playing;
}

/// <summary>
/// The <c>{variable}</c> language of text elements, in the spirit of KLWP's globals. A variable is <c>{name}</c> or
/// <c>{name:format}</c>, where the format applies to dates and times (a .NET format such as <c>HH:mm</c> or
/// <c>dddd d MMMM</c>). Use <c>{{</c> and <c>}}</c> for literal braces; unknown names are left as typed.
/// </summary>
public static class ScreenVariables
{
    public sealed record Info(string Name, string Description, string Example);

    /// <summary>
    /// The variables, for showing in the editor.
    /// </summary>
    public static IReadOnlyList<Info> Catalog { get; } = new Info[]
    {
        new("time", "Time, 24 hour. Optional format, e.g. {time:HH:mm:ss}", "14:07"),
        new("time12", "Time, 12 hour", "2:07 PM"),
        new("date", "Date. Optional format, e.g. {date:dddd d MMMM}", "Sun 18 Oct"),
        new("weekday", "Day of the week", "Sunday"),
        new("song", "Track title", "Bohemian Rhapsody"),
        new("artist", "Track artist", "Queen"),
        new("album", "Album", "A Night at the Opera"),
        new("state", "Playing / Paused", "Playing"),
        new("playicon", "Play or pause symbol", "▶"),
        new("elapsed", "Position in the track", "1:23"),
        new("duration", "Track length", "5:55"),
        new("rpm", "Engine speed, rounded to 50. Needs bike data (Settings > Bike data)", "4350"),
        new("water", "Coolant temperature, {water:C} (default) or {water:F}", "88"),
        new("intake", "Intake air temperature, {intake:C} (default) or {intake:F}", "31"),
        new("gear", "Gear (N for neutral, - between gears)", "3"),
        new("tps", "Throttle valve position, in the bike's own units (about 0-13.5 observed). Number format allowed, e.g. {tps:0}", "12.5"),
        new("aps", "Twist-grip position, in the bike's own units (about 0-85 observed)", "13.0"),
        new("bank", "Bank angle, degrees", "23"),
        new("pitch", "Pitch rate", "1.5"),
        new("accelx", "Acceleration X (unverified units)", "0.15"),
        new("accely", "Acceleration Y (unverified units)", "-0.08"),
        new("frontspeed", "Front wheel speed", "62"),
        new("rearspeed", "Rear wheel speed", "62"),
    };

    /// <summary>
    /// Names of the variables that come from the bike's CAN data (the pit board only redraws every ~15 s).
    /// </summary>
    public static readonly IReadOnlySet<string> CanVariableNames = new HashSet<string>
    {
        "rpm", "water", "intake", "gear", "tps", "aps", "bank", "pitch", "accelx", "accely", "frontspeed", "rearspeed",
    };

    /// <summary>
    /// True when any text element of <paramref name="layout"/> uses a bike-data variable.
    /// </summary>
    public static bool UsesCan(ScreenLayout layout) =>
        layout.Elements.Any(e => e.Kind == ScreenElementKind.Text && TextUsesCan(e.Text));

    private static bool TextUsesCan(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (var i = text.IndexOf('{'); i >= 0 && i < text.Length; i = text.IndexOf('{', i + 1))
        {
            if (i + 1 < text.Length && text[i + 1] == '{') { i++; continue; }
            var end = text.IndexOf('}', i + 1);
            if (end < 0) return false;
            var token = text.Substring(i + 1, end - i - 1);
            var colon = token.IndexOf(':');
            var name = (colon < 0 ? token : token[..colon]).Trim().ToLowerInvariant();
            if (CanVariableNames.Contains(name)) return true;
            i = end;
        }
        return false;
    }

    private const string Dash = "—";

    private static string Number(double? value, string? format, string defaultFormat, CultureInfo culture)
    {
        if (value == null) return Dash;
        var v = value.Value;
        var f = format != null && (format.Contains('0') || format.Contains('#')) ? format : defaultFormat;
        try
        {
            return v.ToString(f, culture);
        }
        catch (FormatException)
        {
            return v.ToString(defaultFormat, culture);
        }
    }

    private static string Temperature(double? celsius, string? format, CultureInfo culture)
    {
        if (celsius == null) return Dash;
        var c = celsius.Value;
        return format != null && format.Trim().Equals("F", StringComparison.OrdinalIgnoreCase)
            ? (c * 9 / 5 + 32).ToString("0", culture)
            : c.ToString("0", culture);
    }

    private static string CanLookup(string name, string? format, CanValues? can, CultureInfo culture)
    {
        switch (name)
        {
            case "rpm":
                var rpm = can?.Rpm;
                return rpm != null ? (Math.Round(rpm.Value / 50) * 50).ToString("0", culture) : Dash;
            case "water": return Temperature(can?.WaterTempC, format, culture);
            case "intake": return Temperature(can?.IntakeAirTempC, format, culture);
            case "gear":
                return CanSignals.GearText(can?.Gear);
            case "tps": return Number(can?.Tps, format, "0.0", culture);
            case "aps": return Number(can?.Aps, format, "0.0", culture);
            case "bank": return Number(can?.BankAngle, format, "0", culture);
            case "pitch": return Number(can?.PitchRate, format, "0.0", culture);
            case "accelx": return Number(can?.AccelX, format, "0.00", culture);
            case "accely": return Number(can?.AccelY, format, "0.00", culture);
            case "frontspeed": return Number(can?.FrontWheelSpeed, format, "0", culture);
            case "rearspeed": return Number(can?.RearWheelSpeed, format, "0", culture);
            default: return Dash;
        }
    }

    /// <summary>
    /// Replaces the variables in <paramref name="template"/> using <paramref name="context"/>.
    /// </summary>
    public static string Resolve(string? template, ScreenContext context)
    {
        if (string.IsNullOrEmpty(template)) return "";

        var result = new StringBuilder(template.Length);
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                result.Append('{');
                i++;
            }
            else if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                result.Append('}');
                i++;
            }
            else if (c == '{')
            {
                var end = template.IndexOf('}', i + 1);
                if (end < 0)
                {
                    result.Append(c);
                    continue;
                }

                var token = template.Substring(i + 1, end - i - 1);
                var colon = token.IndexOf(':');
                var name = (colon < 0 ? token : token[..colon]).Trim().ToLowerInvariant();
                var format = colon < 0 ? null : token[(colon + 1)..];

                var value = Lookup(name, format, context);
                if (value == null) result.Append('{').Append(token).Append('}');
                else result.Append(value);
                i = end;
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }

    private static string? Lookup(string name, string? format, ScreenContext context)
    {
        var culture = CultureInfo.CurrentCulture;
        var media = context.HasMusic ? context.Media : null;
        switch (name)
        {
            case "time": return Format(context.Now, format ?? "HH:mm", culture);
            case "time12": return Format(context.Now, format ?? "h:mm tt", culture);
            case "date": return Format(context.Now, format ?? "ddd d MMM", culture);
            case "weekday": return Format(context.Now, format ?? "dddd", culture);
            case "song": return media?.SongName ?? "";
            case "artist": return media?.ArtistName ?? "";
            case "album": return media?.AlbumName ?? "";
            case "state": return media == null ? "" : media.PlaybackState == YmcMediaPlaybackState.Playing ? "Playing" : "Paused";
            case "playicon": return media == null ? "" : media.PlaybackState == YmcMediaPlaybackState.Playing ? "▶" : "⏸";
            case "elapsed": return media == null ? "" : Clock(media.ElapsedSeconds);
            case "duration": return media == null ? "" : Clock(media.DurationSeconds);
            default:
                return CanVariableNames.Contains(name) ? CanLookup(name, format, context.Can, culture) : null;
        }
    }

    private static string Format(DateTime value, string format, CultureInfo culture)
    {
        try
        {
            return value.ToString(format, culture);
        }
        catch (FormatException)
        {
            return value.ToString("HH:mm", culture);
        }
    }

    private static string Clock(int seconds) =>
        seconds <= 0 ? "0:00" : $"{seconds / 60}:{seconds % 60:00}";
}
