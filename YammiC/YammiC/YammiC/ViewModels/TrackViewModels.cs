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

/// <summary>
/// Base for the Track pages: follows the Track Timer service and refreshes on the UI thread.
/// </summary>
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

    /// <summary>
    /// False while a session is on: nothing that changes a session's set-up may be touched.
    /// </summary>
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
                 (track.LastUsed != null ? $" · last used {track.LastUsed.Value.ToLocalTime():d MMM yyyy}" : "");
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

    /// <summary>
    /// Raised when a row asks to be edited or exported; the view handles navigation and file pickers.
    /// </summary>
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
        var track = Service.Library.Import(json);
        Status = track != null ? $"Imported {track.Name}." : "That file is not a track.";
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
        if (_track.StartFinish != null) Lines.Add(new MapLine { Label = "Start/finish", Kind = MapLineKind.StartFinish, A = _track.StartFinish.A, B = _track.StartFinish.B });
        foreach (var sector in _track.Sectors)
            Lines.Add(new MapLine { Kind = MapLineKind.Sector, A = sector.A, B = sector.B });
        if (_track.PitOut != null) Lines.Add(new MapLine { Kind = MapLineKind.PitOut, A = _track.PitOut.A, B = _track.PitOut.B });
        Renumber();
        if (_track.StartFinish == null) AddStartFinish();
        else Status = "Tap a line to select it, then drag its handles, flip its direction or delete it.";
        Attach();
    }

    // Design-time constructor.
    public TrackEditorViewModel() : this(App.TrackTimer)
    {
    }

    public List<MapLine> Lines { get; } = new();

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    /// <summary>The line the next taps on the map place points for.</summary>
    public MapLine? ActiveLine { get; set; }

    /// <summary>The line the buttons below the map act on.</summary>
    public MapLine? Selected { get; private set; }

    private IEnumerable<MapLine> SectorLines => Lines.Where(l => l.Kind == MapLineKind.Sector);

    private MapLine? StartFinishLine => Lines.FirstOrDefault(l => l.Kind == MapLineKind.StartFinish);

    private MapLine? PitOutLine => Lines.FirstOrDefault(l => l.Kind == MapLineKind.PitOut);

    public bool CanAddStartFinish => StartFinishLine == null;

    public bool CanAddSector => SectorLines.Count() < Track.MaxSectorLines;

    public bool CanAddPitOut => PitOutLine == null;

    public bool HasSelection => Selected != null;

    public bool HasCompleteSelection => Selected is { A: not null, B: not null };

    public string SelectedText => Selected == null ? "Tap a line to select it" : Selected.Label;

    /// <summary>Raised when the lines or the active line changed from here (the map redraws).</summary>
    public event Action? MapChanged;

    /// <summary>Raised when the selection was changed from here (the map highlights it).</summary>
    public event Action? SelectionRequested;

    /// <summary>Where to start the map: the track, else the phone's position.</summary>
    public GeoPoint? InitialCenter
    {
        get
        {
            if (_track.Center != null) return _track.Center;
            var fix = Service.Location.LastKnown;
            return fix != null ? fix.Point : null;
        }
    }

    public GpsFix? Fix => Service.Location.LastKnown;

    public GeoPoint? Here => Fix?.Point;

    protected override void Refresh()
    {
        OnPropertyChanged(nameof(IsUnlocked));
        RefreshLines();
    }

    private void RefreshLines()
    {
        OnPropertyChanged(nameof(CanAddStartFinish));
        OnPropertyChanged(nameof(CanAddSector));
        OnPropertyChanged(nameof(CanAddPitOut));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasCompleteSelection));
        OnPropertyChanged(nameof(SelectedText));
    }

    /// <summary>The map tells us which line the rider touched, or a line it just finished placing.</summary>
    public void OnMapSelected(MapLine? line)
    {
        Selected = line;
        RefreshLines();
    }

    // Sector lines are numbered in the order they are listed.
    private void Renumber()
    {
        var number = 0;
        foreach (var line in Lines)
        {
            line.Number = line.Kind == MapLineKind.Sector ? ++number : 0;
            line.Label = line.Kind switch
            {
                MapLineKind.StartFinish => "Start/finish",
                MapLineKind.PitOut => "Pit-out line",
                _ => $"Sector line {number}",
            };
        }
    }

    // A new line is selected and waits for its two taps. Adding another line never needs a save: it only ever changes this list.
    private void Begin(MapLine line, string what)
    {
        ActiveLine = line;
        Selected = line;
        Status = $"Tap the map twice to place the {what}.";
        RefreshLines();
        MapChanged?.Invoke();
        SelectionRequested?.Invoke();
    }

    [RelayCommand]
    private void AddStartFinish()
    {
        if (!CanAddStartFinish) return;
        var line = new MapLine { Kind = MapLineKind.StartFinish };
        Lines.Insert(0, line);
        Renumber();
        Begin(line, "start/finish line");
    }

    [RelayCommand]
    private void AddSector()
    {
        if (!CanAddSector) return;
        var line = new MapLine { Kind = MapLineKind.Sector };
        // Sector lines stay in order ahead of the pit-out line.
        var pit = PitOutLine;
        Lines.Insert(pit != null ? Lines.IndexOf(pit) : Lines.Count, line);
        Renumber();
        Begin(line, line.Label.ToLowerInvariant());
    }

    [RelayCommand]
    private void AddPitOut()
    {
        if (!CanAddPitOut) return;
        var line = new MapLine { Kind = MapLineKind.PitOut };
        Lines.Add(line);
        Renumber();
        Begin(line, "pit-out line (a lap that crosses it is marked invalid)");
    }

    /// <summary>Reverses the direction the selected line has to be crossed in.</summary>
    [RelayCommand]
    private void FlipSelected()
    {
        if (Selected is not { A: { } a, B: { } b } line) return;
        line.A = b;
        line.B = a;
        Status = $"{line.Label}: direction reversed.";
        MapChanged?.Invoke();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (Selected is not { } line) return;
        Lines.Remove(line);
        if (ActiveLine == line) ActiveLine = null;
        Selected = null;
        Renumber();
        Status = $"{line.Label} removed.";
        RefreshLines();
        MapChanged?.Invoke();
        SelectionRequested?.Invoke();
    }

    // A line that has both ends placed, or null. Every line is saved with its direction: the arrow on the map is what counts.
    private static TrackLine? ToTrackLine(MapLine? line)
    {
        if (line == null || line.A == null || line.B == null) return null;
        return new TrackLine(line.A.Value, line.B.Value, Directed: true);
    }

    /// <summary>Saves the track. Returns true when it was saved (the view then goes back).</summary>
    public bool Save()
    {
        _track.Name = Name.Trim();
        _track.StartFinish = ToTrackLine(StartFinishLine);
        _track.Sectors = SectorLines.Select(ToTrackLine).OfType<TrackLine>().ToList();
        _track.PitOut = ToTrackLine(PitOutLine);
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
            foreach (var lap in timer.Laps.Reverse()) Laps.Add($"Lap {lap.Lap}   {Format(lap.Time)}{(lap.Valid ? "" : "   invalid (pit out)")}");
            var best = timer.BestLap;
            BestText = best != null ? $"Best: lap {best.Lap}  {Format(best.Time)}" : "";
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
        var fix = Service.LastFix;
        if (!location.HasPermission)
            LocationText = "Location permission is needed.";
        else if (!location.HasBackgroundPermission)
            LocationText = "Location works while the app is open. For the bike to start a session on its own, allow location all the time.";
        else
            LocationText = fix != null ? $"GPS ready (accuracy {fix.Accuracy:0} m)" : "GPS ready.";
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
            var records = ReadRecords(file);
            var header = records.FirstOrDefault(r => r.Type == "header");
            var laps = records.Where(r => r.Type == "lap").ToList();
            var best = laps.Count > 0 ? laps.Min(l => l.Seconds ?? double.MaxValue) : (double?)null;
            var title = header?.TrackName != null ? $"{header.TrackName}  ·  {header.Utc.ToLocalTime():d MMM yyyy HH:mm}" : file;
            var detail = $"{laps.Count} lap{(laps.Count == 1 ? "" : "s")}" + (best != null ? $" · best {TrackTimerViewModel.Format(TimeSpan.FromSeconds(best.Value))}" : "") +
                         $" · {records.Count(r => r.Type == "sample")} GPS points";
            Sessions.Add(new SessionRow(file, title, detail, row => ExportRequested?.Invoke(row.File, Gpx(row.File)), Delete));
        }
    }

    private IReadOnlyList<SessionRecord> ReadRecords(string file)
    {
        var text = _files.Read(SessionWriter.Folder, file);
        return text != null ? SessionWriter.Read(text) : Array.Empty<SessionRecord>();
    }

    private string Gpx(string file) => GpxExporter.Export(ReadRecords(file));

    private void Delete(SessionRow row)
    {
        if (Service.Timer.IsLocked && Service.Timer.SessionName == row.File) return;
        _files.Delete(SessionWriter.Folder, row.File);
        Refresh();
    }
}
