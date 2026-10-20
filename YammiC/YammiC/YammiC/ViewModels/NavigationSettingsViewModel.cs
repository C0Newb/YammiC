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
