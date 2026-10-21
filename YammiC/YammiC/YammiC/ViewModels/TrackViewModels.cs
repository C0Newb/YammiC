using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CNCO.YccuSdk.Can;
using CNCO.YccuSdk.Tracks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YammiC.Controls;

namespace YammiC.ViewModels;

/// <summary>Base for the Track pages: follows the Track Timer service and refreshes on the UI thread.</summary>
public abstract class TrackViewModelBase : ViewModelBase
{
    protected TrackTimerService Service { get; }

    protected TrackViewModelBase(TrackTimerService service)
    {
        Service = service;
    }

    protected void Attach()
    {
        Service.Changed += HandleChanged;
        Refresh();
    }

    public virtual void Detach() => Service.Changed -= HandleChanged;

    private void HandleChanged() => Dispatcher.UIThread.Post(Refresh);

    protected abstract void Refresh();

    /// <summary>False while a session is on: nothing that changes a session's set-up may be touched.</summary>
    public bool IsUnlocked => !Service.Timer.IsLocked;
}

// -- Track list ------------------------------------------------------------

public partial class TrackRow : ObservableObject
{
    public TrackRow(Track track, bool selected, Action<TrackRow> select, Action<TrackRow> edit, Action<TrackRow> delete, Action<TrackRow> export)
    {
        Track = track;
        Title = track.Name;
        Detail = $"{track.SectorCount} sector{(track.SectorCount == 1 ? "" : "s")}" +
                 (track.LastUsed is { } used ? $" · last used {used.ToLocalTime():d MMM yyyy}" : "");
        IsSelected = selected;
        SelectCommand = new RelayCommand(() => select(this));
        EditCommand = new RelayCommand(() => edit(this));
        DeleteCommand = new RelayCommand(() => delete(this));
        ExportCommand = new RelayCommand(() => export(this));
    }

