using CNCO.YccuSdk.Imaging;
using SkiaSharp;

namespace CNCO.YccuSdk.Vpb;

/// <summary>
/// Builds VPB (Virtual Pit Board) image byte arrays from various sources.
///
/// The CCU expects a 600x306 JPEG with a 526x66 message strip pasted at (0, 163) on a black
/// background. These helpers handle the composition and JPEG encoding.
/// </summary>
public static class VpbImageBuilder
{
    private const int VehicleWidth = 600;
    private const int VehicleHeight = 306;
    private const int StripWidth = 526;
    private const int StripHeight = 66;
    private const int StripTop = 163;

    /// <summary>
    /// Passes through pre-encoded JPEG bytes. Use this when you already have a complete JPEG
    /// matching the CCU's expected format (600x306 with composed message strip).
    /// </summary>
    public static byte[] FromJpeg(byte[] jpegBytes) => jpegBytes;

    /// <summary>
    /// Builds a full VPB image from text and background color. Composes a 526x66 message strip
    /// with white text on <paramref name="backgroundColor"/>, then pastes it onto a 600x306 black canvas at
    /// position (0, 163), and compresses to JPEG (quality 100).
    /// </summary>
    public static byte[] FromText(string text, SKColor? backgroundColor = null)
    {
        using var boardStrip = new SKBitmap(StripWidth, StripHeight);
        using (var canvas = new SKCanvas(boardStrip))
        {
            canvas.Clear(backgroundColor ?? SKColors.Blue);
            using var font = new SKFont(SKTypeface.Default, 40f);
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            var metrics = font.Metrics;
            var y = (StripHeight / 2f) - (metrics.Descent + metrics.Ascent) / 2f;
            canvas.DrawText(text, StripWidth / 2f, y, SKTextAlign.Center, font, paint);
        }

        using var vehicleImage = new SKBitmap(VehicleWidth, VehicleHeight);
        using (var canvas = new SKCanvas(vehicleImage))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(boardStrip, 0f, StripTop);
        }

        return YmcImaging.EncodeJpeg(vehicleImage);
    }

    /// <summary>
    /// Converts a bitmap to JPEG bytes with default quality (100). Fits the bitmap inside 600x306, keeping its aspect ratio (black bars)
    /// (the CCU's expected VPB size).
    /// </summary>
    public static byte[] FromBitmap(SKBitmap bitmap) => FromBitmap(bitmap, jpegQuality: 100);

    /// <summary>
    /// Converts a bitmap to JPEG bytes with custom quality (0-100). Fits the bitmap inside 600x306, keeping its aspect ratio (black bars)
    /// (the CCU's expected VPB size).
    /// </summary>
    public static byte[] FromBitmap(SKBitmap bitmap, int jpegQuality)
    {
        using var scaled = YmcImaging.Letterbox(bitmap, VehicleWidth, VehicleHeight);
        return YmcImaging.EncodeJpeg(scaled, jpegQuality);
    }

    /// <summary>
    /// Decodes any supported image format (PNG, JPEG, ...) and converts it like <see cref="FromBitmap(SKBitmap, int)"/>.
    /// </summary>
    public static byte[]? FromEncodedImage(byte[] encodedImage, int jpegQuality = 100)
    {
        using var bitmap = SKBitmap.Decode(encodedImage);
        return bitmap == null ? null : FromBitmap(bitmap, jpegQuality);
    }
}
