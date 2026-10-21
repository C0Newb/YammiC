using CNCO.YccuSdk.Bike;
using CNCO.YccuSdk.Can;
using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk.Tracks;

/// <summary>
/// The Track Timer as the app sees it: settings, the track library, GPS, the bike's "ready" flag and the session itself
/// (<see cref="Timer"/>). While a session is on, the settings cannot be changed.
/// </summary>
public sealed class TrackTimerService
{
    private const string EnabledKey = "track_enabled";
    private const string HzKey = "track_hz";
    private const string RatesKey = "track_rates";
    private const string AutoSelectKey = "track_autoselect";
    private const string AutoSelectMetresKey = "track_autoselect_m";
    private const string VibrateKey = "track_vibrate";
    private const string RecordIdsKey = "track_record_ids";
    private const string SelectedKey = "track_selected";

    private readonly ITrackBikeLink _bike;
    private readonly ILocationSource _location;
    private readonly IYmcKeyValueStore _storage;
    private readonly Logger _log;
    private ReadyBitState _lastBit = ReadyBitState.Unknown;
    private string _lastRaw = "";
    private bool _started;

    public TrackTimerService(
        ITrackBikeLink bike,
        ITrackFileStore files,
        ILocationSource location,
        IVibrator vibrator,
        IYmcKeyValueStore storage,
        Logger logger,
        Func<DateTime>? utcNow = null)
    {
        _bike = bike;
        _location = location;
        _storage = storage;
        _log = logger.Section("Track timer");
        Files = files;
        Library = new TrackLibrary(files, utcNow);
        Timer = new TimerController(Library, files, storage, vibrator, new LinkBoardSender(bike), bike.LapSender, utcNow);
    }

    public TrackLibrary Library { get; }

    /// <summary>
    /// The track and session files (sessions are read from here for the session list and exports).
    /// </summary>
    public ITrackFileStore Files { get; }

    public TimerController Timer { get; }

    public ILocationSource Location => _location;

    /// <summary>
    /// The most recent GPS fix received while a session is on.
    /// </summary>
    public GpsFix? LastFix { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// Loads settings, starts listening for the bike's ready flag and resumes a session that was on when the app stopped.
    /// </summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        var selected = _storage.GetString(SelectedKey);
        if (!string.IsNullOrEmpty(selected)) Timer.SelectTrack(selected);
        ApplyOptions();
        ApplyRequiredIds();
        _bike.CanUpdated += OnCanUpdated;
        Timer.Changed += OnTimerChanged;

        Timer.Restore(_location.HasBackgroundPermission);
        if (Timer.IsLocked) _location.Start(GpsHz, OnFix);
    }

    /// <summary>
    /// Tells the pages that tracks or sessions changed (the library does not raise it itself).
    /// </summary>
    public void NotifyChanged() => Changed?.Invoke();

    // -- Settings ---------------------------------------------------------

