using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class SessionsView : ContentPage
{
    private SessionsViewModel? _vm;

    public SessionsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.ExportRequested -= Export;
            _vm = DataContext as SessionsViewModel;
            if (_vm != null) _vm.ExportRequested += Export;
        };
    }

    private async void Export(string file, string gpx)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null || _vm == null) return;
        var saved = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export session as GPX",
            SuggestedFileName = Path.ChangeExtension(file, ".gpx"),
            DefaultExtension = "gpx",
            FileTypeChoices = [new FilePickerFileType("GPX track") { Patterns = ["*.gpx"], MimeTypes = ["application/gpx+xml"] }],
        });
        if (saved == null) return;
        await using var stream = await saved.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(gpx);
        _vm.Status = "Exported.";
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _vm?.Detach();
    }
}
