using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CNCO.YammiC.Screens;
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

/// <summary>One row of the page list; refreshes its title when the page is renamed.</summary>
public partial class PageItem : ObservableObject
{
    public PageItem(ScreenPage page)
    {
        Page = page;
    }

    public ScreenPage Page { get; }

    public string Title => string.IsNullOrWhiteSpace(Page.Name) ? Page.Id : Page.Name;

    public void Refresh() => OnPropertyChanged(nameof(Title));
}

/// <summary>One row of the element list; refreshes its title when the element is renamed.</summary>
public partial class ElementItem : ObservableObject
{
    public ElementItem(EditElement element)
    {
        Element = element;
    }

    public EditElement Element { get; }

    public string Title => string.IsNullOrWhiteSpace(Element.Name) ? Element.Kind.ToString() : Element.Name;

    public string Kind => Element.Kind.ToString();

    public void Refresh() => OnPropertyChanged(nameof(Title));
}

/// <summary>
/// Edits the custom screen of one display (navigation or pit board): its pages, what the bike's buttons do on each, and the
/// elements of the selected page. Works on a copy of the saved screen; nothing reaches the bike until <see cref="ApplyCommand"/>.
/// The Code tab shows the whole screen as AXAML and is the other way to edit the same document.
/// </summary>
public partial class ScreenEditorViewModel : ViewModelBase
{
    private const int MaxImageSide = 1024;
    private const int CodeTab = 2;

    private readonly IBikeController _controller;
    private readonly PreviewRenderer?[] _renderers = new PreviewRenderer?[2];
    private ScreenDocument _document = new();
    private PageEditModel? _model;
    private bool _loading;
    private bool _codeDirty;
    private bool _settingCode;

