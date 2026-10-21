using System.Globalization;

namespace CNCO.YccuSdk.Screens;

/// <summary>
/// Everything a page can show at one moment, as plain data (safe to hand between threads).
/// </summary>
public sealed record ScreenSnapshot(
    ScreenContext Context,
    string PageId,
    int PageIndex,
    int PageCount,
    IReadOnlyDictionary<string, bool> Flags,
    IReadOnlyDictionary<string, string> Vars)
{
    private static readonly IReadOnlyDictionary<string, bool> NoFlags = new Dictionary<string, bool>();
    private static readonly IReadOnlyDictionary<string, string> NoVars = new Dictionary<string, string>();

    public static ScreenSnapshot Of(ScreenContext context, string pageId = "main", int pageIndex = 0, int pageCount = 1) =>
        new(context, pageId, pageIndex, pageCount, NoFlags, NoVars);

    /// <summary>
    /// Changes whenever something a page could show changes. The clock only counts to the minute unless
    /// <paramref name="includeSeconds"/>; bike data is included as the text the pages would show (so "—" for stale values).
    /// </summary>
    public string Signature(bool includeSeconds, TimeSpan? scrollStep = null)
    {
        var c = Context;
        var m = c.Media;
        var now = includeSeconds ? c.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) : c.Now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);

        // Scrolling text moves with the clock, so while it shows the picture changes every step.
        if (scrollStep != null && scrollStep.Value > TimeSpan.Zero && c.HasMusic) now += "." + c.Now.Ticks / scrollStep.Value.Ticks;
        var can = string.Join(',', ScreenVariables.CanVariableNames.Select(n => ScreenVariables.Resolve("{" + n + "}", c)));
        var flags = string.Join(',', Flags.OrderBy(f => f.Key).Select(f => $"{f.Key}={f.Value}"));
        var art = c.AlbumArt != null && c.AlbumArt.Length > 0 ? ArtStamp(c.AlbumArt) : "-";
        var vars = string.Join(',', Vars.OrderBy(v => v.Key).Select(v => $"{v.Key}={v.Value}"));
        return $"{PageId}|{PageIndex}/{PageCount}|{now}|{(c.Night ? 'N' : 'D')}|" +
               $"{m?.SongName}|{m?.ArtistName}|{m?.AlbumName}|{m?.PlaybackState}|{m?.ElapsedSeconds}|{m?.DurationSeconds}|{can}|{flags}|{vars}|{art}";
    }

    // Cheap fingerprint of a picture: its size plus a sample of its bytes (a different cover is practically never the same here).
    private static string ArtStamp(byte[] picture)
    {
        var hash = 17;
        var step = Math.Max(picture.Length / 64, 1);
        for (var i = 0; i < picture.Length; i += step) hash = unchecked(hash * 31 + picture[i]);
        return $"{picture.Length}:{hash}";
    }
}
