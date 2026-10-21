namespace CNCO.YccuSdk.Tracks;

/// <summary>
/// The saved tracks. One JSON file per track; a damaged file is skipped, not fatal.
/// </summary>
public sealed class TrackLibrary
{
    public const string Folder = "tracks";

    private readonly ITrackFileStore _files;
    private readonly Func<DateTime> _utcNow;

    public TrackLibrary(ITrackFileStore files, Func<DateTime>? utcNow = null)
    {
        _files = files;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Names of files that could not be read as tracks, from the last <see cref="List"/>.
    /// </summary>
    public IReadOnlyList<string> Skipped { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<Track> List()
    {
        var tracks = new List<Track>();
        var skipped = new List<string>();
        foreach (var name in _files.List(Folder))
        {
            var text = _files.Read(Folder, name);
            var track = text != null ? Track.FromJson(text) : null;
            if (track == null) skipped.Add(name);
            else tracks.Add(track);
        }
        Skipped = skipped;
        return tracks.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public Track? Get(string id)
    {
        var text = _files.Read(Folder, FileName(id));
        return text != null ? Track.FromJson(text) : null;
    }

    /// <summary>
    /// Saves the track (giving it an id if it has none). Returns an error message, or null when saved.
    /// </summary>
    public string? Save(Track track)
    {
        var problem = track.Validate();
        if (problem != null) return problem;
        if (string.IsNullOrEmpty(track.Id)) track.Id = Guid.NewGuid().ToString("N");
        _files.Write(Folder, FileName(track.Id), track.ToJson());
        return null;
    }

    public void Delete(string id) => _files.Delete(Folder, FileName(id));

    public void MarkUsed(string id)
    {
        var track = Get(id);
        if (track == null) return;
        track.LastUsed = _utcNow();
        Save(track);
    }

    public string Export(Track track) => track.ToJson();

    /// <summary>
    /// Adds a track from exported text under a new id. Returns the track, or null when the text is not a valid track.
    /// </summary>
    public Track? Import(string json)
    {
        var track = Track.FromJson(json);
        if (track == null) return null;
        track.Id = "";
        track.LastUsed = null;
        return Save(track) == null ? track : null;
    }

    private static string FileName(string id) => id + ".json";
}
