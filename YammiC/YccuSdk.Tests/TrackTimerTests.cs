using CNCO.YccuSdk.Can;
using CNCO.YccuSdk.Platform;
using CNCO.YccuSdk.Tracks;
using Xunit;

namespace CNCO.YccuSdk.Tests;

internal static class TrackTestData
{
    public const double Lat0 = 50.0;
    public const double Lon0 = 8.0;
    private static readonly double MetresPerDegLon = 111194.9 * Math.Cos(Lat0 * Math.PI / 180.0);

    public static GeoPoint At(double eastMetres, double northMetres = 0) =>
        new(Lat0 + northMetres / 111194.9, Lon0 + eastMetres / MetresPerDegLon);

    /// <summary>A vertical line (north-south, 20 m long) at the given distance east of the origin.</summary>
    public static TrackLine VerticalLine(double eastMetres) => new(At(eastMetres, -10), At(eastMetres, 10));

    public static Track Circuit(int sectorLines = 1)
    {
        var track = new Track { Id = "t1", Name = "Test Ring", StartFinish = VerticalLine(0) };
        if (sectorLines >= 1) track.Sectors.Add(VerticalLine(100));
        return track;
    }

    public static GpsFix Fix(double seconds, double eastMetres, double accuracy = 5, double northMetres = 0)
    {
        var p = At(eastMetres, northMetres);
        return new GpsFix(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc).AddSeconds(seconds), p.Lat, p.Lon, 20, 90, accuracy, (long)(seconds * 1e9));
    }

    /// <summary>One lap's worth of fixes starting just before the start line at <paramref name="t"/>: start line at t+0.5, sector at t+9.5, finish at t+11.5.</summary>
    public static IEnumerable<GpsFix> Lap(double t)
    {
        yield return Fix(t, -5);
        yield return Fix(t + 1, 5);
        yield return Fix(t + 9, 95);
        yield return Fix(t + 10, 105);
        yield return Fix(t + 11, -5);
        yield return Fix(t + 12, 5);
    }
}

public class GeoMathTests
{
    [Fact]
    public void DistanceBetweenKnownPoints_IsAboutRight()
    {
        var d = GeoMath.Distance(TrackTestData.At(0), TrackTestData.At(100, 0));
        Assert.InRange(d, 99.5, 100.5);
    }

    [Fact]
    public void Movement_ThroughTheLine_CrossesAtTheRightFraction()
    {
        var line = TrackTestData.VerticalLine(0);
        var hit = GeoMath.Cross(TrackTestData.At(-5), TrackTestData.At(15), line.A, line.B);
        Assert.NotNull(hit);
        Assert.InRange(hit!.Value.T, 0.24, 0.26);
    }

    [Fact]
    public void Movement_ThatStopsShort_OrMissesTheLineEnds_DoesNotCross()
    {
        var line = TrackTestData.VerticalLine(0);
        Assert.Null(GeoMath.Cross(TrackTestData.At(-5), TrackTestData.At(-1), line.A, line.B));
        Assert.Null(GeoMath.Cross(TrackTestData.At(-5, 30), TrackTestData.At(5, 30), line.A, line.B));
    }

    [Fact]
    public void OppositeDirections_ReportOppositeSides()
    {
        var line = TrackTestData.VerticalLine(0);
        var east = GeoMath.Cross(TrackTestData.At(-5), TrackTestData.At(5), line.A, line.B);
        var west = GeoMath.Cross(TrackTestData.At(5), TrackTestData.At(-5), line.A, line.B);
        Assert.NotEqual(east!.Value.Side, west!.Value.Side);
    }
}

public class LapDetectorTests
{
    [Fact]
    public void FirstCrossing_StartsTheLap_AndTheNextLapReportsInterpolatedTimes()
    {
        var detector = new LapDetector(TrackTestData.Circuit());
        var events = TrackTestData.Lap(0).SelectMany(detector.Process).ToList();

        Assert.IsType<LapStarted>(events[0]);
        var sector = Assert.IsType<SectorCompleted>(events[1]);
        Assert.Equal(1, sector.Sector);
        Assert.Equal(9.0, sector.Time.TotalSeconds, 2);

        var lap = Assert.IsType<LapCompleted>(events[2]);
        Assert.Equal(1, lap.Lap);
        Assert.Equal(11.0, lap.Time.TotalSeconds, 2);
        Assert.Equal(new[] { 9.0, 2.0 }, lap.SectorTimes.Select(s => Math.Round(s.TotalSeconds, 2)).ToArray());
    }

