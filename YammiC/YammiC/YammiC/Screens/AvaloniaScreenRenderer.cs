using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CNCO.YccuSdk.Screens;
using SkiaSharp;

namespace CNCO.YammiC.Screens;

/// <summary>
/// Draws pages with Avalonia: the page's XAML is loaded at run time, bound to a <see cref="ScreenState"/>, laid out at the
/// target's design size and rendered into a <see cref="RenderTargetBitmap"/>. All Avalonia work happens on the UI thread.
/// Use one instance per consumer (the bike service, the editor preview): each caches one control per target.
/// </summary>
public sealed class AvaloniaScreenRenderer : IScreenRenderer
{
    private readonly Dictionary<ScreenTarget, ScreenState> _states = new();
    private readonly Dictionary<ScreenTarget, (string Key, Control Root)> _roots = new();

    public string? LastError { get; private set; }

    /// <summary>
    /// False where code can't be generated at run time (iOS, browser): pages can't be loaded there.
    /// </summary>
    public static bool IsSupported => RuntimeFeature.IsDynamicCodeSupported;

    public async Task<SKBitmap?> RenderAsync(ScreenTarget target, ScreenPage page, ScreenSnapshot snapshot, int width, int height)
    {
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(() => RenderOnUi(target, page, snapshot, width, height));
        }
        catch (Exception t)
        {
            LastError = $"{t.GetType().Name}: {t.Message}";
            return null;
        }
    }

    private SKBitmap? RenderOnUi(ScreenTarget target, ScreenPage page, ScreenSnapshot snapshot, int width, int height)
    {
        if (!IsSupported)
        {
            LastError = "Custom screens aren't available on this platform.";
            return null;
        }

        var root = RootFor(target, page);
        if (root == null) return null;

        if (!_states.TryGetValue(target, out var state)) _states[target] = state = new ScreenState();
        state.Apply(snapshot);
        root.DataContext = state;

        var (designWidth, designHeight) = ScreenSizes.DesignSize(target);
        root.Width = designWidth;
        root.Height = designHeight;
        root.Measure(new Size(designWidth, designHeight));
        root.Arrange(new Rect(0, 0, designWidth, designHeight));

        using var bitmap = new RenderTargetBitmap(new PixelSize(designWidth, designHeight), new Vector(96, 96));
        bitmap.Render(root);

        using var stream = new MemoryStream();
        bitmap.Save(stream); // PNG
        stream.Position = 0;
        var full = SKBitmap.Decode(stream);
        if (full == null)
        {
            LastError = "The rendered picture couldn't be read back.";
            return null;
        }

        LastError = null;
        if (full.Width == width && full.Height == height) return full;
        var scaled = full.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear));
        full.Dispose();
        return scaled;
    }

    /// <summary>
    /// The loaded control for <paramref name="page"/> (re-loaded only when the page or its XAML changed); null with <see cref="LastError"/> set if it won't load.
    /// </summary>
    private Control? RootFor(ScreenTarget target, ScreenPage page)
    {
        var key = page.Id + "|" + page.Xaml.GetHashCode();
        if (_roots.TryGetValue(target, out var cached) && cached.Key == key) return cached.Root;

        var issues = ScreenXamlValidator.Validate(page.Xaml);
        if (issues.Count > 0)
        {
            LastError = issues[0].ToString();
            return null;
        }

        try
        {
            var root = AvaloniaRuntimeXamlLoader.Parse<Control>(page.Xaml, typeof(AvaloniaScreenRenderer).Assembly);

            // The old page's widgets listen to the shared state; detaching it lets them (and the old page) go.
            if (_roots.TryGetValue(target, out var old)) old.Root.DataContext = null;

            _roots[target] = (key, root);
            return root;
        }
        catch (Exception t)
        {
            LastError = $"{t.GetType().Name}: {t.Message}";
            return null;
        }
    }
}
