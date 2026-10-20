using CommunityToolkit.Mvvm.ComponentModel;
using CNCO.YccuSdk.Navigation;
using YammiC.Services;

namespace YammiC.ViewModels;

public partial class SettingsViewModel : BikeViewModelBase
{
    private bool _applying;

    [ObservableProperty]
    public partial bool MusicMetadataEnabled { get; set; }

    [ObservableProperty]
    public partial bool PhoneMetadataEnabled { get; set; }

    [ObservableProperty]
    public partial bool AutoReconnectEnabled { get; set; }

    [ObservableProperty]
    public partial bool HomeAndWorkEnabled { get; set; }

    [ObservableProperty]
    public partial NavMode NavigationMode { get; set; }

    public static NavMode[] NavModes { get; } = System.Enum.GetValues<NavMode>();

    public bool ReelsSupported => Controller.ReelsSupported;

    [ObservableProperty]
    public partial string ReelsFolderText { get; set; } = "";

    /// <summary>Called by the view with the folder the system picker returned.</summary>
    public void SetReelsFolder(string folderUri, string? displayName)
    {
        Controller.SetReelsFolder(folderUri, displayName);
        ReelsFolderText = ReelsFolderLabel();
    }

    private string ReelsFolderLabel() => Controller.ReelsFolderName is { Length: > 0 } name ? name : "No folder chosen";

    [ObservableProperty]
    public partial bool VpbScreenEnabled { get; set; }

    [ObservableProperty]
    public partial bool NavigationEnabled { get; set; }

    [ObservableProperty]
    public partial bool VpbEnabled { get; set; }

    public SettingsViewModel(IBikeController controller) : base(controller)
    {
        MusicMetadataEnabled = controller.MusicMetadataEnabled;
        PhoneMetadataEnabled = controller.PhoneMetadataEnabled;
        AutoReconnectEnabled = controller.AutoReconnectEnabled;
        HomeAndWorkEnabled = controller.HomeAndWorkEnabled;
        NavigationMode = controller.NavigationMode;
        ReelsFolderText = ReelsFolderLabel();
        VpbScreenEnabled = controller.VpbScreenEnabled;
        Attach();
    }

    // Design-time constructor.
    public SettingsViewModel() : this(new FakeBikeController())
    {
    }

    protected override void OnSnapshot(BikeSnapshot snapshot)
    {
        _applying = true;
        try
        {
            NavigationEnabled = snapshot.NavigationEnabled;
            VpbEnabled = snapshot.VpbEnabled;
        }
        finally
        {
            _applying = false;
        }
    }

    partial void OnMusicMetadataEnabledChanged(bool value) => Controller.MusicMetadataEnabled = value;

    partial void OnPhoneMetadataEnabledChanged(bool value) => Controller.PhoneMetadataEnabled = value;

    partial void OnAutoReconnectEnabledChanged(bool value) => Controller.AutoReconnectEnabled = value;

    partial void OnHomeAndWorkEnabledChanged(bool value) => Controller.HomeAndWorkEnabled = value;

    partial void OnNavigationModeChanged(NavMode value) => Controller.NavigationMode = value;

    partial void OnVpbScreenEnabledChanged(bool value) => Controller.VpbScreenEnabled = value;

    partial void OnNavigationEnabledChanged(bool value)
    {
        if (!_applying) Controller.SetNavigationEnabled(value);
    }

    partial void OnVpbEnabledChanged(bool value)
    {
        if (!_applying) Controller.SetVpbEnabled(value);
    }
}
