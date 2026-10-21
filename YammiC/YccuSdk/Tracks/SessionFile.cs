using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CNCO.YccuSdk.Tracks;

/// <summary>
/// One line of a session file. <see cref="Type"/> is "header", "sample", "lap", "sector" or "note".
/// </summary>
public sealed class SessionRecord
{
    public string Type { get; set; } = "";
    public DateTime Utc { get; set; }

    // header
    public string? TrackId { get; set; }
    public string? TrackName { get; set; }
    public double? GpsHz { get; set; }
    public List<string>? Signals { get; set; }

    // sample
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public double? Speed { get; set; }
    public double? Bearing { get; set; }
    public double? Accuracy { get; set; }
    public Dictionary<string, double>? Can { get; set; }

    // lap / sector
    public int? Lap { get; set; }
    public int? Sector { get; set; }
    public double? Seconds { get; set; }
    public bool? Valid { get; set; }
    public List<double>? SectorSeconds { get; set; }

    // note
    public string? Text { get; set; }
}

/// <summary>
/// Writes a session as JSON Lines: one record per line, appended and flushed as it happens, so a crash leaves a valid file
/// (at worst the last line is cut, which <see cref="Read"/> ignores).
/// </summary>
public sealed class SessionWriter
{
    public const string Folder = "sessions";

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ITrackFileStore _files;

    public SessionWriter(ITrackFileStore files, string name)
    {
        _files = files;
        Name = name;
    }

    public string Name { get; }

    public void Write(SessionRecord record) => _files.Append(Folder, Name, JsonSerializer.Serialize(record, Options) + "\n");

    public void Header(DateTime utc, Track track, double gpsHz, IEnumerable<string> signals) =>
        Write(new SessionRecord { Type = "header", Utc = utc, TrackId = track.Id, TrackName = track.Name, GpsHz = gpsHz, Signals = signals.ToList() });

    public void Sample(GpsFix fix, Dictionary<string, double>? can) =>
        Write(new SessionRecord
        {
            Type = "sample", Utc = fix.Utc, Lat = fix.Lat, Lon = fix.Lon, Speed = fix.SpeedMps, Bearing = fix.Bearing, Accuracy = fix.Accuracy,
            Can = can != null && can.Count > 0 ? can : null,
        });

    public void Event(TrackEvent e)
    {
        switch (e)
        {
            case LapStarted s:
                Write(new SessionRecord { Type = "note", Utc = s.Utc, Text = "Lap 1 started" });
                break;
            case SectorCompleted s:
                Write(new SessionRecord { Type = "sector", Utc = s.Utc, Lap = s.Lap, Sector = s.Sector, Seconds = s.Time.TotalSeconds });
                break;
            case LapCompleted l:
                Write(new SessionRecord { Type = "lap", Utc = l.Utc, Lap = l.Lap, Seconds = l.Time.TotalSeconds, SectorSeconds = l.SectorTimes.Select(t => t.TotalSeconds).ToList(), Valid = l.Valid ? null : false });
                break;
            case PitOutCrossed p:
                Write(new SessionRecord { Type = "note", Utc = p.Utc, Text = $"Pit out during lap {p.Lap}: lap invalid" });
                break;
        }
    }

    public void Note(DateTime utc, string text) => Write(new SessionRecord { Type = "note", Utc = utc, Text = text });

    /// <summary>
    /// Reads a session file; lines that are not valid records (such as a cut-off last line) are skipped.
    /// </summary>
    public static IReadOnlyList<SessionRecord> Read(string text)
    {
        var records = new List<SessionRecord>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var record = JsonSerializer.Deserialize<SessionRecord>(line, Options);
                if (record != null && record.Type != null && record.Type.Length > 0) records.Add(record);
            }
            catch (JsonException)
            {
                // A line cut short by a crash.
            }
        }
        return records;
    }
}

public static class GpxExporter
{
    /// <summary>
    /// A GPX track of the samples in a session, with laps as waypoints.
    /// </summary>
    public static string Export(IReadOnlyList<SessionRecord> records)
    {
        var name = records.FirstOrDefault(r => r.Type == "header")?.TrackName ?? "Session";
        var gpx = new StringBuilder();
        gpx.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        gpx.AppendLine("<gpx version=\"1.1\" creator=\"YammiC\" xmlns=\"http://www.topografix.com/GPX/1/1\">");
        var samples = records.Where(r => r.Type == "sample" && r.Lat != null && r.Lon != null).ToList();
        foreach (var lap in records.Where(r => r.Type == "lap"))
        {
            var near = samples.OrderBy(s => Math.Abs((s.Utc - lap.Utc).Ticks)).FirstOrDefault();
            if (near == null) continue;
            gpx.AppendLine($"  <wpt lat=\"{Num(near.Lat!.Value)}\" lon=\"{Num(near.Lon!.Value)}\"><name>Lap {lap.Lap}: {FormatLap(lap.Seconds ?? 0)}{(lap.Valid == false ? " (invalid)" : "")}</name></wpt>");
        }
        gpx.AppendLine($"  <trk><name>{Escape(name)}</name><trkseg>");
        foreach (var s in samples)
        {
            gpx.Append($"    <trkpt lat=\"{Num(s.Lat!.Value)}\" lon=\"{Num(s.Lon!.Value)}\"><time>{s.Utc.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}</time>");
            if (s.Speed != null) gpx.Append($"<extensions><speed>{Num(s.Speed.Value)}</speed></extensions>");
            gpx.AppendLine("</trkpt>");
        }
        gpx.AppendLine("  </trkseg></trk>");
        gpx.AppendLine("</gpx>");
        return gpx.ToString();
    }

    private static string Num(double value) => value.ToString("0.#######", CultureInfo.InvariantCulture);

    private static string Escape(string text) => System.Security.SecurityElement.Escape(text) ?? "";

    private static string FormatLap(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds:000}";
    }
}
