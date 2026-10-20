using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YammiC.Services;

namespace YammiC.ViewModels;

public partial class DashboardViewModel : BikeViewModelBase
{
    private bool _applying;

    [ObservableProperty]
    public partial bool NeedsNotificationAccess { get; set; }

    [ObservableProperty]
    public partial string BikeName { get; set; } = "";

    [ObservableProperty]
    public partial string BleNameText { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial string OdometerText { get; set; } = "Odometer: --";

    [ObservableProperty]
    public partial string LastError { get; set; } = "";

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial bool CanConnect { get; set; } = true;

    [ObservableProperty]
    public partial bool CanDisconnect { get; set; }

    [ObservableProperty]
    public partial string MediaTrack { get; set; } = "Nothing playing";

    [ObservableProperty]
    public partial string MediaArtist { get; set; } = "";

    [ObservableProperty]
    public partial string MediaState { get; set; } = "";

    [ObservableProperty]
    public partial bool VpbEnabled { get; set; } = true;

    [ObservableProperty]
    public partial string VpbStatus { get; set; } = "";

    [ObservableProperty]
    public partial Bitmap? VpbImagePreview { get; set; }

    [ObservableProperty]
    public partial string VpbImageName { get; set; } = "";

    [ObservableProperty]
    public partial string VpbImageStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool IsSendingVpbImage { get; set; }

    [ObservableProperty]
    public partial string NavigationStatus { get; set; } = "";

    [ObservableProperty]
    public partial string Logs { get; set; } = "";

    // The image that "Send image" pushes to the Virtual Pit Board, as picked/loaded (any format Skia can decode).
    private byte[]? _vpbImageData;

    private const string DefaultVpbImageName = "sandwich.jpg";

    public DashboardViewModel(IBikeController controller) : base(controller)
    {
        UseDefaultVpbImage();
        Attach();
    }

    // Design-time constructor.
    public DashboardViewModel() : this(new FakeBikeController())
    {
    }

    protected override void OnSnapshot(BikeSnapshot snapshot)
    {
        _applying = true;
        try
        {
            var c = snapshot.Connection;
            NeedsNotificationAccess = Controller.NeedsNotificationAccess;
            BikeName = c.DeviceName;
            BleNameText = $"BLE name: {c.DeviceName}";
            StatusText = $"Status: {c.ConnectionLabel}";
            LastError = c.LastError;
            HasError = !string.IsNullOrWhiteSpace(c.LastError);
            Logs = string.Join("\n", c.Logs);

            var button = c.GetConnectButtonState();
            CanConnect = Controller.CanConnect && button == ConnectButtonState.Idle;
            CanDisconnect = button == ConnectButtonState.Connected;

            var media = snapshot.Media;
            MediaTrack = string.IsNullOrWhiteSpace(media?.SongName) ? "Nothing playing" : media!.SongName;
            MediaArtist = media?.ArtistName ?? "";
            MediaState = media?.PlaybackState.ToString() ?? "";

            VpbEnabled = snapshot.VpbEnabled;
            VpbStatus = $"Displaying: {snapshot.VpbStatus}";
            NavigationStatus = NavigationStateFormatter.Describe(snapshot.Navigation);
        }
        finally
        {
            _applying = false;
        }
    }

    partial void OnVpbEnabledChanged(bool value)
    {
        if (!_applying) Controller.SetVpbEnabled(value);
    }

    /// <summary>Selects a user-picked image to send (called by the view after its file picker returns).</summary>
    public void SetVpbImage(byte[] data, string name)
    {
        try
        {
            using var stream = new MemoryStream(data);
            VpbImagePreview = new Bitmap(stream);
            _vpbImageData = data;
            VpbImageName = name;
            VpbImageStatus = "";
        }
        catch (Exception e)
        {
            VpbImageStatus = $"Can't use that image: {e.Message}";
        }
    }

    /// <summary>Goes back to the bundled sandwich.</summary>
    [RelayCommand]
    private void UseDefaultVpbImage()
    {
        try
        {
            using var asset = AssetLoader.Open(new Uri($"avares://YammiC/Assets/{DefaultVpbImageName}"));
            using var memory = new MemoryStream();
            asset.CopyTo(memory);
            SetVpbImage(memory.ToArray(), DefaultVpbImageName);
        }
        catch (Exception e)
        {
            // Not fatal (e.g. design-time previewer without the resource).
            VpbImageStatus = $"Default image unavailable: {e.Message}";
        }
    }

    [RelayCommand]
    private async Task SendVpbImage()
    {
        if (_vpbImageData == null || IsSendingVpbImage) return;
        IsSendingVpbImage = true;
        VpbImageStatus = "Sending...";
        try
        {
            VpbImageStatus = await Controller.SendVpbImageAsync(_vpbImageData)
                ? "Sent"
                : "Not sent (is the bike connected and VPB enabled?)";
        }
        catch (Exception e)
        {
            VpbImageStatus = $"Failed: {e.Message}";
        }
        finally
        {
            IsSendingVpbImage = false;
        }
    }

    [RelayCommand]
    private async Task ClearVpbImage()
    {
        if (IsSendingVpbImage) return;
        IsSendingVpbImage = true;
        try
        {
            VpbImageStatus = await Controller.ClearVpbAsync() ? "Cleared" : "Couldn't clear (is the bike connected?)";
        }
        catch (Exception e)
        {
            VpbImageStatus = $"Failed: {e.Message}";
        }
        finally
        {
            IsSendingVpbImage = false;
        }
    }

    [RelayCommand]
    private void Connect() => Controller.Connect();

    [RelayCommand]
    private void Disconnect() => Controller.Disconnect();

    [RelayCommand]
    private void ClearLogs() => Controller.ClearLogs();

    [RelayCommand]
    private void GrantNotificationAccess() => Controller.OpenNotificationAccessSettings();
}
