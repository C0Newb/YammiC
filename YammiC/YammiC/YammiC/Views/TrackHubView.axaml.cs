using Avalonia.Controls;
using Avalonia.Input;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class TrackHubView : ContentPage
{
    public TrackHubView()
    {
        InitializeComponent();
    }

    private async void Timer_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Navigation != null) await Navigation.PushAsync(new TrackTimerView { DataContext = new TrackTimerViewModel(App.TrackTimer) });
    }

    private async void Tracks_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Navigation != null) await Navigation.PushAsync(new TracksView { DataContext = new TracksViewModel(App.TrackTimer) });
    }

    private async void Sessions_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Navigation != null) await Navigation.PushAsync(new SessionsView { DataContext = new SessionsViewModel(App.TrackTimer) });
    }

    private async void Settings_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Navigation != null) await Navigation.PushAsync(new TrackSettingsView { DataContext = new TrackSettingsViewModel(App.TrackTimer) });
    }

    private async void ManualTimer_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Navigation != null) await Navigation.PushAsync(new LapTimerView { DataContext = new LapTimerViewModel(App.Controller) });
    }
}
