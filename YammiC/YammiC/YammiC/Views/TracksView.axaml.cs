using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CNCO.YccuSdk.Tracks;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class TracksView : ContentPage
{
    private static readonly FilePickerFileType TrackFileType = new("Track")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
    };

    private TracksViewModel? _vm;

    public TracksView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.EditRequested -= Edit;
                _vm.ExportRequested -= Export;
            }
            _vm = DataContext as TracksViewModel;
            if (_vm != null)
            {
                _vm.EditRequested += Edit;
                _vm.ExportRequested += Export;
            }
        };
    }

    private async void New_Click(object? sender, RoutedEventArgs e)
    {
        if (Navigation != null) await Navigation.PushAsync(new TrackEditorView { DataContext = new TrackEditorViewModel(App.TrackTimer) });
    }

    private async void Edit(Track track)
    {
        if (Navigation != null) await Navigation.PushAsync(new TrackEditorView { DataContext = new TrackEditorViewModel(App.TrackTimer, track) });
    }

    private async void Export(Track track)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null || _vm == null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export track",
            SuggestedFileName = track.Name.Replace(' ', '-') + ".json",
            DefaultExtension = "json",
            FileTypeChoices = [TrackFileType],
        });
        if (file == null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(_vm.Export(track));
        _vm.Status = "Exported.";
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null || _vm == null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import track",
            AllowMultiple = false,
            FileTypeFilter = [TrackFileType],
        });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        _vm.Import(await reader.ReadToEndAsync());
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _vm?.Detach();
    }
}
