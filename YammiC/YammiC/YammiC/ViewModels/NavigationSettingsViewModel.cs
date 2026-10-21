using System.Collections.Generic;
using CNCO.YccuSdk.Navigation.Protocol;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using YammiC.Services;

namespace YammiC.ViewModels;

public partial class NavigationSettingsViewModel : BikeViewModelBase
{
    private bool _applying;

    [ObservableProperty]
    public partial bool NavigationEnabled { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    public NavigationSettingsViewModel(IBikeController controller) : base(controller)
    {
        Attach();
    }

    // Design-time constructor.
    public NavigationSettingsViewModel() : this(new FakeBikeController())
    {
    }

    // -- Turn-by-turn test -----------------------------------------------

    public record TurnChoice(string Name, TurnIcon Icon)
    {
        public override string ToString() => Name;
    }

    public static IReadOnlyList<TurnChoice> TurnChoices { get; } = new TurnChoice[]
    {
        new("Turn right", TurnIcon.Right),
        new("Turn left", TurnIcon.Left),
        new("Sharp right", TurnIcon.SharpRight),
        new("Sharp left", TurnIcon.SharpLeft),
        new("Keep right", TurnIcon.KeepRight),
        new("Keep left", TurnIcon.KeepLeft),
        new("U-turn", TurnIcon.UTurnLeft),
        new("Roundabout", TurnIcon.Roundabout),
        new("Exit right", TurnIcon.ExitRight),
        new("Exit left", TurnIcon.ExitLeft),
        new("Continue", TurnIcon.Continue),
        new("Arrive", TurnIcon.Arriving),
    };

    [ObservableProperty]
    public partial TurnChoice SelectedTurn { get; set; } = TurnChoices[0];

    /// <summary>Distance to the turn in feet (imperial) or metres (metric).</summary>
    [ObservableProperty]
    public partial decimal TurnDistance { get; set; } = 500;

    [ObservableProperty]
    public partial bool TurnMetric { get; set; }

    [ObservableProperty]
    public partial string TurnRoad { get; set; } = "Main St";

    [ObservableProperty]
    public partial string TurnStatus { get; set; } = "";

    [RelayCommand]
    private void SendTurn()
    {
        var small = (double)TurnDistance;
        var turn = new TurnGuidance(
            SelectedTurn.Icon,
            TurnMetric ? small : small * 0.3048,
            TurnRoad.Trim(),
            TurnMetric ? DistanceUnit.Metric : DistanceUnit.Imperial);
        TurnStatus = Controller.SendTurnGuidance(turn)
            ? $"Sent: {turn.Describe()}"
            : "Not sent: the navigation link is not up. Open the navigation screen on the bike first.";
    }

    [RelayCommand]
    private void ClearTurn() =>
        TurnStatus = Controller.ClearTurnGuidance() ? "Cleared." : "Not sent: the navigation link is not up.";

    protected override void OnSnapshot(BikeSnapshot snapshot)
    {
        _applying = true;
        try
        {
            NavigationEnabled = snapshot.NavigationEnabled;
            StatusText = $"Status: {NavigationStateFormatter.Describe(snapshot.Navigation)}";
        }
        finally
        {
            _applying = false;
        }
    }

    partial void OnNavigationEnabledChanged(bool value)
    {
        if (!_applying) Controller.SetNavigationEnabled(value);
    }
}
