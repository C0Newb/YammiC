using System.Text.Json;
using System.Text.Json.Serialization;

namespace CNCO.YccuSdk.Tracks;

/// <summary>A line on the map drawn between two points.</summary>
public sealed record TrackLine(GeoPoint A, GeoPoint B)
{
    public double Length => GeoMath.Distance(A, B);
}

/// <summary>
/// A circuit: one start/finish line plus up to <see cref="MaxSectorLines"/> intermediate sector lines (so up to four sectors,
/// which is what the bike's lap message carries).
/// </summary>
public sealed class Track
{
    public const int MaxSectorLines = 3;
    public const double MinLineLength = 2.0;
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public TrackLine? StartFinish { get; set; }
    public List<TrackLine> Sectors { get; set; } = new();
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsed { get; set; }

    /// <summary>Number of timed sectors per lap (intermediate lines + 1).</summary>
    [JsonIgnore]
    public int SectorCount => Sectors.Count + 1;

    /// <summary>Null when the track can be used, else why not.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "The track needs a name.";
        if (StartFinish is not { } line) return "Draw the start/finish line.";
        if (line.Length < MinLineLength) return "The start/finish line is too short.";
        if (Sectors.Count > MaxSectorLines) return $"At most {MaxSectorLines} sector lines.";
        foreach (var sector in Sectors)
        {
            if (sector.Length < MinLineLength) return "A sector line is too short.";
        }
        return null;
    }

    public GeoPoint? Center => StartFinish is { } l ? GeoMath.Midpoint(l.A, l.B) : null;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Null when the text is not a track.</summary>
    public static Track? FromJson(string json)
    {
        try
        {
            var track = JsonSerializer.Deserialize<Track>(json, Options);
            return track?.Validate() == null ? track : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
