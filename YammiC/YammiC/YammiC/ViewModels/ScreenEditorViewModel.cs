using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Navigation;
using CNCO.YccuSdk.Screens;
using SkiaSharp;
using YammiC.Services;

namespace YammiC.ViewModels;

/// <summary>What the editor preview pretends is playing.</summary>
public enum PreviewMusic
{
    Playing,
    Paused,
    None,
}

/// <summary>One row of the element list; refreshes its title when the element is renamed.</summary>
public partial class ScreenElementItem : ObservableObject
{
    public ScreenElementItem(ScreenElement element)
    {
        Element = element;
    }

    public ScreenElement Element { get; }

    public string Title => string.IsNullOrWhiteSpace(Element.Name)
        ? (Element.Kind == ScreenElementKind.Image ? "Image" : "Text")
        : Element.Name;

    public string Kind => Element.Kind == ScreenElementKind.Image ? "Image" : "Text";

    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
    }
}

/// <summary>
/// Edits the custom screen of one display (navigation or pit board). Works on a copy of the saved layout; nothing reaches
/// the bike until <see cref="ApplyCommand"/>. The canvas control edits element positions directly, this holds the rest.
/// </summary>
public partial class ScreenEditorViewModel : ViewModelBase
{
    private const int MaxImageSide = 1024;

    private readonly IBikeController _controller;
    private bool _loading;

    public ScreenEditorViewModel(IBikeController controller)
    {
        _controller = controller;
        LoadTarget();
    }

    // Design-time constructor.
    public ScreenEditorViewModel() : this(new FakeBikeController())
    {
    }

    public static ScreenTarget[] Targets { get; } = { ScreenTarget.Navigation, ScreenTarget.VirtualPitBoard };
    public static ScreenShowWhen[] ShowWhens { get; } = Enum.GetValues<ScreenShowWhen>();
    public static ScreenTextAlign[] Aligns { get; } = Enum.GetValues<ScreenTextAlign>();
    public static ScreenImageFit[] Fits { get; } = Enum.GetValues<ScreenImageFit>();
    public static PreviewMusic[] PreviewMusics { get; } = Enum.GetValues<PreviewMusic>();

    public static string VariableHelp { get; } = string.Join(
        "\n",
        ScreenVariables.Catalog.Select(v => $"{{{v.Name}}}  {v.Description}  (e.g. {v.Example})"));

    public ObservableCollection<ScreenElementItem> Items { get; } = new();

    /// <summary>The layout being edited (the canvas draws and edits this object).</summary>
    [ObservableProperty]
    public partial ScreenLayout Layout { get; set; } = new();

    [ObservableProperty]
    public partial ScreenTarget Target { get; set; } = ScreenTarget.Navigation;

    [ObservableProperty]
    public partial ScreenContext PreviewContext { get; set; } = new(DateTime.Now, null, false);

    [ObservableProperty]
    public partial bool PreviewNight { get; set; }

    [ObservableProperty]
    public partial PreviewMusic PreviewMusicState { get; set; } = PreviewMusic.Playing;

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    /// <summary>Whether this screen is what the bike shows (navigation: instead of the demo image).</summary>
    [ObservableProperty]
    public partial bool ScreenEnabled { get; set; }

    [ObservableProperty]
    public partial string LayoutName { get; set; } = "";

    [ObservableProperty]
    public partial string LayoutBackground { get; set; } = "";

    [ObservableProperty]
    public partial string LayoutNightBackground { get; set; } = "";

    // -- Selection ------------------------------------------------------------------

    [ObservableProperty]
    public partial ScreenElementItem? SelectedItem { get; set; }

    public bool HasSelection => SelectedItem != null;
    public bool IsTextSelected => SelectedItem?.Element.Kind == ScreenElementKind.Text;
    public bool IsImageSelected => SelectedItem?.Element.Kind == ScreenElementKind.Image;

    [ObservableProperty]
    public partial string SelName { get; set; } = "";

    [ObservableProperty]
    public partial double SelX { get; set; }

    [ObservableProperty]
    public partial double SelY { get; set; }

    [ObservableProperty]
    public partial double SelWidth { get; set; }

    [ObservableProperty]
    public partial double SelHeight { get; set; }

    [ObservableProperty]
    public partial ScreenShowWhen SelShowWhen { get; set; }

