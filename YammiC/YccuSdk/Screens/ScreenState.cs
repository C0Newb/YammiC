using System.ComponentModel;
using CNCO.YccuSdk.Models;

namespace CNCO.YccuSdk.Screens;

/// <summary>
/// A dictionary a page can bind to (<c>{Binding Flags[menu]}</c>): missing keys read as the default value.
/// </summary>
public sealed class StateBag<T> : INotifyPropertyChanged
{
    private IReadOnlyDictionary<string, T> _values = new Dictionary<string, T>();

    public event PropertyChangedEventHandler? PropertyChanged;

    public T this[string key] => _values.TryGetValue(key, out var v) ? v : default!;

    public void Replace(IReadOnlyDictionary<string, T> values)
    {
        _values = values;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}

/// <summary>
/// What pages bind to (<c>{Binding Song}</c>, <c>{Binding IsPlaying}</c>, ...). Updated with <see cref="Apply"/> on the thread
/// that owns the controls; every update raises "all properties changed".
/// </summary>
public sealed class ScreenState : INotifyPropertyChanged
{
    private ScreenSnapshot _snapshot = ScreenSnapshot.Of(new ScreenContext(DateTime.Now, null, false));

    public event PropertyChangedEventHandler? PropertyChanged;

    public StateBag<bool> Flags { get; } = new();
    public StateBag<string> Vars { get; } = new();

    public void Apply(ScreenSnapshot snapshot)
    {
        _snapshot = snapshot;
        Flags.Replace(snapshot.Flags);
        Vars.Replace(snapshot.Vars);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    /// <summary>
    /// Expands <c>{variables}</c> in <paramref name="template"/> (see <see cref="ScreenVariables"/>).
    /// </summary>
    public string Resolve(string? template) => ScreenVariables.Resolve(template, _snapshot.Context);

    private ScreenContext C => _snapshot.Context;
    private YmcMediaMetaData? Media => C.HasMusic ? C.Media : null;
    private string V(string name) => ScreenVariables.Resolve("{" + name + "}", C);

    public DateTime Now => C.Now;
    public string Time => V("time");
    public string Date => V("date");
    public string Weekday => V("weekday");
    public string Song => V("song");
    public string Artist => V("artist");
    public string Album => V("album");
    public string State => V("state");
    public string PlayIcon => V("playicon");
    public string Elapsed => V("elapsed");
    public string Duration => V("duration");
    public double Progress
    {
        get
        {
            var media = Media;
            if (media == null || media.DurationSeconds <= 0) return 0;
            return Math.Clamp((double)media.ElapsedSeconds / media.DurationSeconds, 0, 1);
        }
    }

    public bool Night => C.Night;
    public bool Day => !C.Night;
    public bool HasMusic => C.HasMusic;
    public bool NoMusic => !C.HasMusic;
    public bool IsPlaying => C.IsPlaying;
    public bool IsPaused => C.HasMusic && !C.IsPlaying;

    public string PageId => _snapshot.PageId;
    public int PageIndex => _snapshot.PageIndex;
    public int PageNumber => _snapshot.PageIndex + 1;
    public int PageCount => _snapshot.PageCount;

    public string Rpm => V("rpm");
    public string Water => V("water");
    public string Intake => V("intake");
    public string Gear => V("gear");
    public string Tps => V("tps");
    public string Aps => V("aps");
    public string Bank => V("bank");
    public string Pitch => V("pitch");
    public string AccelX => V("accelx");
    public string AccelY => V("accely");
    public string FrontSpeed => V("frontspeed");
    public string RearSpeed => V("rearspeed");

    /// <summary>
    /// The current track's picture (encoded image bytes), or null.
    /// </summary>
    public byte[]? AlbumArt => C.AlbumArt;

    public bool HasAlbumArt => C.AlbumArt != null && C.AlbumArt.Length > 0;
}
