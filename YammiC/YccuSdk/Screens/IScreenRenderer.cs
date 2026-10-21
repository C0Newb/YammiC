using SkiaSharp;

namespace CNCO.YccuSdk.Screens;

/// <summary>Draws a page for the bike. Implemented with Avalonia in the UI project; never throws (returns null and sets <see cref="LastError"/>).</summary>
public interface IScreenRenderer
{
    /// <summary>Why the last call returned null; null after a success.</summary>
    string? LastError { get; }

    /// <summary>Renders <paramref name="page"/> showing <paramref name="snapshot"/>, scaled to <paramref name="width"/> x <paramref name="height"/>. The caller owns the bitmap.</summary>
    Task<SKBitmap?> RenderAsync(ScreenTarget target, ScreenPage page, ScreenSnapshot snapshot, int width, int height);
}