    [ObservableProperty]
    public partial string SelText { get; set; } = "";

    [ObservableProperty]
    public partial double SelFontSize { get; set; }

    [ObservableProperty]
    public partial bool SelBold { get; set; }

    [ObservableProperty]
    public partial ScreenTextAlign SelAlign { get; set; }

    [ObservableProperty]
    public partial string SelColor { get; set; } = "";

    [ObservableProperty]
    public partial string SelNightColor { get; set; } = "";

    [ObservableProperty]
    public partial string SelBackground { get; set; } = "";

    [ObservableProperty]
    public partial string SelNightBackground { get; set; } = "";

    [ObservableProperty]
    public partial ScreenImageFit SelFit { get; set; }

    /// <summary>Raised when the picture needs redrawing (any edit).</summary>
    public event Action? LayoutChanged;

    // -- Loading --------------------------------------------------------------------

    private void LoadTarget()
    {
        _loading = true;
        try
        {
            Layout = _controller.GetScreenLayout(Target);
            foreach (var element in Layout.Elements) ScreenSizes.ClampToBounds(element, Target);
            LayoutName = Layout.Name;
            LayoutBackground = Layout.Background;
            LayoutNightBackground = Layout.NightBackground ?? "";
            ScreenEnabled = Target == ScreenTarget.Navigation ? _controller.NavigationMode == NavMode.Ui : _controller.VpbScreenEnabled;
            RebuildItems();
            UpdatePreviewContext();
        }
        finally
        {
            _loading = false;
        }
        Select(null);
    }

    private void RebuildItems()
    {
        Items.Clear();
        foreach (var element in Layout.Elements) Items.Add(new ScreenElementItem(element));
    }

    partial void OnTargetChanged(ScreenTarget value)
    {
        LoadTarget();
        Status = "";
    }

    partial void OnScreenEnabledChanged(bool value)
    {
        if (_loading) return;
        if (Target == ScreenTarget.Navigation)
        {
            // On: show this screen. Off: back to the sandwich, but only if this screen was what was showing.
            if (value) _controller.NavigationMode = NavMode.Ui;
            else if (_controller.NavigationMode == NavMode.Ui) _controller.NavigationMode = NavMode.Sandwich;
        }
        else _controller.VpbScreenEnabled = value;
    }

    // -- Preview ----------------------------------------------------------------------

    partial void OnPreviewNightChanged(bool value) => UpdatePreviewContext();

    partial void OnPreviewMusicStateChanged(PreviewMusic value) => UpdatePreviewContext();

    private void UpdatePreviewContext()
    {
        YmcMediaMetaData? media = PreviewMusicState == PreviewMusic.None
            ? null
            : new YmcMediaMetaData(
                "Queen",
                "A Night at the Opera",
                "Bohemian Rhapsody",
                355,
                PreviewMusicState == PreviewMusic.Playing ? YmcMediaPlaybackState.Playing : YmcMediaPlaybackState.Paused,
                1f,
                83);
        PreviewContext = new ScreenContext(DateTime.Now, media, PreviewNight, 1f, CNCO.YccuSdk.Can.CanValues.Sample);
    }

    /// <summary>Tells the canvas to redraw.</summary>
    private void Changed()
    {
        if (_loading) return;
        LayoutChanged?.Invoke();
    }

    // -- Canvas <-> selection ----------------------------------------------------------

    /// <summary>The canvas selected (or deselected) an element by touching it.</summary>
    public void Select(ScreenElement? element)
    {
        SelectedItem = element == null ? null : Items.FirstOrDefault(i => i.Element == element);
    }