    public ScreenEditorViewModel(IBikeController controller)
    {
        _controller = controller;
        UpdatePreviewContext();
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
    public static ImageOrigin[] Origins { get; } = Enum.GetValues<ImageOrigin>();
    public static PreviewMusic[] PreviewMusics { get; } = Enum.GetValues<PreviewMusic>();

    public static string VariableHelp { get; } =
        string.Join("\n", ScreenVariables.Catalog.Select(v => $"{{{v.Name}}}  {v.Description}  (e.g. {v.Example})")) +
        "\n\nBindings (for TextBlock Text=\"{Binding ...}\" and IsVisible):\n" +
        "HasMusic IsPlaying IsPaused NoMusic Day Night Song Artist Album Time Date Progress Elapsed Duration PageNumber PageCount " +
        "Flags[name] Vars[name] Rpm Water Intake Gear Tps Aps Bank Pitch AccelX AccelY FrontSpeed RearSpeed";

    public static string ActionHelp { get; } =
        "Actions: NextPage, PreviousPage, GoTo:<page id>, Media.PlayPause, Media.Next, Media.Previous, Toggle:<flag>, Set:<name>=<value>, None. " +
        "Empty = use the screen's default.";

    public ObservableCollection<PageItem> Pages { get; } = new();
    public ObservableCollection<ElementItem> Items { get; } = new();
    public ObservableCollection<string> Issues { get; } = new();

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

    /// <summary>0 = Design, 1 = Pages, 2 = Code.</summary>
    [ObservableProperty]
    public partial int SelectedTab { get; set; }

    // -- Preview picture ----------------------------------------------------------------

    /// <summary>The selected page as the bike would show it (design size); null until drawn.</summary>
    [ObservableProperty]
    public partial Bitmap? Preview { get; set; }

    /// <summary>The selected page's elements, for the canvas to outline.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<EditElement>? PreviewElements { get; set; }

    // -- Pages ------------------------------------------------------------------------------

    [ObservableProperty]
    public partial PageItem? SelectedPage { get; set; }

    [ObservableProperty]
    public partial string PageName { get; set; } = "";

    [ObservableProperty]
    public partial string PageZoomIn { get; set; } = "";

    [ObservableProperty]
    public partial string PageZoomOut { get; set; } = "";

    [ObservableProperty]
    public partial string DefaultZoomIn { get; set; } = "";

    [ObservableProperty]
    public partial string DefaultZoomOut { get; set; } = "";

    [ObservableProperty]
    public partial string PageBackground { get; set; } = "";

    [ObservableProperty]
    public partial string PageNightBackground { get; set; } = "";

    // -- Code -------------------------------------------------------------------------------

    [ObservableProperty]
    public partial string Code { get; set; } = "";

    // -- Selection --------------------------------------------------------------------------

    [ObservableProperty]
    public partial ElementItem? SelectedItem { get; set; }

    public EditElement? SelectedElement => SelectedItem?.Element;
    public bool HasSelection => SelectedItem != null;
    public bool IsTextSelected => SelectedItem?.Element.Kind == EditKind.Text;
    public bool IsImageSelected => SelectedItem?.Element.Kind == EditKind.Image;
    public bool IsEmbeddedImageSelected => SelectedItem?.Element is { Kind: EditKind.Image, ImageOrigin: ImageOrigin.Embedded };
    public bool IsProgressSelected => SelectedItem?.Element.Kind == EditKind.Progress;
    public bool HasFill => SelectedItem?.Element.Kind is EditKind.Text or EditKind.Image or EditKind.Box or EditKind.Progress;

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

    [ObservableProperty]
    public partial ImageOrigin SelOrigin { get; set; }

    // -- Loading --------------------------------------------------------------------------------

    private void LoadTarget()
    {
        _loading = true;
        try
        {
            _document = ParseOrDefault(_controller.GetScreenAxaml(Target));
            SetCode(_document.ToAxaml());
            Issues.Clear();
            ScreenEnabled = Target == ScreenTarget.Navigation ? _controller.NavigationMode == NavMode.Ui : _controller.VpbScreenEnabled;
            LoadPages();
        }
        finally
        {
            _loading = false;
        }
        SelectedPage = Pages.FirstOrDefault();
    }

    private ScreenDocument ParseOrDefault(string text)
    {
        try
        {
            return ScreenDocument.Parse(text);
        }
        catch (ScreenDocumentException)
        {
            return ScreenDefaults.Document(Target);
        }
    }

    /// <summary>Refills the page list and the screen-wide actions from <see cref="_document"/> (caller holds <see cref="_loading"/>).</summary>
    private void LoadPages()
    {
        SelectedPage = null;
        Pages.Clear();
        foreach (var page in _document.Pages) Pages.Add(new PageItem(page));
        DefaultZoomIn = _document.DefaultActions.GetValueOrDefault(ScreenEvent.ZoomIn, "");
        DefaultZoomOut = _document.DefaultActions.GetValueOrDefault(ScreenEvent.ZoomOut, "");
    }

    private void SetCode(string text)
    {
        _settingCode = true;
        try
        {
            Code = text;
            _codeDirty = false;
        }
        finally
        {
            _settingCode = false;
        }
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

    // -- Pages ------------------------------------------------------------------------------------

    partial void OnSelectedPageChanged(PageItem? value)
    {
        _model = null;
        Items.Clear();
        SelectedItem = null;
        PreviewElements = null;
        if (value == null)
        {
            Preview = null;
            return;
        }

        var wasLoading = _loading;
        _loading = true;
        try
        {
            PageName = value.Page.Name;
            PageZoomIn = value.Page.Actions.GetValueOrDefault(ScreenEvent.ZoomIn, "");
            PageZoomOut = value.Page.Actions.GetValueOrDefault(ScreenEvent.ZoomOut, "");
            try
            {
                _model = new PageEditModel(Target, value.Page.Xaml);
                foreach (var element in _model.Elements) Items.Add(new ElementItem(element));
                PageBackground = _model.RootBackground;
                PageNightBackground = _model.RootNightBackground;
                PreviewElements = _model.Elements;
            }
            catch (System.Xml.XmlException e)
            {
                Issues.Add($"page \"{value.Page.Id}\": {e.Message}");
            }
        }
        finally
        {
            _loading = wasLoading;
        }
        RequestPreview();
    }

    partial void OnPageNameChanged(string value)
    {
        if (_loading || SelectedPage == null) return;
        SelectedPage.Page.Name = value;
        SelectedPage.Refresh();
        SetCode(_document.ToAxaml());
    }

    partial void OnPageZoomInChanged(string value) => EditAction(value, a => Store(SelectedPage?.Page.Actions, ScreenEvent.ZoomIn, a));
    partial void OnPageZoomOutChanged(string value) => EditAction(value, a => Store(SelectedPage?.Page.Actions, ScreenEvent.ZoomOut, a));
    partial void OnDefaultZoomInChanged(string value) => EditAction(value, a => Store(_document.DefaultActions, ScreenEvent.ZoomIn, a));
    partial void OnDefaultZoomOutChanged(string value) => EditAction(value, a => Store(_document.DefaultActions, ScreenEvent.ZoomOut, a));

    private void EditAction(string text, Action<string?> store)
    {
        if (_loading) return;
        if (!string.IsNullOrWhiteSpace(text) && !ScreenAction.TryParse(text, out _))
        {
            Status = $"\"{text}\" isn't an action. {ActionHelp}";
            return;
        }
        store(string.IsNullOrWhiteSpace(text) ? null : text.Trim());
        SetCode(_document.ToAxaml());
        Status = "";
    }

    private static void Store(Dictionary<ScreenEvent, string>? actions, ScreenEvent screenEvent, string? action)
    {
        if (actions == null) return;
        if (action == null) actions.Remove(screenEvent);
        else actions[screenEvent] = action;
    }

    [RelayCommand]
    private void AddPage()
    {
        var page = new ScreenPage
        {
            Id = NextPageId(),
            Name = "Page " + (Pages.Count + 1),
            Xaml = ScreenLayoutMigration.ToDocument(new ScreenLayout { Target = Target, Background = "#000000" }).Pages[0].Xaml,
        };
        _document.Pages.Add(page);
        var item = new PageItem(page);
        Pages.Add(item);
        SelectedPage = item;
        SetCode(_document.ToAxaml());
    }

    [RelayCommand]
    private void DeletePage()
    {
        if (SelectedPage == null) return;
        if (Pages.Count <= 1)
        {
            Status = "A screen needs at least one page.";
            return;
        }
        var item = SelectedPage;
        _document.Pages.Remove(item.Page);
        Pages.Remove(item);
        SelectedPage = Pages.FirstOrDefault();
        SetCode(_document.ToAxaml());
    }

    [RelayCommand]
    private void MovePageUp() => MovePage(-1);

    [RelayCommand]
    private void MovePageDown() => MovePage(1);

    private void MovePage(int direction)
    {
        if (SelectedPage == null) return;
        var item = SelectedPage;
        var index = Pages.IndexOf(item);
        var target = index + direction;
        if (target < 0 || target >= Pages.Count) return;
        Pages.Move(index, target);
        _document.Pages.RemoveAt(index);
        _document.Pages.Insert(target, item.Page);
        SelectedPage = item;
        SetCode(_document.ToAxaml());
    }

    private string NextPageId()
    {
        for (var i = Pages.Count + 1; ; i++)
            if (_document.Find("page" + i) == null) return "page" + i;
    }

    // -- Preview ----------------------------------------------------------------------------------

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

    partial void OnPreviewContextChanged(ScreenContext value) => RequestPreview();

    private async void RequestPreview()
    {
        if (SelectedPage == null) return;
        var renderer = _renderers[(int)Target] ??= new PreviewRenderer(Target);
        var page = SelectedPage.Page;
        var snapshot = ScreenSnapshot.Of(PreviewContext, page.Id, Math.Max(Pages.IndexOf(SelectedPage), 0), Math.Max(Pages.Count, 1));
        var bitmap = await renderer.RenderAsync(page, snapshot);
        if (bitmap != null) Preview = bitmap;
        else if (renderer.LastError != null) Status = $"Can't draw this page: {renderer.LastError}";
    }

    /// <summary>Call after any visual edit: stores the page XAML, refreshes the code view and the picture.</summary>
    private void PageEdited()
    {
        if (_loading || _model == null || SelectedPage == null) return;
        SelectedPage.Page.Xaml = _model.ToXaml();
        SetCode(_document.ToAxaml());
        RequestPreview();
    }

    // -- Canvas <-> selection ---------------------------------------------------------------------

    /// <summary>The canvas selected (or deselected) an element by touching it.</summary>
    public void Select(EditElement? element)
    {
        SelectedItem = element == null ? null : Items.FirstOrDefault(i => i.Element == element);
    }

    /// <summary>The canvas dragged the selected element: apply it (kept inside the shown area) and show the numbers.</summary>
    public void OnBoundsDragged(EditElement element, float x, float y, float width, float height)
    {
        if (_model == null) return;
        _model.SetBounds(element, x, y, width, height);
        EchoGeometry(element);
        PageEdited();
    }

    private void EchoGeometry(EditElement element)
    {
        if (SelectedItem?.Element != element) return;
        var wasLoading = _loading;
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
            _loading = wasLoading;
        }
    }

    partial void OnSelectedItemChanged(ElementItem? value)
    {
        OnPropertyChanged(nameof(SelectedElement));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsTextSelected));
        OnPropertyChanged(nameof(IsImageSelected));
        OnPropertyChanged(nameof(IsEmbeddedImageSelected));
        OnPropertyChanged(nameof(IsProgressSelected));
        OnPropertyChanged(nameof(HasFill));
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
            SelText = e.Template;
            SelFontSize = e.FontSize;
            SelBold = e.Bold;
            SelAlign = e.Align;
            SelColor = e.Color;
            SelNightColor = e.NightColor ?? "";
            SelBackground = e.Background ?? "";
            SelNightBackground = e.NightBackground ?? "";
            SelFit = e.Stretch switch { "UniformToFill" => ScreenImageFit.Cover, "Fill" => ScreenImageFit.Stretch, _ => ScreenImageFit.Contain };
            SelOrigin = e.ImageOrigin;
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    // -- Field edits write through to the element ---------------------------------------------------

