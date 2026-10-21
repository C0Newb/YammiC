using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using YammiC.ViewModels;

namespace YammiC.Views;

/// <summary>
/// The app's frame: a drawer (hamburger menu) with the sections, and the chosen section's own page stack as the content. Each
/// section is built fresh when chosen, so the pages' view models follow the controller only while they are showing.
/// The content is a plain page (no scroll viewer around it), so touch-drag in the editors and the map keeps working.
/// </summary>
public partial class ShellView : DrawerPage
{
    public ShellView()
    {
        InitializeComponent();
        Content = Build("Dashboard");
    }

    private void Section_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string section })
        {
            Content = Build(section);
            IsOpen = false;
        }
    }

    private static Page Build(string section) => section switch
    {
        "Telemetry" => new Section(() => new CanView { DataContext = new CanViewModel(App.Controller) }),
        "Track" => new Section(() => new TrackHubView()),
        "YTrac" => new Section(() => new RideModesView { DataContext = new RideModesViewModel(App.Controller) }),
        "BikeUi" => new Section(() => new BikeUiView { DataContext = new SettingsViewModel(App.Controller) }),
        "Log" => new Section(() => new DebugLogView { DataContext = new DebugLogViewModel(App.Controller) }),
        "Settings" => new Section(() => new SettingsView { DataContext = new SettingsViewModel(App.Controller) }),
        _ => new Section(() => new DashboardView { DataContext = new DashboardViewModel(App.Controller) }),
    };

    /// <summary>One section: a page stack whose first page is made when it appears.</summary>
    private sealed class Section : NavigationPage
    {
        private readonly Func<Page> _root;

        public Section(Func<Page> root) => _root = root;

        protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CurrentPage == null) await PushAsync(_root());
        }
    }
}
