using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CNCO.YccuSdk.Yrc;
using CommunityToolkit.Mvvm.ComponentModel;
using YammiC.Services;

namespace YammiC.ViewModels;

/// <summary>
/// One setting of a ride mode, with the range the bike allows.
/// </summary>
public partial class RideValueRow : ObservableObject
{
    public RideValueRow(SavedRideValue value)
    {
        Kind = value.Kind;
        Title = YrcNames.Parameter(value.Kind);
        var meaning = YrcNames.Meaning(value.Kind);
        Detail = value.Max > value.Min
            ? (meaning.Length > 0 ? $"{meaning}, {value.Min} to {value.Max}" : $"{value.Min} to {value.Max}")
            : meaning;
        Min = value.Max > value.Min ? value.Min : -1000;
        Max = value.Max > value.Min ? value.Max : 1000;
        Value = value.Current;
    }

    public int Kind { get; }
    public string Title { get; }
    public string Detail { get; }
    public decimal Min { get; }
    public decimal Max { get; }

    [ObservableProperty]
    public partial decimal Value { get; set; }
}

/// <summary>
/// Edits one ride mode in the library: its name on the bike and the value of each setting (within the bike's ranges).
/// </summary>
public partial class RideModeEditorViewModel : ViewModelBase
{
    private readonly ProfileLibrary _library;
    private readonly string _id;

    public RideModeEditorViewModel(ProfileLibrary library, SavedRideMode item)
    {
        _library = library;
        _id = item.Id;
        Label = item.Label;
        BikeName = item.Name.Trim();
        ModeText = $"{YrcNames.Mode(item.Mode)}";
        foreach (var v in item.Values) Rows.Add(new RideValueRow(v));
    }

    // Design-time constructor.
    public RideModeEditorViewModel() : this(new ProfileLibrary(new MemoryKeyValueStore()), SampleItem())
    {
    }

    private static SavedRideMode SampleItem() => new()
    {
        Label = "Track 1 sample",
        Mode = 28,
        Name = "TRACK 1",
        Values = new List<SavedRideValue>
        {
            new() { Kind = 1, Current = 1, Min = 1, Max = 4 },
            new() { Kind = 2, Current = 3, Min = 0, Max = 9 },
        },
    };

    public ObservableCollection<RideValueRow> Rows { get; } = new();

    public string ModeText { get; }

    [ObservableProperty]
    public partial string Label { get; set; } = "";

    /// <summary>
    /// The name shown on the bike; at most 10 characters.
    /// </summary>
    [ObservableProperty]
    public partial string BikeName { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    /// <summary>
    /// Saves the changes to the library entry (nothing is sent to the bike). Returns true when saved.
    /// </summary>
    public bool Save()
    {
        var ok = _library.Update(_id, Label.Trim(), BikeName.Trim(), Rows.ToDictionary(r => r.Kind, r => (int)r.Value));
        Status = ok ? "Saved to the library. Send it from the ride modes page." : "That entry no longer exists.";
        return ok;
    }
}