    private void Edit(Action<EditElement> change)
    {
        if (_loading || SelectedItem == null) return;
        change(SelectedItem.Element);
        SelectedItem.Refresh();
        PageEdited();
    }

    /// <summary>A position or size edit: kept inside the area the bike shows, with the fields corrected to what was applied.</summary>
    private void EditGeometry(Func<EditElement, (float X, float Y, float W, float H)> change)
    {
        if (_loading || SelectedItem == null || _model == null) return;
        var element = SelectedItem.Element;
        var (x, y, w, h) = change(element);
        _model.SetBounds(element, x, y, w, h);
        EchoGeometry(element);
        PageEdited();
    }

    partial void OnSelNameChanged(string value) => Edit(e => e.Name = value);
    partial void OnSelXChanged(double value) => EditGeometry(e => ((float)value, e.Y, e.Width, e.Height));
    partial void OnSelYChanged(double value) => EditGeometry(e => (e.X, (float)value, e.Width, e.Height));
    partial void OnSelWidthChanged(double value) => EditGeometry(e => (e.X, e.Y, (float)value, e.Height));
    partial void OnSelHeightChanged(double value) => EditGeometry(e => (e.X, e.Y, e.Width, (float)value));
    partial void OnSelShowWhenChanged(ScreenShowWhen value) => Edit(e => e.ShowWhen = value);
    partial void OnSelTextChanged(string value) => Edit(e => e.Template = value);
    partial void OnSelFontSizeChanged(double value) => Edit(e => e.FontSize = (float)value);
    partial void OnSelBoldChanged(bool value) => Edit(e => e.Bold = value);
    partial void OnSelAlignChanged(ScreenTextAlign value) => Edit(e => e.Align = value);
    partial void OnSelColorChanged(string value) => Edit(e => e.Color = value);
    partial void OnSelNightColorChanged(string value) => Edit(e => e.NightColor = value);
    partial void OnSelBackgroundChanged(string value) => Edit(e => e.Background = value);
    partial void OnSelNightBackgroundChanged(string value) => Edit(e => e.NightBackground = value);