    [Fact]
    public void SecondLap_IsNumberedAndTimedFromTheFirstFinish()
    {
        var detector = new LapDetector(TrackTestData.Circuit());
        var events = TrackTestData.Lap(0).Concat(TrackTestData.Lap(12)).SelectMany(detector.Process).OfType<LapCompleted>().ToList();
        Assert.Equal(new[] { 1, 2 }, events.Select(l => l.Lap).ToArray());
    }

    [Fact]
    public void CrossingTheOtherWay_IsIgnored()
    {
        var detector = new LapDetector(TrackTestData.Circuit(sectorLines: 0));
        // The first crossing is westward and sets the direction; the eastward crossing afterwards must not count.
        var events = new[] { TrackTestData.Fix(0, 5), TrackTestData.Fix(1, -5), TrackTestData.Fix(20, -5), TrackTestData.Fix(21, 5) }
            .SelectMany(detector.Process).ToList();
        Assert.Single(events);
        Assert.IsType<LapStarted>(events[0]);
    }

    [Fact]
    public void ALapShorterThanTheMinimum_IsNotCounted()
    {
        var detector = new LapDetector(TrackTestData.Circuit(sectorLines: 0), TimeSpan.FromSeconds(30));
        var events = new[]
        {
            TrackTestData.Fix(0, -5), TrackTestData.Fix(1, 5), TrackTestData.Fix(2, 50), TrackTestData.Fix(3, -5), TrackTestData.Fix(4, 5),
        }.SelectMany(detector.Process).ToList();
        Assert.Single(events);
    }

    [Fact]
    public void PoorAccuracyFixes_AreSkipped()
    {
        var detector = new LapDetector(TrackTestData.Circuit(sectorLines: 0));
        var events = new[] { TrackTestData.Fix(0, -5, accuracy: 80), TrackTestData.Fix(1, 5, accuracy: 80) }.SelectMany(detector.Process).ToList();
        Assert.Empty(events);
    }
}

public class TrackStorageTests
{
    [Fact]
    public void Track_RoundTripsThroughJson_AndRejectsGarbage()
    {
        var track = TrackTestData.Circuit();
        var back = Track.FromJson(track.ToJson());
        Assert.NotNull(back);
        Assert.Equal("Test Ring", back!.Name);
        Assert.Single(back.Sectors);
        Assert.Null(Track.FromJson("not json"));
    }

    [Fact]
    public void Validate_RejectsMissingNameShortLinesAndTooManySectors()
    {
        var t = TrackTestData.Circuit();
        t.Name = " ";
        Assert.NotNull(t.Validate());

        t = TrackTestData.Circuit();
        t.StartFinish = new TrackLine(TrackTestData.At(0), TrackTestData.At(0.5));
        Assert.NotNull(t.Validate());

        t = TrackTestData.Circuit();
        for (var i = 0; i < 3; i++) t.Sectors.Add(TrackTestData.VerticalLine(150 + i * 50));
        Assert.NotNull(t.Validate());
    }

    [Fact]
    public void Library_SavesListsImportsAndSkipsDamagedFiles()
    {
        var files = new InMemoryTrackFileStore();
        var library = new TrackLibrary(files);
        var track = TrackTestData.Circuit();
        track.Id = "";
        Assert.Null(library.Save(track));
        Assert.NotEmpty(track.Id);

        files.Write(TrackLibrary.Folder, "broken.json", "{{{");
        Assert.Single(library.List());
        Assert.Equal(new[] { "broken.json" }, library.Skipped.ToArray());

        var imported = library.Import(library.Export(track));
        Assert.NotNull(imported);
        Assert.NotEqual(track.Id, imported!.Id);
        Assert.Equal(2, library.List().Count);

        library.Delete(track.Id);
        Assert.Single(library.List());
    }

    [Fact]
    public void Save_RefusesAnInvalidTrack()
    {
        var library = new TrackLibrary(new InMemoryTrackFileStore());
        Assert.NotNull(library.Save(new Track { Name = "No line" }));
    }

