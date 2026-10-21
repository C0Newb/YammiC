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
        Canvas.BoundsDragged += (element, x, y, w, h) => _vm?.OnBoundsDragged(element, x, y, w, h);
        DataContextChanged += (_, _) => Attach(DataContext as ScreenEditorViewModel);
    }

    private void Attach(ScreenEditorViewModel? vm)
    {
        if (_vm != null) _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = vm;
        if (vm == null) return;

        vm.PropertyChanged += OnViewModelPropertyChanged;
        Canvas.Target = vm.Target;
        Canvas.Elements = vm.PreviewElements;
        Canvas.Preview = vm.Preview;
        Canvas.Selected = vm.SelectedElement;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm == null) return;
        switch (e.PropertyName)
        {
            case nameof(ScreenEditorViewModel.Target):
                Canvas.Target = _vm.Target;
                break;
            case nameof(ScreenEditorViewModel.PreviewElements):
                Canvas.Elements = _vm.PreviewElements;
                Canvas.Refresh();
                break;
            case nameof(ScreenEditorViewModel.Preview):
                Canvas.Preview = _vm.Preview;
                break;
            case nameof(ScreenEditorViewModel.SelectedElement):
                Canvas.Selected = _vm.SelectedElement;
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
        if (_vm == null) return;
        var picked = await PickImageAsync();
        if (picked == null) return;
        _vm.AddImage(picked.Value.Data, picked.Value.Name);
    }

    private async void ChangeImage_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var picked = await PickImageAsync();
        if (picked == null) return;
        _vm.ReplaceImage(picked.Value.Data);
    }

    private static readonly FilePickerFileType ScreenFileType = new("Screen (AXAML)")
    {
        Patterns = ["*.axaml", "*.xml"],
        MimeTypes = ["application/xml", "text/xml"],
    };

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (_vm == null || storage == null) return;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export screen",
            SuggestedFileName = $"{_vm.Target}.axaml",
            DefaultExtension = "axaml",
            FileTypeChoices = [ScreenFileType],
        });
        if (file == null) return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(_vm.ExportAxaml());
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
            FileTypeFilter = [ScreenFileType],
        });
        if (files.Count == 0) return;

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        _vm.ImportAxaml(await reader.ReadToEndAsync());
    }
}
