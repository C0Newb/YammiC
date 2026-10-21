using System.Text.Json;
using System.Text.Json.Serialization;

namespace CNCO.YccuSdk.Tracks;

/// <summary>
/// A line on the map drawn between two points. A <see cref="Directed"/> line only counts when it is crossed from the left of A→B to
/// the right of it (looking from A to B), so the order of the points is the direction of travel; an undirected line counts either way.
/// </summary>
public sealed record TrackLine(GeoPoint A, GeoPoint B, bool Directed = false)
{
    public double Length => GeoMath.Distance(A, B);

    /// <summary>The same line with the direction of travel reversed.</summary>
    public TrackLine Flipped() => this with { A = B, B = A };

    /// <summary>True when a crossing that came from <paramref name="side"/> (see <see cref="Crossing"/>) counts for this line.</summary>
    public bool Accepts(int side) => !Directed || side == 1;
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

    /// <summary>
    /// Optional pit-exit line: crossing it marks the lap in progress as invalid.
    /// </summary>
    public TrackLine? PitOut { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsed { get; set; }

    /// <summary>
    /// Number of timed sectors per lap (intermediate lines + 1).
    /// </summary>
    [JsonIgnore]
    public int SectorCount => Sectors.Count + 1;

    /// <summary>
    /// Null when the track can be used, else why not.
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "The track needs a name.";
        var line = StartFinish;
        if (line == null) return "Draw the start/finish line.";
        if (line.Length < MinLineLength) return "The start/finish line is too short.";
        if (Sectors.Count > MaxSectorLines) return $"At most {MaxSectorLines} sector lines.";
        if (PitOut != null && PitOut.Length < MinLineLength) return "The pit-out line is too short.";
        foreach (var sector in Sectors)
        {
            if (sector.Length < MinLineLength) return "A sector line is too short.";
        }
        return null;
    }

    public GeoPoint? Center => StartFinish != null ? GeoMath.Midpoint(StartFinish.A, StartFinish.B) : null;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// Null when the text is not a track.
    /// </summary>
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
