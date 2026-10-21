using System.Runtime.CompilerServices;
using System.Text;
using SkiaSharp;

namespace CNCO.YccuSdk.Screens;

/// <summary>
/// Draws a <see cref="ScreenLayout"/> with SkiaSharp. Pure and UI-free, so the bike pushes use it from a background
/// service and the in-app editor uses the very same code for its preview.
/// </summary>
public static class ScreenRenderer
{
    // Decoded images, keyed by the layout's base64 string (same string instance => same bitmap), so a screen that
    // redraws every minute doesn't decode its pictures every time.
    private static readonly ConditionalWeakTable<string, SKBitmap?> ImageCache = new();

    /// <summary>
    /// Renders at <paramref name="width"/> x <paramref name="height"/>, scaling the layout's design space to fit.
    /// </summary>
    public static SKBitmap Render(ScreenLayout layout, ScreenContext context, int width, int height)
    {
        var (designWidth, designHeight) = ScreenSizes.DesignSize(layout.Target);
        var sx = width / (float)designWidth;
        var sy = height / (float)designHeight;
        var sText = Math.Min(sx, sy);

        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(ParseColor(Pick(context.Night, layout.Background, layout.NightBackground), SKColors.Black));

        if (Math.Abs(context.Zoom - 1f) > 0.001f)
        {
            canvas.Translate(width / 2f, height / 2f);
            canvas.Scale(context.Zoom);
            canvas.Translate(-width / 2f, -height / 2f);
        }

        foreach (var element in layout.Elements)
        {
            if (!IsVisible(element, context)) continue;

            var box = new SKRect(element.X * sx, element.Y * sy, (element.X + element.Width) * sx, (element.Y + element.Height) * sy);
            var background = Pick(context.Night, element.Background, element.NightBackground);
            if (!string.IsNullOrEmpty(background))
            {
                using var fill = new SKPaint { Color = ParseColor(background, SKColors.Transparent), Style = SKPaintStyle.Fill };
                canvas.DrawRect(box, fill);
            }

            canvas.Save();
            canvas.ClipRect(box);
            if (element.Kind == ScreenElementKind.Image) DrawImage(canvas, element, box);
            else DrawText(canvas, element, context, box, sText);
            canvas.Restore();
        }

        return bitmap;
    }

    /// <summary>
    /// A string that changes exactly when the picture would: the visible elements' resolved texts plus the theme. Lets the
    /// pushers skip re-sending an unchanged screen.
    /// </summary>
    public static string Signature(ScreenLayout layout, ScreenContext context)
    {
        var builder = new StringBuilder();
        builder.Append(context.Night ? 'N' : 'D').Append(context.Zoom.ToString("0.###")).Append('|');
        foreach (var element in layout.Elements)
        {
            if (!IsVisible(element, context)) continue;
            builder.Append(element.Id).Append('=');
            if (element.Kind == ScreenElementKind.Text) builder.Append(ScreenVariables.Resolve(element.Text, context));
            builder.Append('|');
        }
        return builder.ToString();
    }

    public static bool IsVisible(ScreenElement element, ScreenContext context) => element.ShowWhen switch
    {
        ScreenShowWhen.MusicPresent => context.HasMusic,
        ScreenShowWhen.MusicPlaying => context.IsPlaying,
        ScreenShowWhen.MusicPaused => context.HasMusic && !context.IsPlaying,
        ScreenShowWhen.NoMusic => !context.HasMusic,
        ScreenShowWhen.Day => !context.Night,
        ScreenShowWhen.Night => context.Night,
        _ => true,
    };

    private static void DrawText(SKCanvas canvas, ScreenElement element, ScreenContext context, SKRect box, float scale)
    {
        var text = ScreenVariables.Resolve(element.Text, context);
        if (string.IsNullOrEmpty(text)) return;

        using var typeface = ResolveTypeface(element.Bold, text);
        using var font = new SKFont(typeface, Math.Max(element.FontSize * scale, 1f));
        using var paint = new SKPaint
        {
            Color = ParseColor(Pick(context.Night, element.Color, element.NightColor), SKColors.White),
            IsAntialias = true,
        };

        text = Fit(text, font, box.Width);
        var width = font.MeasureText(text);
        var x = element.Align switch
        {
            ScreenTextAlign.Center => box.Left + (box.Width - width) / 2f,
            ScreenTextAlign.Right => box.Right - width,
            _ => box.Left,
        };
        var metrics = font.Metrics;
        var baseline = box.MidY - (metrics.Ascent + metrics.Descent) / 2f;
        canvas.DrawText(text, x, baseline, font, paint);
    }

    private static void DrawImage(SKCanvas canvas, ScreenElement element, SKRect box)
    {
        var image = LoadImage(element.ImageData);
        if (image == null) return;

        var scaleX = box.Width / image.Width;
        var scaleY = box.Height / image.Height;
        var (drawWidth, drawHeight) = element.Fit switch
        {
            ScreenImageFit.Stretch => (box.Width, box.Height),
            ScreenImageFit.Cover => (image.Width * Math.Max(scaleX, scaleY), image.Height * Math.Max(scaleX, scaleY)),
            _ => (image.Width * Math.Min(scaleX, scaleY), image.Height * Math.Min(scaleX, scaleY)),
        };
        var target = SKRect.Create(box.MidX - drawWidth / 2f, box.MidY - drawHeight / 2f, drawWidth, drawHeight);

        using var picture = SKImage.FromBitmap(image);
        canvas.DrawImage(picture, target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    private static SKBitmap? LoadImage(string? data)
    {
        if (string.IsNullOrEmpty(data)) return null;
        if (ImageCache.TryGetValue(data, out var cached)) return cached;

        SKBitmap? decoded = null;
        try
        {
            decoded = SKBitmap.Decode(Convert.FromBase64String(data));
        }
        catch (FormatException)
        {
            // Not valid base64: leave it blank.
        }
        ImageCache.AddOrUpdate(data, decoded);
        return decoded;
    }

    /// <summary>
    /// The system sans typeface, or - when it lacks a glyph the text needs (e.g. the play symbol) - one that has it.
    /// </summary>
    private static SKTypeface ResolveTypeface(bool bold, string text)
    {
        var typeface = SKTypeface.FromFamilyName(null, bold ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value < 0x80 || typeface.ContainsGlyph(rune.Value)) continue;
            var fallback = SKFontManager.Default.MatchCharacter(rune.Value);
            if (fallback != null)
            {
                typeface.Dispose();
                return fallback;
            }
            break;
        }
        return typeface;
    }

    /// <summary>
    /// Trims <paramref name="text"/> with "..." so it fits <paramref name="maxWidth"/> pixels.
    /// </summary>
    private static string Fit(string text, SKFont font, float maxWidth)
    {
        if (font.MeasureText(text) <= maxWidth) return text;
        const string ellipsis = "...";
        var fits = (int)font.BreakText(text, Math.Max(maxWidth - font.MeasureText(ellipsis), 0));
        return text[..Math.Clamp(fits, 0, text.Length)].TrimEnd() + ellipsis;
    }

    private static string? Pick(bool night, string? day, string? nightValue) =>
        night && !string.IsNullOrEmpty(nightValue) ? nightValue : day;

    public static SKColor ParseColor(string? value, SKColor fallback) =>
        !string.IsNullOrWhiteSpace(value) && SKColor.TryParse(value.Trim(), out var color) ? color : fallback;
}