    [Fact]
    public void Selector_PicksTheNearestWithinTheThreshold_PreferringTheLastUsedOnTies()
    {
        var near = TrackTestData.Circuit();
        near.Id = "near";
        near.LastUsed = new DateTime(2026, 1, 1);
        var alsoNear = TrackTestData.Circuit();
        alsoNear.Id = "also";
        alsoNear.LastUsed = new DateTime(2026, 6, 1);
        var far = TrackTestData.Circuit();
        far.Id = "far";
        far.StartFinish = new TrackLine(TrackTestData.At(5000, -10), TrackTestData.At(5000, 10));

        Assert.Equal("also", TrackSelector.Select(new[] { near, alsoNear, far }, TrackTestData.At(50))!.Id);
        Assert.Null(TrackSelector.Select(new[] { far }, TrackTestData.At(0)));
    }
}

public class SessionFileTests
{
    [Fact]
    public void Writer_AppendsOneRecordPerLine_AndReaderToleratesACutLastLine()
    {
        var files = new InMemoryTrackFileStore();
        var writer = new SessionWriter(files, "s.jsonl");
        writer.Header(DateTime.UtcNow, TrackTestData.Circuit(), 5, new[] { "tps" });
        writer.Sample(TrackTestData.Fix(0, 1), new Dictionary<string, double> { ["tps"] = 3.5 });
        writer.Event(new LapCompleted(1, TimeSpan.FromSeconds(61.5), new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(31.5) }, DateTime.UtcNow));

        var text = files.Read(SessionWriter.Folder, "s.jsonl")!;
        Assert.Equal(3, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);

        var records = SessionWriter.Read(text + "{\"Type\":\"sam");
        Assert.Equal(new[] { "header", "sample", "lap" }, records.Select(r => r.Type).ToArray());
        Assert.Equal(3.5, records[1].Can!["tps"]);
        Assert.Equal(61.5, records[2].Seconds);
    }

    [Fact]
    public void Gpx_ContainsTheTrackPointsAndLapWaypoints()
    {
        var files = new InMemoryTrackFileStore();
        var writer = new SessionWriter(files, "g.jsonl");
        writer.Header(DateTime.UtcNow, TrackTestData.Circuit(), 5, Array.Empty<string>());
        var fix = TrackTestData.Fix(0, 1);
        writer.Sample(fix, null);
        writer.Event(new LapCompleted(1, TimeSpan.FromSeconds(75.25), new[] { TimeSpan.FromSeconds(75.25) }, fix.Utc));

        var gpx = GpxExporter.Export(SessionWriter.Read(files.Read(SessionWriter.Folder, "g.jsonl")!));
        Assert.Contains("<trkpt", gpx);
        Assert.Contains("Lap 1: 1:15.250", gpx);
        Assert.Contains("Test Ring", gpx);
    }
}

public class WebMercatorTests
{
    [Fact]
    public void PixelConversion_RoundTrips()
    {
        var p = new GeoPoint(50.1234, 8.5678);
        var (x, y) = WebMercator.ToPixel(p, 17);
        var back = WebMercator.FromPixel(x, y, 17);
        Assert.Equal(p.Lat, back.Lat, 6);
        Assert.Equal(p.Lon, back.Lon, 6);
    }

    [Fact]
    public void TheWorldCentre_IsInTheMiddleOfTheWorldAtZoomZero()
    {
        var (x, y) = WebMercator.ToPixel(new GeoPoint(0, 0), 0);
        Assert.Equal(128, x, 3);
        Assert.Equal(128, y, 3);
    }
}

public class LapMessageTests
{
    private static LapCompleted Lap(int n, double total, params double[] sectors) =>
        new(n, TimeSpan.FromSeconds(total), sectors.Select(TimeSpan.FromSeconds).ToArray(), DateTime.UtcNow);

    [Fact]
    public void FirstLap_HasNoReferenceAndNoGaps()
    {
        var m = LapMessage.Build(Lap(1, 61.5, 30, 31.5), null);
        Assert.Equal(6150, m.LapTimeCs);
        Assert.Equal(Packets.App.AppSendLapSectorTime.NoTime, m.ReferenceCs);
        Assert.All(m.GapsCs, g => Assert.Equal(Packets.App.AppSendLapSectorTime.GapNotMeasured, g));
    }

