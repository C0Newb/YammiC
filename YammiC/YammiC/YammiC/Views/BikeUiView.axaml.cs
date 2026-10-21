using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using YammiC.ViewModels;

namespace YammiC.Views;

/// <summary>The settings for what the bike's displays show (navigation screen and pit board) and the way into the screen editor.</summary>
public partial class BikeUiView : ContentPage
{
    public BikeUiView()
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

    private async void NavigationSettings_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Navigation != null)
            await Navigation.PushAsync(new NavigationSettingsView { DataContext = new NavigationSettingsViewModel(App.Controller) });
    }

    private async void ScreenEditorRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Navigation != null)
            await Navigation.PushAsync(new ScreenEditorView { DataContext = new ScreenEditorViewModel(App.Controller) });
    }
}
