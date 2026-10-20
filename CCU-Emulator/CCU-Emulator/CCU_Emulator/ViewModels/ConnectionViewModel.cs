using CCU_Emulator.Emulation;
using CCU_Emulator.Transport;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCU_Emulator.ViewModels;

public sealed partial class ConnectionViewModel : ViewModelBase
{
    private readonly EmulatorHost _host;

    public ConnectionViewModel(EmulatorHost host)
    {
        _host = host;
        host.Session.State.PropertyChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    private CcuState State => _host.Session.State;

    public string AdvertisedName => _host.Settings.AdvertisedName;

    public string TransportText
        => _host.Settings.Transport + (TransportFactory.IsAvailable(_host.Settings.Transport) ? "" : " (not available in this build)");

    [ObservableProperty] public partial bool Running { get; set; }
    [ObservableProperty] public partial string LastError { get; set; } = "";

    partial void OnRunningChanged(bool value) => OnPropertyChanged(nameof(LinkText));

    public string LinkText
        => !Running ? "Stopped"
        : !State.BleConnected ? "Advertising / waiting for a phone"
        : State.Authenticated ? "Phone connected and authenticated"
        : "Phone connected (not authenticated)";

    public string SppText => State.SppConnected ? "Classic link up" : "Classic link down";

    [RelayCommand]
    private async Task StartAsync()
    {
        await _host.StartAsync();
        Running = _host.IsRunning;
        LastError = _host.LastError ?? "";
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await _host.StopAsync();
        Running = _host.IsRunning;
    }

    [RelayCommand]
    private async Task DropAsync()
    {
        try
        {
            await _host.Session.DropConnectionAsync();
        }
        catch (Exception e)
        {
            LastError = e.Message;
        }
        Running = _host.IsRunning;
    }
}
