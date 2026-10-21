namespace CNCO.YccuSdk.Tracks;

/// <summary>
/// One GPS reading. <see cref="ElapsedNanos"/> is the monotonic clock of the fix and is what lap times are built from.
/// </summary>
public sealed record GpsFix(DateTime Utc, double Lat, double Lon, double SpeedMps, double Bearing, double Accuracy, long ElapsedNanos)
{
    public GeoPoint Point => new(Lat, Lon);
}

public abstract record TrackEvent;

/// <summary>
/// The first crossing of the start/finish line: the first lap begins.
/// </summary>
public sealed record LapStarted(DateTime Utc) : TrackEvent;

/// <summary>
/// A sector line was crossed. <see cref="Sector"/> is 1-based; <see cref="Time"/> is that sector's time.
/// </summary>
public sealed record SectorCompleted(int Lap, int Sector, TimeSpan Time, DateTime Utc) : TrackEvent;

/// <summary>
/// A lap was completed. <see cref="SectorTimes"/> has one entry per sector (the last runs to the finish line).
/// </summary>
public sealed record LapCompleted(int Lap, TimeSpan Time, IReadOnlyList<TimeSpan> SectorTimes, DateTime Utc, bool Valid = true) : TrackEvent;

/// <summary>
/// The pit-out line was crossed during a lap: that lap is marked invalid.
/// </summary>
public sealed record PitOutCrossed(int Lap, DateTime Utc) : TrackEvent;

/// <summary>
/// Turns GPS fixes into lap and sector events. Crossing times are interpolated between the two fixes either side of the line.
/// The direction of the first start/finish crossing is remembered; crossings the other way are ignored.
/// </summary>
public sealed class LapDetector
{
    private readonly Track _track;
    private readonly TimeSpan _minLap;
    private readonly double _maxAccuracy;
    private GpsFix? _previous;
    private int _direction;       // side the rider came from at the first crossing; 0 until known
    private bool _lapRunning;
    private int _lap;
    private int _nextSector;      // index of the sector line expected next
    private double _boundaryNanos; // when the current sector started (interpolated)
    private double _lapStartNanos;
    private readonly List<TimeSpan> _sectorTimes = new();
    private bool _lapInvalid;      // the pit-out line was crossed during this lap

    public LapDetector(Track track, TimeSpan? minLap = null, double maxAccuracy = 30)
    {
        _track = track;
        _minLap = minLap ?? TimeSpan.FromSeconds(10);
        _maxAccuracy = maxAccuracy;
    }

    public int CurrentLap => _lapRunning ? _lap : 0;

    public IReadOnlyList<TrackEvent> Process(GpsFix fix)
    {
        var events = new List<TrackEvent>();
        var startFinish = _track.StartFinish;
        if (fix.Accuracy > _maxAccuracy || startFinish == null) return events;

        var previous = _previous;
        _previous = fix;
        if (previous == null || fix.ElapsedNanos <= previous.ElapsedNanos) return events;

        // Leaving the pits spoils the lap in progress (an in-lap or out-lap): it is still timed, but reported as invalid.
        var pitOut = _track.PitOut;
        if (_lapRunning && !_lapInvalid && pitOut != null
            && GeoMath.Cross(previous.Point, fix.Point, pitOut.A, pitOut.B) is { } pitHit && pitOut.Accepts(pitHit.Side))
        {
            _lapInvalid = true;
            events.Add(new PitOutCrossed(_lap, fix.Utc));
        }

        // Sector lines first: a fix can only cross one thing sensibly, but the expected line wins over the finish.
        if (_lapRunning && _nextSector < _track.Sectors.Count)
        {
            var sector = _track.Sectors[_nextSector];
            var hit = GeoMath.Cross(previous.Point, fix.Point, sector.A, sector.B);
            if (hit != null && sector.Accepts(hit.Value.Side))
            {
                var hitT = hit.Value.T;
                var at = Interpolate(previous, fix, hitT);
                var time = Span(at - _boundaryNanos);
                _boundaryNanos = at;
                _sectorTimes.Add(time);
                _nextSector++;
                events.Add(new SectorCompleted(_lap, _nextSector, time, UtcAt(previous, fix, hitT)));
                return events;
            }
        }

        var crossingOrNull = GeoMath.Cross(previous.Point, fix.Point, startFinish.A, startFinish.B);
        if (crossingOrNull == null) return events;
        var crossing = crossingOrNull.Value;

        if (startFinish.Directed)
        {
            if (!startFinish.Accepts(crossing.Side)) return events;
        }
        else if (_direction == 0) _direction = crossing.Side;
        else if (crossing.Side != _direction) return events;

        var crossedAt = Interpolate(previous, fix, crossing.T);
        var crossedUtc = UtcAt(previous, fix, crossing.T);

        if (!_lapRunning)
        {
            _lapRunning = true;
            _lap = 1;
            StartLap(crossedAt);
            events.Add(new LapStarted(crossedUtc));
            return events;
        }

        var lapTime = Span(crossedAt - _lapStartNanos);
        if (lapTime < _minLap) return events;
        // A lap that skipped sector lines is still a lap, but its sectors are not trustworthy: report what was timed.
        _sectorTimes.Add(Span(crossedAt - _boundaryNanos));
        events.Add(new LapCompleted(_lap, lapTime, _sectorTimes.ToArray(), crossedUtc, !_lapInvalid));
        _lap++;
        StartLap(crossedAt);
        return events;
    }

    private void StartLap(double at)
    {
        _lapStartNanos = at;
        _boundaryNanos = at;
        _nextSector = 0;
        _lapInvalid = false;
        _sectorTimes.Clear();
    }

    private static double Interpolate(GpsFix a, GpsFix b, double t) => a.ElapsedNanos + t * (b.ElapsedNanos - a.ElapsedNanos);

    private static DateTime UtcAt(GpsFix a, GpsFix b, double t) => a.Utc + TimeSpan.FromTicks((long)(t * (b.Utc - a.Utc).Ticks));

    private static TimeSpan Span(double nanos) => TimeSpan.FromTicks((long)(nanos / 100.0));
}