    [Fact]
    public void LaterLap_ReportsGapsToTheBestLapsSectors()
    {
        var m = LapMessage.Build(Lap(2, 62.0, 30.5, 31.5), Lap(1, 61.0, 30, 31));
        Assert.Equal(6100, m.ReferenceCs);
        Assert.Equal(50, m.GapsCs[0]);
        Assert.Equal(50, m.GapsCs[1]);
        Assert.Equal(Packets.App.AppSendLapSectorTime.GapNotMeasured, m.GapsCs[2]);
        Assert.Null(Packets.App.AppSendLapSectorTime.Validate(m.LapNo, m.LapTimeCs, m.ReferenceCs, 0, m.GapsCs));
    }
}

public class GpsRateProbeTests
{
    private static List<GpsFix> Fixes(double hz, double seconds) =>
        Enumerable.Range(0, (int)(hz * seconds) + 1).Select(i => TrackTestData.Fix(i / hz, i)).ToList();

    [Fact]
    public void Measure_ReturnsTheFixRate()
    {
        Assert.Equal(5.0, GpsRateProbe.Measure(Fixes(5, 6)), 2);
        Assert.Equal(0, GpsRateProbe.Measure(Fixes(1, 1)));
    }

    [Fact]
    public void Supports_NeedsMostOfTheRequestedRate()
    {
        Assert.True(GpsRateProbe.Supports(5, 4.5));
        Assert.False(GpsRateProbe.Supports(10, 1.0));
    }
}

public class ReadyBitTests
{
    private static CanRawReading Reading(byte b4, DateTime at) => new(ReadyBit.CanId, new byte[] { 0, 0, 0, 0, b4, 0, 0, 0 }, at, at);

    [Fact]
    public void Bit5OfByte4_IsTheFlag()
    {
        var now = DateTime.UtcNow;
        Assert.Equal(ReadyBitState.Ready, ReadyBit.Read(new[] { Reading(0x20, now) }, now));
        Assert.Equal(ReadyBitState.NotReady, ReadyBit.Read(new[] { Reading(0x0F, now) }, now));
    }

    [Fact]
    public void StaleOrMissingData_IsUnknown()
    {
        var now = DateTime.UtcNow;
        Assert.Equal(ReadyBitState.Unknown, ReadyBit.Read(new[] { Reading(0x20, now.AddSeconds(-10)) }, now));
        Assert.Equal(ReadyBitState.Unknown, ReadyBit.Read(Array.Empty<CanRawReading>(), now));
    }
}

public class TimerControllerTests
{
    private sealed class Store : IYmcKeyValueStore
    {
        public Dictionary<string, string> Values { get; } = new();
        public string? GetString(string key) => Values.TryGetValue(key, out var v) ? v : null;
        public void SetString(string key, string value) => Values[key] = value;
    }

    private sealed class Vibrator : IVibrator
    {
        public int Count;
        public void Vibrate() => Count++;
    }

    private sealed class Board : IBoardMessageSender
    {
        public List<string> Shown { get; } = new();
        public int Released;
        public Task ShowTrackNameAsync(string trackName) { Shown.Add(trackName); return Task.CompletedTask; }
        public Task ReleaseAsync() { Released++; return Task.CompletedTask; }
    }

    private sealed class Laps : ILapSender
    {
        public List<(LapCompleted Lap, LapCompleted? Best)> Sent { get; } = new();
        public Task SendAsync(LapCompleted lap, LapCompleted? best) { Sent.Add((lap, best)); return Task.CompletedTask; }
    }

    private sealed class Rig
    {
        public InMemoryTrackFileStore Files { get; } = new();
        public Store Kv { get; } = new();
        public Vibrator Vib { get; } = new();
        public Board Board { get; } = new();
        public Laps Laps { get; } = new();
        public TrackLibrary Library { get; }
        public TimerController Timer { get; }

        public Rig()
        {
            Library = new TrackLibrary(Files);
            Library.Save(TrackTestData.Circuit());
            Timer = new TimerController(Library, Files, Kv, Vib, Board, Laps);
        }
    }

