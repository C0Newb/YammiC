using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CNCO.YccuSdk.Screens;
using SkiaSharp;

namespace CNCO.YammiC.Screens;

/// <summary>
/// The editor's own renderer (separate from the bike's so previewing never disturbs it). Renders at design size and returns an
/// Avalonia bitmap. Requests made while a render runs are coalesced: only the latest one is drawn next.
/// </summary>
public sealed class PreviewRenderer
{
    private readonly ScreenTarget _target;
    private readonly AvaloniaScreenRenderer _renderer = new();
    private (ScreenPage Page, ScreenSnapshot Snapshot, TaskCompletionSource<Bitmap?> Result)? _waiting;
    private bool _running;

    public PreviewRenderer(ScreenTarget target)
    {
        _target = target;
    }

    public string? LastError => _renderer.LastError;

    /// <summary>Draws the page; null if it can't be drawn (<see cref="LastError"/> says why) or a newer request replaced this one.</summary>
    public Task<Bitmap?> RenderAsync(ScreenPage page, ScreenSnapshot snapshot)
    {
        var result = new TaskCompletionSource<Bitmap?>();
        _waiting?.Result.TrySetResult(null); // superseded
        _waiting = (page, snapshot, result);
        if (!_running) _ = Pump();
        return result.Task;
    }

    private async Task Pump()
    {
        _running = true;
        try
        {
            while (_waiting is { } next)
            {
                _waiting = null;
                var (width, height) = ScreenSizes.DesignSize(_target);
                using var bitmap = await _renderer.RenderAsync(_target, next.Page, next.Snapshot, width, height);
                if (bitmap == null)
                {
                    next.Result.TrySetResult(null);
                    continue;
                }
                using var image = SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                next.Result.TrySetResult(new Bitmap(new MemoryStream(encoded.ToArray())));
            }
        }
        catch (Exception)
        {
            _waiting?.Result.TrySetResult(null);
            _waiting = null;
        }
        finally
        {
            _running = false;
        }
    }
}
