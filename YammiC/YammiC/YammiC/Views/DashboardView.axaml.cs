using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class DashboardView : ContentPage
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private async void ChooseVpbImage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DashboardViewModel vm) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a pit board image",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        if (files.Count == 0) return;

        await using var stream = await files[0].OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        vm.SetVpbImage(memory.ToArray(), files[0].Name);
    }

    private async void NavigationCard_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Navigation != null)
            await Navigation.PushAsync(new NavigationSettingsView { DataContext = new NavigationSettingsViewModel(App.Controller) });
    }
}