    [Fact]
    public void CannotArmWithoutATrack()
    {
        var rig = new Rig();
        Assert.NotNull(rig.Timer.Arm(ArmSource.Rider));
        Assert.Equal(TimerState.Idle, rig.Timer.State);
    }

    [Fact]
    public void BikeReady_ArmsLocksAndTellsTheRider()
    {
        var rig = new Rig();
        Assert.True(rig.Timer.SelectTrack("t1"));
        rig.Timer.OnReadyBit(ReadyBitState.Ready);

        Assert.Equal(TimerState.Ready, rig.Timer.State);
        Assert.True(rig.Timer.IsLocked);
        Assert.Equal(1, rig.Vib.Count);
        Assert.Equal(new[] { "Test Ring" }, rig.Board.Shown);
        Assert.False(rig.Timer.SelectTrack(null));
        Assert.Equal("t1", rig.Timer.SelectedTrackId);
    }

    [Fact]
    public void StaleData_NeverDisarms_ButTheBikeClearingTheFlagDoesWhenItArmed()
    {
        var rig = new Rig();
        rig.Timer.SelectTrack("t1");
        rig.Timer.OnReadyBit(ReadyBitState.Ready);

        rig.Timer.OnReadyBit(ReadyBitState.Unknown);
        Assert.Equal(TimerState.Ready, rig.Timer.State);

        rig.Timer.OnReadyBit(ReadyBitState.NotReady);
        Assert.Equal(TimerState.Idle, rig.Timer.State);
        Assert.Equal(1, rig.Board.Released);
    }

    [Fact]
    public void ARiderArmedSession_IsNotEndedByTheBike()
    {
        var rig = new Rig();
        rig.Timer.SelectTrack("t1");
        Assert.Null(rig.Timer.Arm(ArmSource.Rider));
        rig.Timer.OnReadyBit(ReadyBitState.NotReady);
        Assert.Equal(TimerState.Ready, rig.Timer.State);
        rig.Timer.Disarm();
        Assert.Equal(TimerState.Idle, rig.Timer.State);
    }

    [Fact]
    public void Fixes_TimeLaps_RecordTheSession_AndSendEachLapToTheBike()
    {
        var rig = new Rig();
        rig.Timer.SelectTrack("t1");
        rig.Timer.OnReadyBit(ReadyBitState.Ready);

        foreach (var fix in TrackTestData.Lap(0)) rig.Timer.OnFix(fix);

        Assert.Equal(TimerState.Timing, rig.Timer.State);
        Assert.Single(rig.Timer.Laps);
        Assert.Equal(11.0, rig.Timer.Laps[0].Time.TotalSeconds, 2);
        Assert.Single(rig.Laps.Sent);
        Assert.Null(rig.Laps.Sent[0].Best);

        var records = SessionWriter.Read(rig.Files.Read(SessionWriter.Folder, rig.Timer.SessionName!)!);
        Assert.Equal("header", records[0].Type);
        Assert.Contains(records, r => r.Type == "lap");
        Assert.Equal(6, records.Count(r => r.Type == "sample"));
    }

    [Fact]
    public void FixesWhileIdle_AreIgnored()
    {
        var rig = new Rig();
        rig.Timer.SelectTrack("t1");
        rig.Timer.OnFix(TrackTestData.Fix(0, 1));
        Assert.Empty(rig.Files.List(SessionWriter.Folder));
    }

    [Fact]
    public void AfterARestart_AnArmedSessionResumesInTheSameFile_OrEndsCleanlyWithoutLocation()
    {
        var rig = new Rig();
        rig.Timer.SelectTrack("t1");
        rig.Timer.OnReadyBit(ReadyBitState.Ready);
        var session = rig.Timer.SessionName!;

        var again = new TimerController(rig.Library, rig.Files, rig.Kv, rig.Vib, rig.Board, rig.Laps);
        again.Restore(canLocate: true);
        Assert.Equal(TimerState.Ready, again.State);
        Assert.Equal(session, again.SessionName);

        var third = new TimerController(rig.Library, rig.Files, rig.Kv, rig.Vib, rig.Board, rig.Laps);
        third.Restore(canLocate: false);
        Assert.Equal(TimerState.Idle, third.State);
        Assert.Empty(rig.Kv.GetString("track_timer_armed") ?? "");
    }
}
