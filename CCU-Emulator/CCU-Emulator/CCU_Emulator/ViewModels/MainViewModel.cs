using CCU_Emulator.Emulation;

namespace CCU_Emulator.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    public EmulatorHost Host { get; }
    public DashViewModel Dash { get; }
    public ConnectionViewModel Connection { get; }
    public ButtonsViewModel Buttons { get; }
    public BehaviorViewModel Behavior { get; }
    public NotificationsViewModel Notifications { get; }
    public LogViewModel Log { get; }

    public string Title => $"CCU Emulator — {Host.Settings.AdvertisedName}";

    public MainViewModel(EmulatorHost host)
    {
        Host = host;
        var session = host.Session;
        Dash = new DashViewModel(session.State);
        Connection = new ConnectionViewModel(host);
        Buttons = new ButtonsViewModel(session);
        Behavior = new BehaviorViewModel(host.Settings.Behavior);
        Notifications = new NotificationsViewModel(session.State, session);
        Log = new LogViewModel(session.Log);
    }
}