    public Track Track { get; }
    public string Title { get; }
    public string Detail { get; }
    public IRelayCommand SelectCommand { get; }
    public IRelayCommand EditCommand { get; }
    public IRelayCommand DeleteCommand { get; }
    public IRelayCommand ExportCommand { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public partial class TracksViewModel : TrackViewModelBase
{
    public ObservableCollection<TrackRow> Tracks { get; } = new();

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    /// <summary>Raised when a row asks to be edited or exported; the view handles navigation and file pickers.</summary>
    public event Action<Track>? EditRequested;

    public event Action<Track>? ExportRequested;

    public TracksViewModel(TrackTimerService service) : base(service)
    {
        Attach();
    }

    // Design-time constructor.
    public TracksViewModel() : this(App.TrackTimer)
    {
    }

    protected override void Refresh()
    {
        OnPropertyChanged(nameof(IsUnlocked));
        Tracks.Clear();
        foreach (var track in Service.Library.List())
        {
            Tracks.Add(new TrackRow(track, track.Id == Service.Timer.SelectedTrackId, Select, row => EditRequested?.Invoke(row.Track), Delete, row => ExportRequested?.Invoke(row.Track)));
        }
        if (Service.Library.Skipped.Count > 0) Status = $"{Service.Library.Skipped.Count} file(s) could not be read and were skipped.";
    }

    private void Select(TrackRow row)
    {
        if (!Service.SelectTrack(row.Track.Id)) Status = "A session is on: the track cannot be changed.";
        else Status = $"{row.Title} will be used for the next session.";
    }

    private void Delete(TrackRow row)
    {
        if (Service.Timer.IsLocked) return;
        Service.Library.Delete(row.Track.Id);
        if (Service.Timer.SelectedTrackId == row.Track.Id) Service.SelectTrack(null);
        Refresh();
    }

    public string Export(Track track) => Service.Library.Export(track);

    public void Import(string json)
    {
        Status = Service.Library.Import(json) is { } track ? $"Imported {track.Name}." : "That file is not a track.";
        Refresh();
    }
}

// -- Track editor ----------------------------------------------------------

public partial class TrackEditorViewModel : TrackViewModelBase
{
    private readonly Track _track;

    public TrackEditorViewModel(TrackTimerService service, Track? existing = null) : base(service)
    {
        _track = existing ?? new Track();
        Name = _track.Name;
        StartFinish = new MapLine { Label = "Start/Finish", IsStartFinish = true, A = _track.StartFinish?.A, B = _track.StartFinish?.B };
        Lines.Add(StartFinish);
        for (var i = 0; i < _track.Sectors.Count; i++)
            Lines.Add(new MapLine { Label = $"Sector {i + 1}", A = _track.Sectors[i].A, B = _track.Sectors[i].B });
        ActiveLine = StartFinish.A == null ? StartFinish : null;
        Attach();
    }

    // Design-time constructor.
    public TrackEditorViewModel() : this(App.TrackTimer)
    {
    }

    public List<MapLine> Lines { get; } = new();

    public MapLine StartFinish { get; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    /// <summary>The line the next taps on the map place points for.</summary>
    public MapLine? ActiveLine { get; set; }

    public bool CanAddSector => Lines.Count - 1 < Track.MaxSectorLines;

    /// <summary>Raised when the lines or the active line changed from here (the map redraws).</summary>
    public event Action? MapChanged;

    /// <summary>Where to start the map: the track, else the phone's position.</summary>
    public GeoPoint? InitialCenter =>
        _track.Center ?? (Service.Location.LastKnown is { } fix ? fix.Point : null);

    public GeoPoint? Here => Service.Location.LastKnown?.Point;

    protected override void Refresh()
    {
        OnPropertyChanged(nameof(IsUnlocked));
        OnPropertyChanged(nameof(CanAddSector));
    }

    [RelayCommand]
    private void RedrawStartFinish()
    {
        StartFinish.A = StartFinish.B = null;
        ActiveLine = StartFinish;
        Status = "Tap the map twice to place the start/finish line.";
        MapChanged?.Invoke();
    }

    [RelayCommand]
    private void AddSector()
    {
        if (!CanAddSector) return;
        var line = new MapLine { Label = $"Sector {Lines.Count}" };
        Lines.Add(line);
        ActiveLine = line;
        Status = $"Tap the map twice to place sector line {Lines.Count - 1}.";
        OnPropertyChanged(nameof(CanAddSector));
        MapChanged?.Invoke();
    }

    [RelayCommand]
    private void RemoveLastSector()
    {
        if (Lines.Count <= 1) return;
        var last = Lines[^1];
        Lines.RemoveAt(Lines.Count - 1);
        if (ActiveLine == last) ActiveLine = null;
        OnPropertyChanged(nameof(CanAddSector));
        MapChanged?.Invoke();
    }

    /// <summary>Saves the track. Returns true when it was saved (the view then goes back).</summary>
    public bool Save()
    {
        _track.Name = Name.Trim();
        _track.StartFinish = StartFinish is { A: { } a, B: { } b } ? new TrackLine(a, b) : null;
        _track.Sectors = Lines.Skip(1).Where(l => l.A != null && l.B != null).Select(l => new TrackLine(l.A!.Value, l.B!.Value)).ToList();
        var problem = Service.Library.Save(_track);
        Status = problem ?? "Saved.";
        if (problem == null) Service.NotifyChanged();
        return problem == null;
    }
}

// -- Timer -----------------------------------------------------------------

public partial class TrackTimerViewModel : TrackViewModelBase
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    public TrackTimerViewModel(TrackTimerService service) : base(service)
    {
        Attach();
        _tick.Tick += (_, _) => RefreshLocationText();
        _tick.Start();
    }

    // Design-time constructor.
    public TrackTimerViewModel() : this(App.TrackTimer)
    {
    }

    public override void Detach()
    {
        _tick.Stop();
        base.Detach();
    }

    public ObservableCollection<Track> Tracks { get; } = new();

    public ObservableCollection<string> Laps { get; } = new();

    private bool _refreshing;

    [ObservableProperty]
    public partial Track? SelectedTrack { get; set; }

    [ObservableProperty]
    public partial string StateText { get; set; } = "";

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    [ObservableProperty]
    public partial string LocationText { get; set; } = "";

    [ObservableProperty]
    public partial string BestText { get; set; } = "";

    [ObservableProperty]
    public partial bool CanArm { get; set; }

    [ObservableProperty]
    public partial bool IsLocked { get; set; }

    [ObservableProperty]
    public partial bool EndArmed { get; set; }

    public string EndText => EndArmed ? "Tap again to end the session" : "End session";

    protected override void Refresh()
    {
        _refreshing = true;
        try
        {
            OnPropertyChanged(nameof(IsUnlocked));
            var timer = Service.Timer;
            IsLocked = timer.IsLocked;
            if (!IsLocked) EndArmed = false;
            OnPropertyChanged(nameof(EndText));

            var tracks = Service.Library.List();
            if (!Tracks.Select(t => t.Id).SequenceEqual(tracks.Select(t => t.Id)))
            {
                Tracks.Clear();
                foreach (var t in tracks) Tracks.Add(t);
            }
            SelectedTrack = Tracks.FirstOrDefault(t => t.Id == timer.SelectedTrackId);

            StateText = timer.State switch
            {
                TimerState.Idle => "Not running",
                TimerState.Ready => "Ready: cross the start line to begin",
                _ => timer.CurrentLap > 0 ? $"Timing: lap {timer.CurrentLap}" : "Timing",
            };
            CanArm = !IsLocked && SelectedTrack != null;

            Laps.Clear();
            foreach (var lap in timer.Laps.Reverse()) Laps.Add($"Lap {lap.Lap}   {Format(lap.Time)}");
            BestText = timer.BestLap is { } best ? $"Best: lap {best.Lap}  {Format(best.Time)}" : "";
            RefreshLocationText();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshLocationText()
    {
        var location = Service.Location;
        LocationText = !location.HasPermission
            ? "Location permission is needed."
            : !location.HasBackgroundPermission
                ? "Location works while the app is open. For the bike to start a session on its own, allow location all the time."
                : Service.LastFix is { } fix ? $"GPS ready (accuracy {fix.Accuracy:0} m)" : "GPS ready.";
    }

    partial void OnSelectedTrackChanged(Track? value)
    {
        if (_refreshing || Service.Timer.IsLocked) return;
        Service.SelectTrack(value?.Id);
        CanArm = value != null;
    }

    [RelayCommand]
    private void Arm()
    {
        var problem = Service.Arm();
        Message = problem ?? "";
    }

    [RelayCommand]
    private void End()
    {
        // Ending a session is deliberate: the first tap only arms the button.
        if (!EndArmed)
        {
            EndArmed = true;
            OnPropertyChanged(nameof(EndText));
            return;
        }
        Service.Disarm();
    }

    [RelayCommand]
    private void AllowLocation() => Service.Location.RequestPermission(background: false);

    [RelayCommand]
    private void AllowLocationAlways() => Service.Location.RequestPermission(background: true);

    public static string Format(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds:000}";
}

// -- Track settings ----------------------------------------------------------

public partial class TrackSettingsViewModel : TrackViewModelBase
{
    private bool _applying;

    public TrackSettingsViewModel(TrackTimerService service) : base(service)
    {
        foreach (var info in CanSignals.Catalog) Signals.Add(new CanSignalOption(info, false));
        foreach (var option in Signals) option.PropertyChanged += (_, _) => ApplySignals();
        Attach();
    }

    // Design-time constructor.
    public TrackSettingsViewModel() : this(App.TrackTimer)
    {
    }

    public ObservableCollection<double> Rates { get; } = new();

    public ObservableCollection<CanSignalOption> Signals { get; } = new();

    public static IReadOnlyList<double> AutoSelectDistances { get; } = new[] { 100.0, 250.0, 500.0, 1000.0 };

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial double SelectedRate { get; set; }

    [ObservableProperty]
    public partial bool AutoSelect { get; set; }

    [ObservableProperty]
    public partial double AutoSelectMetres { get; set; }

    [ObservableProperty]
    public partial bool Vibrate { get; set; }

    [ObservableProperty]
    public partial string RateStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool IsTesting { get; set; }

    protected override void Refresh()
    {
        _applying = true;
        try
        {
            OnPropertyChanged(nameof(IsUnlocked));
            Enabled = Service.Enabled;
            AutoSelect = Service.AutoSelectTrack;
            AutoSelectMetres = AutoSelectDistances.Contains(Service.AutoSelectMetres) ? Service.AutoSelectMetres : 500;
            Vibrate = Service.VibrateOnReady;

            Rates.Clear();
            foreach (var rate in Service.AvailableRates) Rates.Add(rate);
            SelectedRate = Service.GpsHz;
            RateStatus = Service.TestedRates == null
                ? "Not tested yet: only 1 Hz is offered. Test with a clear view of the sky."
                : $"Your phone delivered: {string.Join(", ", Service.TestedRates.Select(r => r.ToString("0.#", CultureInfo.InvariantCulture)))} Hz";

            var recorded = Service.RecordedCanIds;
            foreach (var option in Signals) option.IsSelected = recorded.Contains(option.Id);
        }
        finally
        {
            _applying = false;
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (!_applying) Service.Enabled = value;
    }

    partial void OnSelectedRateChanged(double value)
    {
        if (!_applying && value > 0) Service.GpsHz = value;
    }

    partial void OnAutoSelectChanged(bool value)
    {
        if (!_applying) Service.AutoSelectTrack = value;
    }

    partial void OnAutoSelectMetresChanged(double value)
    {
        if (!_applying) Service.AutoSelectMetres = value;
    }

    partial void OnVibrateChanged(bool value)
    {
        if (!_applying) Service.VibrateOnReady = value;
    }

    private void ApplySignals()
    {
        if (_applying) return;
        Service.RecordedCanIds = Signals.Where(s => s.IsSelected).Select(s => s.Id).ToList();
    }

    [RelayCommand]
    private async Task TestRates()
    {
        if (IsTesting || Service.Timer.IsLocked) return;
        if (!Service.Location.HasPermission)
        {
            Service.Location.RequestPermission(background: false);
            RateStatus = "Allow location, then test again.";
            return;
        }
        IsTesting = true;
        RateStatus = "Testing 1, 2, 5 and 10 Hz for six seconds each...";
        try
        {
            await Service.TestGpsRatesAsync();
        }
        finally
        {
            IsTesting = false;
            Dispatcher.UIThread.Post(Refresh);
        }
    }
}

// -- Sessions --------------------------------------------------------------

public partial class SessionRow : ObservableObject
{
    public SessionRow(string file, string title, string detail, Action<SessionRow> export, Action<SessionRow> delete)
    {
        File = file;
        Title = title;
        Detail = detail;
        ExportCommand = new RelayCommand(() => export(this));
        DeleteCommand = new RelayCommand(() => delete(this));
    }

    public string File { get; }
    public string Title { get; }
    public string Detail { get; }
    public IRelayCommand ExportCommand { get; }
    public IRelayCommand DeleteCommand { get; }
}

public partial class SessionsViewModel : TrackViewModelBase
{
    private readonly ITrackFileStore _files;

    public SessionsViewModel(TrackTimerService service) : base(service)
    {
        _files = service.Files;
        Attach();
    }

    // Design-time constructor.
    public SessionsViewModel() : this(App.TrackTimer)
    {
    }

    public ObservableCollection<SessionRow> Sessions { get; } = new();

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    public event Action<string, string>? ExportRequested;

    protected override void Refresh()
    {
        Sessions.Clear();
        foreach (var file in _files.List(SessionWriter.Folder).OrderByDescending(n => n, StringComparer.Ordinal))
        {
            var records = _files.Read(SessionWriter.Folder, file) is { } text ? SessionWriter.Read(text) : Array.Empty<SessionRecord>();
            var header = records.FirstOrDefault(r => r.Type == "header");
            var laps = records.Where(r => r.Type == "lap").ToList();
            var best = laps.Count > 0 ? laps.Min(l => l.Seconds ?? double.MaxValue) : (double?)null;
            var title = header?.TrackName is { } name ? $"{name}  ·  {header.Utc.ToLocalTime():d MMM yyyy HH:mm}" : file;
            var detail = $"{laps.Count} lap{(laps.Count == 1 ? "" : "s")}" + (best is { } b ? $" · best {TrackTimerViewModel.Format(TimeSpan.FromSeconds(b))}" : "") +
                         $" · {records.Count(r => r.Type == "sample")} GPS points";
            Sessions.Add(new SessionRow(file, title, detail, row => ExportRequested?.Invoke(row.File, Gpx(row.File)), Delete));
        }
    }

    private string Gpx(string file) =>
        GpxExporter.Export(_files.Read(SessionWriter.Folder, file) is { } text ? SessionWriter.Read(text) : Array.Empty<SessionRecord>());

    private void Delete(SessionRow row)
    {
        if (Service.Timer.IsLocked && Service.Timer.SessionName == row.File) return;
        _files.Delete(SessionWriter.Folder, row.File);
        Refresh();
    }
}
