using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using YammiC.ViewModels;

namespace YammiC.Views;

public partial class ScreenEditorView : ContentPage
{
    private ScreenEditorViewModel? _vm;

    public ScreenEditorView()
    {
        InitializeComponent();

        Canvas.SelectionChanged += element => _vm?.Select(element);
        Canvas.ElementChanged += element => _vm?.OnElementDragged(element);
        DataContextChanged += (_, _) => Attach(DataContext as ScreenEditorViewModel);
    }

    private void Attach(ScreenEditorViewModel? vm)
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.LayoutChanged -= Canvas.Refresh;
        }

        _vm = vm;
        if (vm == null) return;

        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.LayoutChanged += Canvas.Refresh;
        Canvas.Layout = vm.Layout;
        Canvas.Context = vm.PreviewContext;
        Canvas.Selected = vm.SelectedItem?.Element;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm == null) return;
        switch (e.PropertyName)
        {
            case nameof(ScreenEditorViewModel.Layout):
                Canvas.Layout = _vm.Layout;
                break;
            case nameof(ScreenEditorViewModel.PreviewContext):
                Canvas.Context = _vm.PreviewContext;
                break;
            case nameof(ScreenEditorViewModel.SelectedItem):
                Canvas.Selected = _vm.SelectedItem?.Element;
                break;
        }
    }

    private async Task<(byte[] Data, string Name)?> PickImageAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null) return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an image",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        if (files.Count == 0) return null;

        await using var stream = await files[0].OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return (memory.ToArray(), files[0].Name);
    }

    private async void AddImage_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || await PickImageAsync() is not { } picked) return;
        _vm.AddImage(picked.Data, picked.Name);
    }

    private async void ChangeImage_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || await PickImageAsync() is not { } picked) return;
        _vm.ReplaceImage(picked.Data);
    }

    private static readonly FilePickerFileType LayoutFileType = new("Screen layout")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
    };

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (_vm == null || storage == null) return;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export screen",
            SuggestedFileName = $"{_vm.LayoutName}.json",
            DefaultExtension = "json",
            FileTypeChoices = [LayoutFileType],
        });
        if (file == null) return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(_vm.ExportJson());
        _vm.Status = "Exported.";
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (_vm == null || storage == null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import screen",
            AllowMultiple = false,
            FileTypeFilter = [LayoutFileType],
        });
        if (files.Count == 0) return;

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        _vm.ImportJson(await reader.ReadToEndAsync());
    }
}
