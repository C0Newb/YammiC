using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CCU_Emulator.Emulation;
using CCU_Emulator.Transport;
using CCU_Emulator.ViewModels;
using CCU_Emulator.Views;

namespace CCU_Emulator;

public partial class App : Application
{
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
            var path = Path.Combine(AppContext.BaseDirectory, "emulator.settings.json");
            var notes = new List<string>();

            CcuSettings settings;
            try
            {
                settings = SettingsStore.Load(path);
            }
            catch (Exception e)
            {
                settings = new CcuSettings();   // a broken file is left untouched; run with defaults
                notes.Add($"Could not read {path}: {e.Message}. Using defaults.");
            }

            if (!TransportFactory.IsAvailable(settings.Transport))
            {
                notes.Add($"Transport {settings.Transport} is not available in this build; using LoopbackTcp.");
                settings.Transport = TransportKind.LoopbackTcp;
            }

            var host = new EmulatorHost(settings, path, a => Dispatcher.UIThread.Post(a));
            foreach (var note in notes) host.Session.Log.Info("Host", note);

            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel(host) };
            desktop.Exit += (_, _) => host.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