    partial void OnSelOriginChanged(ImageOrigin value)
    {
        Edit(e => e.ImageOrigin = value);
        OnPropertyChanged(nameof(IsEmbeddedImageSelected));
    }

    partial void OnSelFitChanged(ScreenImageFit value) =>
        Edit(e => e.Stretch = value switch { ScreenImageFit.Cover => "UniformToFill", ScreenImageFit.Stretch => "Fill", _ => "Uniform" });

    partial void OnPageBackgroundChanged(string value)
    {
        if (_loading || _model == null) return;
        _model.RootBackground = value;
        PageEdited();
    }

    partial void OnPageNightBackgroundChanged(string value)
    {
        if (_loading || _model == null) return;
        _model.RootNightBackground = value;
        PageEdited();
    }

    // -- Element commands ------------------------------------------------------------------------------

    private void AfterAdd(EditElement element)
    {
        if (_model == null) return;
        var item = new ElementItem(element);
        Items.Add(item);
        SelectedItem = item;
        PageEdited();
        OnPropertyChanged(nameof(PreviewElements));
    }

    [RelayCommand]
    private void AddText()
    {
        if (_model != null) AfterAdd(_model.AddText());
    }

    [RelayCommand]
    private void AddBox()
    {
        if (_model != null) AfterAdd(_model.AddBox());
    }

    [RelayCommand]
    private void AddAlbumArt()
    {
        if (_model != null) AfterAdd(_model.AddAlbumArt());
    }

    [RelayCommand]
    private void AddProgress()
    {
        if (_model != null) AfterAdd(_model.AddProgress());
    }

