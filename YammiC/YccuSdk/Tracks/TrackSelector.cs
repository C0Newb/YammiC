namespace CNCO.YccuSdk.Tracks;

/// <summary>Picks the saved track the rider is standing at. Runs once per session start, so cost is irrelevant.</summary>
public static class TrackSelector
{
    /// <summary>Distance within which two tracks count as equally close and the more recently used one wins.</summary>
    private const double TieMetres = 25;

    public static Track? Select(IEnumerable<Track> tracks, GeoPoint here, double thresholdMetres = 500)
    {
        var candidates = tracks
            .Select(t => (Track: t, Distance: t.Center is { } c ? GeoMath.Distance(here, c) : double.MaxValue))
            .Where(x => x.Distance <= thresholdMetres)
            .OrderBy(x => x.Distance)
            .ToList();
        if (candidates.Count == 0) return null;

        var nearest = candidates[0].Distance;
        return candidates
            .Where(x => x.Distance - nearest <= TieMetres)
            .OrderByDescending(x => x.Track.LastUsed ?? DateTime.MinValue)
            .ThenBy(x => x.Distance)
            .First().Track;
    }
}
