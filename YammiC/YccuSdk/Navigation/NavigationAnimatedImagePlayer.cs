using CNCO.YccuSdk.Imaging;
using CNCO.YccuSdk.Logging;
using SkiaSharp;

namespace CNCO.YccuSdk.Navigation;

/// <summary>
/// Decodes an animated GIF/WebP and drives its playback. Every frame is decoded up front (SKCodec handles
/// the inter-frame dependencies), letterboxed via <c>letterbox</c>, JPEG-compressed, and then handed to <c>onFrame</c>
/// on the per-frame delay baked into the source file. Loops forever until <see cref="Stop"/> is called.
/// A source that isn't actually animated pushes a single frame and stops.
/// </summary>
internal class NavigationAnimatedImagePlayer
{
    private const int MinFrameDelayMs = 20;

    private readonly Logger _log;
    private readonly Func<SKBitmap, SKBitmap> _letterbox;
    private readonly Action<byte[]> _onFrame;
    private CancellationTokenSource? _cancellation;

    public NavigationAnimatedImagePlayer(Logger logger, Func<SKBitmap, SKBitmap> letterbox, Action<byte[]> onFrame)
    {
        _log = logger.Section("NavigationAnimatedImagePlayer");
        _letterbox = letterbox;
        _onFrame = onFrame;
    }

    /// <summary>Starts decoding/playing <paramref name="encodedImage"/>. Call <see cref="Stop"/> first if reusing.</summary>
    public void Start(byte[] encodedImage, int quality)
    {
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _ = Task.Run(() => DecodeAndPlayAsync(encodedImage, quality, cancellation.Token));
    }

    /// <summary>Stops playback. Safe to call more than once.</summary>
    public void Stop()
    {
        _cancellation?.Cancel();
        _cancellation = null;
    }

    private async Task DecodeAndPlayAsync(byte[] encodedImage, int quality, CancellationToken cancellationToken)
    {
        List<(byte[] Jpeg, int DelayMs)> frames;
        try
        {
            frames = DecodeFrames(encodedImage, quality);
        }
        catch (Exception t)
        {
            _log.Debug($"decodeAndPlay: failed to decode image: {t.Message}");
            return;
        }

        if (frames.Count == 0) return;
        if (frames.Count == 1)
        {
            _log.Debug("decodeAndPlay: image is not animated, pushing single frame");
            _onFrame(frames[0].Jpeg);
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (var (jpeg, delayMs) in frames)
                {
                    _onFrame(jpeg);
                    await Task.Delay(Math.Max(delayMs, MinFrameDelayMs), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }

    private List<(byte[] Jpeg, int DelayMs)> DecodeFrames(byte[] encodedImage, int quality)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(encodedImage))
            ?? throw new InvalidOperationException("unsupported image format");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var frameCount = Math.Max(codec.FrameCount, 1);
        var frameInfo = codec.FrameInfo;

        // Last frame index that uses each frame as its base, so decoded frames can be released as soon as nothing needs them.
        var lastNeededBy = new int[frameCount];
        Array.Fill(lastNeededBy, -1);
        for (var index = 0; index < frameCount && frameCount > 1; index++)
        {
            var required = frameInfo[index].RequiredFrame;
            if (required >= 0) lastNeededBy[required] = index;
        }

        var raw = new SKBitmap?[frameCount];
        var result = new List<(byte[] Jpeg, int DelayMs)>();
        try
        {
            for (var index = 0; index < frameCount; index++)
            {
                var bitmap = new SKBitmap(info);
                var options = new SKCodecOptions(index);
                var required = frameCount > 1 ? frameInfo[index].RequiredFrame : -1;
                if (required >= 0)
                {
                    // Frames may be deltas against an earlier frame: start from that frame's pixels.
                    raw[required]!.GetPixelSpan().CopyTo(bitmap.GetPixelSpan());
                    options = new SKCodecOptions(index, required);
                }

                var status = codec.GetPixels(info, bitmap.GetPixels(), options);
                if (status != SKCodecResult.Success && status != SKCodecResult.IncompleteInput)
                {
                    bitmap.Dispose();
                    throw new InvalidOperationException($"failed to decode frame {index}: {status}");
                }
                raw[index] = bitmap;

                using (var boxed = _letterbox(bitmap))
                {
                    result.Add((YmcImaging.EncodeJpeg(boxed, quality), frameCount > 1 ? frameInfo[index].Duration : 0));
                }

                // Release frames that no later frame depends on.
                for (var i = 0; i <= index; i++)
                {
                    if (raw[i] != null && lastNeededBy[i] <= index)
                    {
                        raw[i]!.Dispose();
                        raw[i] = null;
                    }
                }
            }
        }
        finally
        {
            foreach (var bitmap in raw) bitmap?.Dispose();
        }
        return result;
    }
}
