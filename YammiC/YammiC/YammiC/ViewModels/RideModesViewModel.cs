using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CNCO.YccuSdk.Yrc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YammiC.Services;

namespace YammiC.ViewModels;

/// <summary>
/// One ride mode as the bike has it.
/// </summary>
public class BikeModeItem
{
    public BikeModeItem(YrcProfile profile, Action<BikeModeItem> save)
    {
        Profile = profile;
        Title = string.IsNullOrEmpty(profile.Name) ? YrcNames.Mode(profile.Mode) : $"{profile.Name}  ({YrcNames.Mode(profile.Mode)})";
        Detail = (YrcNames.IsEditable(profile) ? "" : "Built in: cannot be changed on the bike\n") + RideModesViewModel.Describe(profile.Values);
        SaveCommand = new RelayCommand(() => save(this));
    }

    public YrcProfile Profile { get; }
    public string Title { get; }
    public string Detail { get; }
    public IRelayCommand SaveCommand { get; }
}

/// <summary>
/// One ride mode in the phone's library.
/// </summary>
public partial class LibraryItem : ObservableObject
{
    public LibraryItem(SavedRideMode saved, Action<LibraryItem> send, Action<LibraryItem> delete, Action<LibraryItem> edit)
    {
        Saved = saved;
        Title = string.IsNullOrEmpty(saved.Label) ? saved.Name : saved.Label;
        Detail = $"{YrcNames.Mode(saved.Mode)}, shown as '{saved.Name}'\n{RideModesViewModel.Describe(saved.ToProfile().Values)}";
        SendCommand = new RelayCommand(() => send(this));
        DeleteCommand = new RelayCommand(() => delete(this));
        EditCommand = new RelayCommand(() => edit(this));
    }

    public SavedRideMode Saved { get; }
    public string Title { get; }
    public string Detail { get; }
    public IRelayCommand SendCommand { get; }
    public IRelayCommand DeleteCommand { get; }
    public IRelayCommand EditCommand { get; }

    /// <summary>
    /// Sending changes the bike, so the first tap only arms the button.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SendText))]
    public partial bool IsArmed { get; set; }

    public string SendText => IsArmed ? "Tap again to change the bike" : "Send to bike";
}

/// <summary>
/// Ride modes (the Custom and Track profiles): read from the bike, kept in a library, exported, imported, sent back.
/// </summary>
public partial class RideModesViewModel : BikeViewModelBase
{
    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public ObservableCollection<BikeModeItem> BikeModes { get; } = new();

    public ObservableCollection<LibraryItem> Library { get; } = new();

    public RideModesViewModel(IBikeController controller) : base(controller)
    {
        Controller.RideModesChanged += HandleRideModesChanged;
        Controller.ProfileLibrary.Changed += HandleLibraryChanged;
        Attach();
        RefreshBike();
        RefreshLibrary();
    }

    // Design-time constructor.
    public RideModesViewModel() : this(new FakeBikeController())
    {
    }

    public override void Detach()
    {
        base.Detach();
        Controller.RideModesChanged -= HandleRideModesChanged;
        Controller.ProfileLibrary.Changed -= HandleLibraryChanged;
    }

    protected override void OnSnapshot(BikeSnapshot snapshot)
    {
    }

    /// <summary>
    /// Short text for a list of settings: "PWR 3 (1-5), TCS 5 (0-9)".
    /// </summary>
    public static string Describe(IEnumerable<YrcValue> values) =>
        string.Join(", ", values.Select(v => v.Max > v.Min
            ? $"{YrcNames.Parameter(v.Kind)} {v.Current} ({v.Min}-{v.Max})"
            : $"{YrcNames.Parameter(v.Kind)} {v.Current}"));

    [RelayCommand]
    private async Task ReadFromBike()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "Reading from the bike...";
        try
        {
            Status = await Controller.ReadRideModesAsync();
        }
        catch (Exception e)
        {
            Status = $"Failed: {e.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SaveToLibrary(BikeModeItem item)
    {
        var p = item.Profile;
        var label = $"{(string.IsNullOrEmpty(p.Name) ? YrcNames.Mode(p.Mode) : p.Name)} {DateTime.Now:yyyy-MM-dd HH:mm}";
        Controller.ProfileLibrary.Add(p, label);
        Status = $"Saved '{label}' to the library";
    }

    private void Delete(LibraryItem item)
    {
        Controller.ProfileLibrary.Remove(item.Saved.Id);
        Status = $"Removed '{item.Title}'";
    }

    private async void Send(LibraryItem item)
    {
        if (IsBusy) return;

        if (!item.IsArmed)
        {
            var problem = Check(item);
            if (problem != null)
            {
                Status = problem;
                return;
            }
            item.IsArmed = true;
            Status = $"This replaces {YrcNames.Mode(item.Saved.Mode)} on the bike with '{item.Title}'. Tap again to do it.";
            _ = Task.Delay(6000).ContinueWith(_ => Dispatcher.UIThread.Post(() => item.IsArmed = false));
            return;
        }

        item.IsArmed = false;
        var again = Check(item); // the bike's data may have changed while the button was armed
        if (again != null)
        {
            Status = again;
            return;
        }

        IsBusy = true;
        Status = "Sending to the bike...";
        try
        {
            Status = await Controller.SendRideModesAsync(new[] { item.Saved.ToProfile() });
        }
        catch (Exception e)
        {
            Status = $"Failed: {e.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Why this entry must not be sent now, or null.
    /// </summary>
    private string? Check(LibraryItem item)
    {
        var own = ProfileLibrary.Check(item.Saved);
        if (own != null) return $"Not sent: {own}";
        if (Controller.BikeProfiles.Count == 0) return "Read the ride modes from the bike first, so the values can be checked against what it allows.";
        var problems = ProfileLibrary.CheckAgainstBike(item.Saved.ToProfile(), Controller.BikeProfiles);
        return problems.Count == 0 ? null : "Not sent: " + string.Join("; ", problems);
    }

    /// <summary>
    /// The whole library as the contents of a file.
    /// </summary>
    public string ExportJson() => Controller.ProfileLibrary.Export();

    /// <summary>
    /// Adds the ride modes of a file to the library.
    /// </summary>
    public void ImportJson(string json)
    {
        Controller.ProfileLibrary.Import(json, out var message);
        Status = message;
    }

    private void HandleRideModesChanged() => Dispatcher.UIThread.Post(RefreshBike);

    private void HandleLibraryChanged() => Dispatcher.UIThread.Post(RefreshLibrary);

    private void RefreshBike()
    {
        BikeModes.Clear();
        foreach (var p in Controller.BikeProfiles.OrderBy(p => p.Mode)) BikeModes.Add(new BikeModeItem(p, SaveToLibrary));
    }

    private void RefreshLibrary()
    {
        Library.Clear();
        foreach (var s in Controller.ProfileLibrary.Items) Library.Add(new LibraryItem(s, Send, Delete, item => EditRequested?.Invoke(item.Saved)));
    }

    /// <summary>
    /// Raised when a library entry should be edited; the view opens the editor page.
    /// </summary>
    public event Action<SavedRideMode>? EditRequested;
}
