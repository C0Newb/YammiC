using System.Text.Json;
using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk.Tracks;

public enum TimerState
{
    Idle,
    /// <summary>
    /// Armed: the bike said ready (or the rider armed it) and GPS is being recorded, but the first line has not been crossed.
    /// </summary>
    Ready,
    /// <summary>
    /// The first start/finish crossing happened: laps are being timed.
    /// </summary>
    Timing,
}

public enum ArmSource
{
    Rider,
    Bike,
}

public interface IVibrator
{
    void Vibrate();
}

/// <summary>
/// Shows the track name on the board while armed. <see cref="ReleaseAsync"/> gives the board back to the normal screens.
/// </summary>
public interface IBoardMessageSender
{
    Task ShowTrackNameAsync(string trackName);

    Task ReleaseAsync();
}

/// <summary>
/// Sends a finished lap to the bike. <paramref name="best"/> is the best lap before this one, if any.
/// </summary>
public interface ILapSender
{
    Task SendAsync(LapCompleted lap, LapCompleted? best);
}

public sealed class TimerOptions
{
    public double GpsHz { get; set; } = 5;
    public TimeSpan MinLap { get; set; } = TimeSpan.FromSeconds(10);
    public bool VibrateOnReady { get; set; } = true;
    public IReadOnlyList<string> Signals { get; set; } = Array.Empty<string>();
}

/// <summary>
/// The track session: Idle → Ready → Timing → Idle. While Ready or Timing <see cref="IsLocked"/> is true and the track cannot be
/// changed. It never disarms because data went stale or the bike disconnected; only the rider, or the bike clearing the flag
/// while it armed the session, ends it.
/// </summary>
public sealed class TimerController
{
    private const string ArmedKey = "track_timer_armed";

    private readonly TrackLibrary _library;
    private readonly ITrackFileStore _files;
    private readonly IYmcKeyValueStore _store;
    private readonly IVibrator _vibrator;
    private readonly IBoardMessageSender _board;
    private readonly ILapSender _laps;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();

    private Track? _track;
    private LapDetector? _detector;
    private SessionWriter? _session;
    private ArmSource _source;
    private LapCompleted? _best;
    private readonly List<LapCompleted> _completed = new();