    /// <summary>Adds an image element from a picked file (any format Skia can decode); big pictures are shrunk first.</summary>
    public void AddImage(byte[] file, string name)
    {
        if (_model == null) return;
        var data = PrepareImage(file);
        if (data == null)
        {
            Status = "Can't use that image.";
            return;
        }

        var element = _model.AddImage(Convert.ToBase64String(data));
        if (!string.IsNullOrWhiteSpace(name)) element.Name = System.IO.Path.GetFileNameWithoutExtension(name);
        AfterAdd(element);
        Status = "";
    }

    /// <summary>Replaces the selected image element's picture.</summary>
    public void ReplaceImage(byte[] file)
    {
        if (SelectedItem == null || SelectedItem.Element.Kind != EditKind.Image) return;
        var data = PrepareImage(file);
        if (data == null)
        {
            Status = "Can't use that image.";
            return;
        }
        SelectedItem.Element.ImageData = Convert.ToBase64String(data);
        Status = "";
        PageEdited();
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
        if (_model == null || SelectedItem == null) return;
        AfterAdd(_model.Duplicate(SelectedItem.Element));
    }

    [RelayCommand]
    private void DeleteElement()
    {
        if (_model == null || SelectedItem == null) return;
        var item = SelectedItem;
        _model.Delete(item.Element);
        Items.Remove(item);
        SelectedItem = null;
        PageEdited();
    }

    [RelayCommand]
    private void MoveForward() => MoveSelected(1);

    [RelayCommand]
    private void MoveBackward() => MoveSelected(-1);

    private void MoveSelected(int direction)
    {
        if (_model == null || SelectedItem == null) return;
        var item = SelectedItem;
        var index = Items.IndexOf(item);
        var target = Math.Clamp(index + direction, 0, Items.Count - 1);
        if (target == index) return;
        _model.Move(item.Element, direction);
        Items.Move(index, target);
        SelectedItem = item;
        PageEdited();
    }

    // -- Code tab --------------------------------------------------------------------------------------

    partial void OnCodeChanged(string value)
    {
        if (!_settingCode) _codeDirty = true;
    }

    partial void OnSelectedTabChanged(int value)
    {
        // Leaving the Code tab after typing in it: read what was typed (a bad edit stays on the Code tab, shown as problems).
        if (value != CodeTab && _codeDirty) CheckCode();
    }

    [RelayCommand]
    private void CheckCode()
    {
        Issues.Clear();
        ScreenDocument parsed;
        try
        {
            parsed = ScreenDocument.Parse(Code);
        }
        catch (ScreenDocumentException e)
        {
            Issues.Add(e.Message);
            Status = "The code has problems.";
            return;
        }

        foreach (var page in parsed.Pages)
            foreach (var issue in ScreenXamlValidator.Validate(page.Xaml))
                Issues.Add($"page \"{page.Id}\": {issue}");
        if (Issues.Count > 0)
        {
            Status = "The code has problems.";
            return;
        }

        // Good: it replaces the working document and the visual editor re-reads it.
        parsed.Target = Target;
        _document = parsed;
        var keep = SelectedPage?.Page.Id;
        _loading = true;
        try
        {
            LoadPages();
        }
        finally
        {
            _loading = false;
        }
        SelectedPage = Pages.FirstOrDefault(p => p.Page.Id == keep) ?? Pages.FirstOrDefault();
        _codeDirty = false;
        Status = "Code is valid.";
    }

    // -- Save / load ------------------------------------------------------------------------------------

    [RelayCommand]
    private void Apply()
    {
        if (_codeDirty)
        {
            CheckCode();
            if (Issues.Count > 0) return;
        }

        var issues = _controller.SaveScreenAxaml(Target, _document.ToAxaml());
        if (issues.Count > 0)
        {
            Issues.Clear();
            foreach (var issue in issues) Issues.Add(issue.ToString());
            Status = "Not saved: fix the problems first.";
            return;
        }

        Issues.Clear();
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
        _controller.ResetScreenDocument(Target);
        LoadTarget();
        Status = "Back to the built-in screen.";
    }

    public string ExportAxaml() => _document.ToAxaml();

    /// <summary>Replaces the working screen with an imported file (not saved until Apply).</summary>
    public void ImportAxaml(string text)
    {
        SetCode(text);
        _codeDirty = true;
        CheckCode();
        if (Issues.Count == 0) Status = "Imported. Press Apply to use it.";
    }
}
