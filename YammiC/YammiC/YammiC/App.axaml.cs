using Avalonia;
using CNCO.YccuSdk;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using YammiC.Services;
using YammiC.ViewModels;
using YammiC.Views;

namespace YammiC;

public partial class App : Application
{
    /// <summary>
    /// The SDK connection broker. Set by the platform head before the UI starts (only Android has a
    /// Bluetooth implementation so far); null means "no Bluetooth on this platform".
    /// </summary>
    public static YmcConnectionBroker? Broker { get; set; }

    private static IBikeController? _controller;

    /// <summary>
    /// What the pages talk to. A platform head may assign its own; otherwise one is created lazily over
    /// <see cref="Broker"/>, or a fake when the platform has no Bluetooth.
    /// </summary>
    public static IBikeController Controller
    {
        get => _controller ??= Broker != null ? new BrokerBikeController(Broker) : new FakeBikeController();
        set => _controller = value;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new PageNavigationHost()
            {
                Page = new MainView { DataContext = new MainViewModel() }
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new PageNavigationHost()
            {
                Page = new MainView { DataContext = new MainViewModel() }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}