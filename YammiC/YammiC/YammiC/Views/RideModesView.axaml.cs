using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CNCO.YccuSdk.Yrc;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class RideModesView : ContentPage
{
    private RideModesViewModel? _vm;

    public RideModesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.EditRequested -= Edit;
            _vm = DataContext as RideModesViewModel;
            if (_vm != null) _vm.EditRequested += Edit;
        };
    }

    private async void Edit(SavedRideMode item)
    {
        if (Navigation != null)
            await Navigation.PushAsync(new RideModeEditorView { DataContext = new RideModeEditorViewModel(App.Controller.ProfileLibrary, item) });
    }

    private static readonly FilePickerFileType RideModesFileType = new("Ride modes")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
    };

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (DataContext is not RideModesViewModel vm || storage == null) return;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export ride modes",
            SuggestedFileName = "ride-modes.json",
            DefaultExtension = "json",
            FileTypeChoices = [RideModesFileType],
        });
        if (file == null) return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(vm.ExportJson());
        vm.Status = "Exported.";
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (DataContext is not RideModesViewModel vm || storage == null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import ride modes",
            AllowMultiple = false,
            FileTypeFilter = [RideModesFileType],
        });
        if (files.Count == 0) return;

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        vm.ImportJson(await reader.ReadToEndAsync());
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        (DataContext as RideModesViewModel)?.Detach();
    }
}