    /// <summary>
    /// Whether the Track Timer listens for the bike's ready flag (adds one CAN ID to the request).
    /// </summary>
    public bool Enabled
    {
        get => _storage.GetString(EnabledKey) == "1";
        set
        {
            if (Timer.IsLocked || Enabled == value) return;
            _storage.SetString(EnabledKey, value ? "1" : "0");
            ApplyRequiredIds();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// GPS fixes per second. Only rates the phone delivers are accepted once they have been tested.
    /// </summary>
    public double GpsHz
    {
        get => double.TryParse(_storage.GetString(HzKey), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hz) && AvailableRates.Contains(hz) ? hz : AvailableRates.Contains(5) ? 5 : AvailableRates[0];
        set
        {
            if (Timer.IsLocked || !AvailableRates.Contains(value)) return;
            _storage.SetString(HzKey, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ApplyOptions();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// The rates found by <see cref="TestGpsRatesAsync"/>, or null if the test has not been run.
    /// </summary>
    public IReadOnlyList<double>? TestedRates
    {
        get
        {
            var text = _storage.GetString(RatesKey);
            if (string.IsNullOrEmpty(text)) return null;
            return text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        }
    }

    /// <summary>
    /// What the rate setting offers: the tested rates, or just 1 Hz until the test has run.
    /// </summary>
    public IReadOnlyList<double> AvailableRates
    {
        get
        {
            var tested = TestedRates;
            return tested != null && tested.Count > 0 ? tested : new[] { 1.0 };
        }
    }

    public bool AutoSelectTrack
    {
        get => _storage.GetString(AutoSelectKey) != "0";
        set
        {
            if (Timer.IsLocked) return;
            _storage.SetString(AutoSelectKey, value ? "1" : "0");
            Changed?.Invoke();
        }
    }

    public double AutoSelectMetres
    {
        get => double.TryParse(_storage.GetString(AutoSelectMetresKey), out var m) && m > 0 ? m : 500;
        set
        {
            if (Timer.IsLocked || value <= 0) return;
            _storage.SetString(AutoSelectMetresKey, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Changed?.Invoke();
        }
    }

    public bool VibrateOnReady
    {
        get => _storage.GetString(VibrateKey) != "0";
        set
        {
            if (Timer.IsLocked) return;
            _storage.SetString(VibrateKey, value ? "1" : "0");
            ApplyOptions();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// CAN signals saved with each GPS sample (a subset of <see cref="CanSignals.KnownIds"/>; none by default).
    /// </summary>
    public IReadOnlyList<int> RecordedCanIds
    {
        get
        {
            var text = _storage.GetString(RecordIdsKey);
            if (string.IsNullOrEmpty(text)) return Array.Empty<int>();
            return text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out var id) ? id : -1).Where(CanSignals.KnownIds.Contains).ToList();
        }
        set
        {
            if (Timer.IsLocked) return;
            var ids = value.Where(CanSignals.KnownIds.Contains).Distinct().OrderBy(i => i).ToList();
            _storage.SetString(RecordIdsKey, string.Join(",", ids));
            ApplyOptions();
            ApplyRequiredIds();
            Changed?.Invoke();
        }
    }

    // -- Actions ----------------------------------------------------------

    /// <summary>
    /// Chooses the track for the next session (ignored while a session is on). Null clears the choice.
    /// </summary>
    public bool SelectTrack(string? id)
    {
        if (!Timer.SelectTrack(id)) return false;
        _storage.SetString(SelectedKey, id ?? "");
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Starts a session from the app. Returns why not, or null.
    /// </summary>
    public string? Arm() => TryArm(ArmSource.Rider);

    public void Disarm() => Timer.Disarm();

    /// <summary>
    /// Times each GPS rate for a few seconds and remembers which ones the phone delivers. Needs location permission and a view of the sky.
    /// </summary>
    public async Task<IReadOnlyList<double>> TestGpsRatesAsync(CancellationToken cancellation = default)
    {
        if (Timer.IsLocked || !_location.HasPermission) return AvailableRates;
        var rates = await GpsRateProbe.ProbeAsync(_location, TimeSpan.FromSeconds(6), cancellation).ConfigureAwait(false);
        if (rates.Count > 0)
        {
            _storage.SetString(RatesKey, string.Join(",", rates.Select(r => r.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            _log.Info($"GPS rates supported: {string.Join(", ", rates)} Hz");
        }
        else
        {
            _log.Info("GPS rate test found no fixes");
        }
        Changed?.Invoke();
        return AvailableRates;
    }

    // -- Internals --------------------------------------------------------

    private string? TryArm(ArmSource source)
    {
        if (!_location.HasPermission) return "Location permission is needed.";
        ApplyOptions();
        var problem = Timer.Arm(source);
        if (problem != null) return problem;
        _location.Start(GpsHz, OnFix);
        _log.Info($"Session started ({source}) at {GpsHz} Hz");
        return null;
    }

    private void ApplyOptions()
    {
        Timer.Options.GpsHz = GpsHz;
        Timer.Options.VibrateOnReady = VibrateOnReady;
        Timer.Options.Signals = RecordedCanIds.Select(id => CanSignals.Catalog.FirstOrDefault(c => c.Id == id)?.Name ?? $"0x{id:X4}").ToList();
    }

    private void ApplyRequiredIds()
    {
        var ids = new List<int>();
        if (Enabled)
        {
            ids.Add(ReadyBit.CanId);
            ids.AddRange(RecordedCanIds);
        }
        _bike.SetRequiredCanIds(ids);
    }

    private void OnTimerChanged()
    {
        if (Timer.State == TimerState.Idle) _location.Stop();
        Changed?.Invoke();
    }

    private void OnFix(GpsFix fix)
    {
        LastFix = fix;
        Timer.OnFix(fix, Timer.Options.Signals.Count > 0 ? CanSample() : null);
    }

    private Dictionary<string, double> CanSample()
    {
        var v = _bike.CanValues();
        var sample = new Dictionary<string, double>();
        void Add(string name, double? value)
        {
            if (value != null) sample[name] = value.Value;
        }
        Add("rpm", v.Rpm);
        Add("gear", v.Gear);
        Add("water", v.WaterTempC);
        Add("intake", v.IntakeAirTempC);
        Add("tps", v.Tps);
        Add("aps", v.Aps);
        Add("bank", v.BankAngle);
        Add("pitch", v.PitchRate);
        Add("accx", v.AccelX);
        Add("accy", v.AccelY);
        Add("front", v.FrontWheelSpeed);
        Add("rear", v.RearWheelSpeed);
        return sample;
    }

    private void OnCanUpdated()
    {
        if (!Enabled) return;
        var raw = _bike.RawCan();

        // Log the flag's bytes whenever they change, so the ID can be checked against the bike.
        var reading = raw.FirstOrDefault(r => r.Id == ReadyBit.CanId);
        var text = reading == null ? "" : Convert.ToHexString(reading.Data);
        if (text != _lastRaw)
        {
            _lastRaw = text;
            if (text.Length > 0) _log.Info($"0x{ReadyBit.CanId:X4} = {text}");
        }

        var bit = ReadyBit.Read(raw, DateTime.UtcNow);
        if (bit == ReadyBitState.Unknown || bit == _lastBit) return;
        _lastBit = bit;
        _log.Info($"Bike ready flag: {bit}");
        HandleReady(bit);
    }

    private void HandleReady(ReadyBitState bit)
    {
        if (bit == ReadyBitState.Ready && Timer.State == TimerState.Idle)
        {
            var here = _location.LastKnown;
            if (AutoSelectTrack && here != null)
            {
                var nearest = TrackSelector.Select(Library.List(), here.Point, AutoSelectMetres);
                if (nearest != null) SelectTrack(nearest.Id);
            }

            var problem = TryArm(ArmSource.Bike);
            if (problem != null) _log.Info($"Bike said ready but the session did not start: {problem}");
        }
        else if (bit == ReadyBitState.NotReady)
        {
            Timer.OnReadyBit(bit);
        }
    }
}