    /// <summary>The canvas moved or resized the selected element: show the new numbers.</summary>
    public void OnElementDragged(ScreenElement element)
    {
        if (SelectedItem?.Element != element) return;
        _loading = true;
        try
        {
            SelX = element.X;
            SelY = element.Y;
            SelWidth = element.Width;
            SelHeight = element.Height;
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnSelectedItemChanged(ScreenElementItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsTextSelected));
        OnPropertyChanged(nameof(IsImageSelected));
        if (value == null) return;

        var e = value.Element;
        var wasLoading = _loading;
        _loading = true;
        try
        {
            SelName = e.Name;
            SelX = e.X;
            SelY = e.Y;
            SelWidth = e.Width;
            SelHeight = e.Height;
            SelShowWhen = e.ShowWhen;
            SelText = e.Text;
            SelFontSize = e.FontSize;
            SelBold = e.Bold;
            SelAlign = e.Align;
            SelColor = e.Color;
            SelNightColor = e.NightColor ?? "";
            SelBackground = e.Background ?? "";
            SelNightBackground = e.NightBackground ?? "";
            SelFit = e.Fit;
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    // -- Field edits write through to the element ------------------------------------------

    private void Edit(Action<ScreenElement> change)
    {
        if (_loading || SelectedItem == null) return;
        change(SelectedItem.Element);
        SelectedItem.Refresh();
        Changed();
    }

    /// <summary>A position or size edit: kept inside the area the bike shows, with the fields corrected to what was applied.</summary>
    private void EditGeometry(Action<ScreenElement> change)
    {
        if (_loading || SelectedItem == null) return;
        var element = SelectedItem.Element;
        change(element);
        ScreenSizes.ClampToBounds(element, Target);
        OnElementDragged(element);
        SelectedItem.Refresh();
        Changed();
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    partial void OnSelNameChanged(string value) => Edit(e => e.Name = value);
    partial void OnSelXChanged(double value) => EditGeometry(e => e.X = (float)value);
    partial void OnSelYChanged(double value) => EditGeometry(e => e.Y = (float)value);
    partial void OnSelWidthChanged(double value) => EditGeometry(e => e.Width = (float)value);
    partial void OnSelHeightChanged(double value) => EditGeometry(e => e.Height = (float)value);
    partial void OnSelShowWhenChanged(ScreenShowWhen value) => Edit(e => e.ShowWhen = value);
    partial void OnSelTextChanged(string value) => Edit(e => e.Text = value);
    partial void OnSelFontSizeChanged(double value) => Edit(e => e.FontSize = Math.Max((float)value, 1));
    partial void OnSelBoldChanged(bool value) => Edit(e => e.Bold = value);
    partial void OnSelAlignChanged(ScreenTextAlign value) => Edit(e => e.Align = value);
    partial void OnSelColorChanged(string value) => Edit(e => e.Color = string.IsNullOrWhiteSpace(value) ? "#FFFFFF" : value.Trim());
    partial void OnSelNightColorChanged(string value) => Edit(e => e.NightColor = NullIfEmpty(value));
    partial void OnSelBackgroundChanged(string value) => Edit(e => e.Background = NullIfEmpty(value));
    partial void OnSelNightBackgroundChanged(string value) => Edit(e => e.NightBackground = NullIfEmpty(value));
    partial void OnSelFitChanged(ScreenImageFit value) => Edit(e => e.Fit = value);

    partial void OnLayoutNameChanged(string value)
    {
        if (!_loading) Layout.Name = value;
    }

    partial void OnLayoutBackgroundChanged(string value)
    {
        if (_loading) return;
        Layout.Background = string.IsNullOrWhiteSpace(value) ? "#000000" : value.Trim();
        Changed();
    }

    partial void OnLayoutNightBackgroundChanged(string value)
    {
        if (_loading) return;
        Layout.NightBackground = NullIfEmpty(value);
        Changed();
    }

    // -- Commands -------------------------------------------------------------------------

    private void AddElement(ScreenElement element)
    {
        Layout.Elements.Add(element);
        var item = new ScreenElementItem(element);
        Items.Add(item);
        SelectedItem = item;
        Changed();
    }

    [RelayCommand]
    private void AddText()
    {
        var area = ScreenSizes.Bounds(Target);
        var textHeight = Math.Min(48f, area.Height);
        AddElement(new ScreenElement
        {
            Name = "Text",
            Kind = ScreenElementKind.Text,
            Text = "{time}",
            X = area.X + area.Width * 0.1f,
            Y = area.Y + (area.Height - textHeight) / 2f,
            Width = area.Width * 0.8f,
            Height = textHeight,
            FontSize = 32,
            Align = ScreenTextAlign.Center,
            Color = "#FFFFFF",
            NightColor = null,
            Background = null,
        });
    }

    /// <summary>Adds an image element from a picked file (any format Skia can decode); big pictures are shrunk first.</summary>
    public void AddImage(byte[] file, string name)
    {
        var data = PrepareImage(file);
        if (data == null)
        {
            Status = "Can't use that image.";
            return;
        }

        var area = ScreenSizes.Bounds(Target);
        AddElement(new ScreenElement
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Image" : System.IO.Path.GetFileNameWithoutExtension(name),
            Kind = ScreenElementKind.Image,
            ImageData = Convert.ToBase64String(data),
            X = area.X + area.Width * 0.25f,
            Y = area.Y + area.Height * 0.2f,
            Width = area.Width * 0.5f,
            Height = area.Height * 0.6f,
            Fit = ScreenImageFit.Contain,
        });
        Status = "";
    }

    /// <summary>Replaces the selected image element's picture.</summary>
    public void ReplaceImage(byte[] file)
    {
        if (SelectedItem == null || SelectedItem.Element.Kind != ScreenElementKind.Image) return;
        var data = PrepareImage(file);
        if (data == null)
        {
            Status = "Can't use that image.";
            return;
        }
        SelectedItem.Element.ImageData = Convert.ToBase64String(data);
        Status = "";
        Changed();
    }

    private static byte[]? PrepareImage(byte[] file)
    {
        using var source = SKBitmap.Decode(file);
        if (source == null) return null;

        var scale = Math.Min(1f, MaxImageSide / (float)Math.Max(source.Width, source.Height));
        if (scale >= 1f && file.Length < 300_000) return file;

        var width = Math.Max((int)(source.Width * scale), 1);
        var height = Math.Max((int)(source.Height * scale), 1);
        using var resized = source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized == null) return null;
        using var image = SKImage.FromBitmap(resized);
        // Keep transparency when there is some; otherwise JPEG is far smaller.
        var hasAlpha = source.AlphaType != SKAlphaType.Opaque;
        using var encoded = image.Encode(hasAlpha ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, 88);
        return encoded?.ToArray();
    }

    [RelayCommand]
    private void DuplicateElement()
    {
        if (SelectedItem == null) return;
        var copy = SelectedItem.Element.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = SelectedItem.Element.Name + " copy";
        copy.X += 10;
        copy.Y += 10;
        ScreenSizes.ClampToBounds(copy, Target);
        AddElement(copy);
    }

    [RelayCommand]
    private void DeleteElement()
    {
        if (SelectedItem == null) return;
        var item = SelectedItem;
        Layout.Elements.Remove(item.Element);
        Items.Remove(item);
        SelectedItem = null;
        Changed();
    }

    [RelayCommand]
    private void MoveForward() => MoveSelected(1);

    [RelayCommand]
    private void MoveBackward() => MoveSelected(-1);

    private void MoveSelected(int direction)
    {
        if (SelectedItem == null) return;
        var item = SelectedItem;
        var index = Items.IndexOf(item);
        var target = index + direction;
        if (target < 0 || target >= Items.Count) return;
        Items.Move(index, target);
        Layout.Elements.RemoveAt(index);
        Layout.Elements.Insert(target, item.Element);
        SelectedItem = item;
        Changed();
    }

    [RelayCommand]
    private void Apply()
    {
        _controller.SaveScreenLayout(Target, Layout);
        Status = Target == ScreenTarget.Navigation && !ScreenEnabled
            ? "Saved. Turn on \"Show on the bike\" to see it."
            : "Saved and sent to the bike.";
    }

    [RelayCommand]
    private void Revert()
    {
        LoadTarget();
        Status = "Reverted to the saved screen.";
    }

    [RelayCommand]
    private void ResetToDefault()
    {
        _controller.ResetScreenLayout(Target);
        LoadTarget();
        Status = "Back to the built-in screen.";
    }

    public string ExportJson() => ScreenLayoutStore.ToJson(Layout);

    /// <summary>Replaces the working layout with an imported file (not saved until Apply).</summary>
    public void ImportJson(string json)
    {
        var layout = ScreenLayoutStore.TryParse(json);
        if (layout == null)
        {
            Status = "That file isn't a screen layout.";
            return;
        }

        layout.Target = Target;
        foreach (var element in layout.Elements) ScreenSizes.ClampToBounds(element, Target);
        Layout = layout;
        _loading = true;
        try
        {
            LayoutName = layout.Name;
            LayoutBackground = layout.Background;
            LayoutNightBackground = layout.NightBackground ?? "";
            RebuildItems();
        }
        finally
        {
            _loading = false;
        }
        Select(null);
        LayoutChanged?.Invoke();
        Status = "Imported. Press Apply to use it.";
    }
}