    public TimerController(
        TrackLibrary library,
        ITrackFileStore files,
        IYmcKeyValueStore store,
        IVibrator vibrator,
        IBoardMessageSender board,
        ILapSender laps,
        Func<DateTime>? utcNow = null)
    {
        _library = library;
        _files = files;
        _store = store;
        _vibrator = vibrator;
        _board = board;
        _laps = laps;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public TimerOptions Options { get; } = new();

    public TimerState State { get; private set; } = TimerState.Idle;

    /// <summary>
    /// True while armed or timing: nothing the rider could change is changeable.
    /// </summary>
    public bool IsLocked => State != TimerState.Idle;

    public string? SelectedTrackId { get; private set; }

    public string? SessionName => _session?.Name;

    public int CurrentLap => _detector?.CurrentLap ?? 0;

    public IReadOnlyList<LapCompleted> Laps
    {
        get
        {
            lock (_gate) return _completed.ToList();
        }
    }

    public LapCompleted? BestLap => _best;

    public event Action? Changed;

    /// <summary>
    /// Chooses the track for the next session. Returns false (and changes nothing) while locked or for an unknown track.
    /// </summary>
    public bool SelectTrack(string? id)
    {
        lock (_gate)
        {
            if (IsLocked) return false;
            if (id != null && _library.Get(id) == null) return false;
            SelectedTrackId = id;
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Starts a session on the selected track. Returns a reason when it cannot (no track, already armed).
    /// </summary>
    public string? Arm(ArmSource source, DateTime? resumeFrom = null, string? resumeSession = null)
    {
        Track track;
        lock (_gate)
        {
            if (IsLocked) return "Already armed.";
            var found = SelectedTrackId == null ? null : _library.Get(SelectedTrackId);
            if (found == null) return "Pick a track first.";
            track = found;
            _track = track;
            _source = source;
            _detector = new LapDetector(track, Options.MinLap);
            _completed.Clear();
            _best = null;
            var now = _utcNow();
            var name = resumeSession ?? $"{now:yyyyMMdd-HHmmss}-{Safe(track.Name)}.jsonl";
            _session = new SessionWriter(_files, name);
            if (resumeSession == null) _session.Header(now, track, Options.GpsHz, Options.Signals);
            else _session.Note(now, "Resumed after restart");
            State = TimerState.Ready;
            Persist();
        }

        _library.MarkUsed(track.Id);
        if (Options.VibrateOnReady) _vibrator.Vibrate();
        _ = SafeAsync(() => _board.ShowTrackNameAsync(track.Name));
        Changed?.Invoke();
        return null;
    }

    /// <summary>
    /// Ends the session (rider action).
    /// </summary>
    public void Disarm() => End("Ended by rider");

    /// <summary>
    /// The bike's ready flag changed. Only a session the bike armed is ended by the bike.
    /// </summary>
    public void OnReadyBit(ReadyBitState bit)
    {
        switch (bit)
        {
            case ReadyBitState.Ready when State == TimerState.Idle:
                Arm(ArmSource.Bike);
                break;
            case ReadyBitState.NotReady when State != TimerState.Idle && _source == ArmSource.Bike:
                End("Bike cleared ready");
                break;
        }
    }

    /// <summary>
    /// Feeds one GPS fix. Ignored while idle.
    /// </summary>
    public void OnFix(GpsFix fix, Dictionary<string, double>? can = null)
    {
        IReadOnlyList<TrackEvent> events;
        LapCompleted? best = null;
        LapCompleted? finished = null;
        lock (_gate)
        {
            if (State == TimerState.Idle || _detector == null || _session == null) return;
            _session.Sample(fix, can);
            events = _detector.Process(fix);
            foreach (var e in events)
            {
                _session.Event(e);
                if (e is LapStarted) State = TimerState.Timing;
                if (e is LapCompleted lap)
                {
                    best = _best;
                    finished = lap;
                    _completed.Add(lap);
                    if (lap.Valid && (_best == null || lap.Time < _best.Time)) _best = lap;
                }
            }
        }

        if (finished != null) _ = SafeAsync(() => _laps.SendAsync(finished, best));
        if (events.Count > 0) Changed?.Invoke();
    }

    /// <summary>
    /// After an app restart: continues a session that was armed. Pass whether location can be read from the background; if not, the
    /// session is ended cleanly (its file is kept).
    /// </summary>
    public void Restore(bool canLocate)
    {
        var json = _store.GetString(ArmedKey);
        if (string.IsNullOrEmpty(json) || IsLocked) return;
        Armed? armed;
        try
        {
            armed = JsonSerializer.Deserialize<Armed>(json);
        }
        catch (JsonException)
        {
            armed = null;
        }
        if (armed == null || armed.TrackId == null || armed.Session == null)
        {
            _store.SetString(ArmedKey, "");
            return;
        }

        if (!canLocate)
        {
            new SessionWriter(_files, armed.Session).Note(_utcNow(), "Ended: location unavailable after restart");
            _store.SetString(ArmedKey, "");
            return;
        }

        SelectedTrackId = armed.TrackId;
        Arm(armed.Source, resumeSession: armed.Session);
    }

    private void End(string reason)
    {
        lock (_gate)
        {
            if (State == TimerState.Idle) return;
            _session?.Note(_utcNow(), reason);
            State = TimerState.Idle;
            _detector = null;
            _store.SetString(ArmedKey, "");
        }

        _ = SafeAsync(_board.ReleaseAsync);
        Changed?.Invoke();
    }

    private void Persist() =>
        _store.SetString(ArmedKey, JsonSerializer.Serialize(new Armed { TrackId = _track?.Id, Session = _session?.Name, Source = _source }));

    private static string Safe(string name) =>
        new(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    private static async Task SafeAsync(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Board and lap messages are best effort; the session itself must carry on.
        }
    }

    private sealed class Armed
    {
        public string? TrackId { get; set; }
        public string? Session { get; set; }
        public ArmSource Source { get; set; }
    }
}
