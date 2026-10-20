using SkiaSharp;

namespace CNCO.YccuSdk.Imaging;

/// <summary>
/// SkiaSharp helpers shared by the Virtual Pit Board and navigation image paths (the Kotlin version used
/// android.graphics Bitmap/Canvas for the same jobs).
/// </summary>
public static class YmcImaging
{
    /// <summary>Encodes <paramref name="bitmap"/> as JPEG with the given quality (0-100).</summary>
    public static byte[] EncodeJpeg(SKBitmap bitmap, int quality = 100)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality)
            ?? throw new InvalidOperationException("JPEG encoding failed");
        return data.ToArray();
    }

    /// <summary>Scales <paramref name="source"/> to exactly <paramref name="width"/> x <paramref name="height"/> (stretching).</summary>
    public static SKBitmap Scale(SKBitmap source, int width, int height)
    {
        var scaled = new SKBitmap(width, height);
        using var canvas = new SKCanvas(scaled);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, new SKRect(0, 0, width, height), LinearSampling);
        return scaled;
    }

    /// <summary>
    /// Scales <paramref name="source"/> to fit within <paramref name="width"/> x <paramref name="height"/> and centers it on a
    /// black canvas of exactly that size, preserving aspect ratio instead of stretching. <paramref name="zoom"/> scales the fitted image about the centre (above 1 crops the edges, below 1 shrinks it).
    /// </summary>
    public static SKBitmap Letterbox(SKBitmap source, int width, int height, float zoom = 1f)
    {
        var canvasBitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(canvasBitmap);
        canvas.Clear(SKColors.Black);

        var scale = Math.Min(width / (float)source.Width, height / (float)source.Height) * zoom;
        var scaledWidth = Math.Max((int)(source.Width * scale), 1);
        var scaledHeight = Math.Max((int)(source.Height * scale), 1);
        var left = (width - scaledWidth) / 2f;
        var top = (height - scaledHeight) / 2f;

        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, new SKRect(left, top, left + scaledWidth, top + scaledHeight), LinearSampling);
        return canvasBitmap;
    }

    private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);
}
