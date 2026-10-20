using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class SettingsView : ContentPage
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void ChooseReelsFolder_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null || DataContext is not SettingsViewModel vm) return;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the reels folder",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;
        vm.SetReelsFolder(folders[0].Path.ToString(), folders[0].Name);
    }

    private async void ScreenEditorRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Navigation != null)
            await Navigation.PushAsync(new ScreenEditorView { DataContext = new ScreenEditorViewModel(App.Controller) });
    }

    private async void CanRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Navigation != null)
            await Navigation.PushAsync(new CanView { DataContext = new CanViewModel(App.Controller) });
    }

    private async void RideModesRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Navigation != null)
            await Navigation.PushAsync(new RideModesView { DataContext = new RideModesViewModel(App.Controller) });
    }

    private async void LapTimerRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Navigation != null)
            await Navigation.PushAsync(new LapTimerView { DataContext = new LapTimerViewModel(App.Controller) });
    }

    private async void PairingRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Navigation != null)
            await Navigation.PushAsync(new PairingView { DataContext = new PairingViewModel(App.Controller) });
    }
}
