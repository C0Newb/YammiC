using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.Provider;
using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Reels;
using SkiaSharp;

namespace CNCO.YccuSdk.Platform.Droid;

/// <summary>
/// Reels Mode on Android. The folder is a Storage Access Framework tree (what the system folder picker returns), so no storage
/// permission is needed. The sound plays through <see cref="MediaPlayer"/> (on the phone, looping, no picture of its own); the
/// pictures come from <see cref="MediaMetadataRetriever"/>, asked for the frame at the player's current position a few times
/// a second. That is slower than decoding straight to a surface, but a surface of our own (ImageReader) crashed the app on a
/// phone whose hardware decoder hands out frames the app can't read, and a native crash can't be caught.
/// </summary>
public sealed class AndroidReelsPlatform : IReelsPlatform
{
    // The navigation display is 480x240; frames are asked for at about that size so they stay cheap to fetch.
    private const float TargetWidth = 480f;
    private const float TargetHeight = 240f;

    private readonly Context _context;
    private readonly Logger _log;
    private readonly HandlerThread _thread;
    private readonly Handler _handler;
    private Playback? _playback;

    public AndroidReelsPlatform(Context context, Logger logger)
    {
        _context = context.ApplicationContext ?? context;
        _log = logger.Section("Reels");
        _thread = new HandlerThread("yammic-reels");
        _thread.Start();
        _handler = new Handler(_thread.Looper!);
    }

    public void RememberFolder(string folderUri)
    {
        try
        {
            _context.ContentResolver!.TakePersistableUriPermission(Android.Net.Uri.Parse(folderUri)!, ActivityFlags.GrantReadUriPermission);
            _log.Debug("keeping access to the reels folder");
        }
        catch (Exception t)
        {
            // Access then lasts only until the app restarts; picking the folder again restores it.
            _log.Debug($"can't keep access to the reels folder: {t.Message}");
        }
    }

    public Task<IReadOnlyList<ReelItem>> ListVideosAsync(string folderUri) => Task.Run<IReadOnlyList<ReelItem>>(() =>
    {
        var items = new List<ReelItem>();
        try
        {
            var tree = Android.Net.Uri.Parse(folderUri)!;
            var children = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, DocumentsContract.GetTreeDocumentId(tree))!;
            var columns = new[]
            {
                DocumentsContract.Document.ColumnDocumentId,
                DocumentsContract.Document.ColumnDisplayName,
                DocumentsContract.Document.ColumnMimeType,
            };
            using var cursor = _context.ContentResolver!.Query(children, columns, null, null, null);
            while (cursor != null && cursor.MoveToNext())
            {
                var mime = cursor.GetString(2);
                if (mime == null || !mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) continue;
                var uri = DocumentsContract.BuildDocumentUriUsingTree(tree, cursor.GetString(0))!;
                items.Add(new ReelItem(uri.ToString()!, cursor.GetString(1) ?? "video"));
            }
        }
        catch (Exception t)
        {
            _log.Debug($"can't list the reels folder: {t.Message}");
        }
        items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return items;
    });

    public void Play(ReelItem item, int maxFps, Action<SKBitmap> onFrame, Action<string> onError) =>
        _handler.Post(() => StartPlayback(item, maxFps, onFrame, onError));

    public void Stop() => _handler.Post(StopPlayback);

    // -- Runs on the reels thread ----------------------------------------------------

    private void StartPlayback(ReelItem item, int maxFps, Action<SKBitmap> onFrame, Action<string> onError)
    {
        StopPlayback();
        try
        {
            var uri = Android.Net.Uri.Parse(item.Uri)!;

            // The retriever stays open for the whole playback; the frame loop owns it and releases it when it ends.
            var retriever = new MediaMetadataRetriever();
            int width, height, rotation;
            try
            {
                retriever.SetDataSource(_context, uri);
                width = ReadInt(retriever, MetadataKey.VideoWidth);
                height = ReadInt(retriever, MetadataKey.VideoHeight);
                rotation = ReadInt(retriever, MetadataKey.VideoRotation);
            }
            catch
            {
                retriever.Dispose();
                throw;
            }
            if (width <= 0 || height <= 0)
            {
                retriever.Dispose();
                onError("no video");
                return;
            }
            _log.Debug($"video {width}x{height} rotation={rotation}");

            var playback = new Playback(retriever, width, height, rotation, Math.Max(maxFps, 1), onFrame, _log);
            _playback = playback;

            var player = new MediaPlayer();
            playback.Player = player;
            player.SetDataSource(_context, uri);
            player.Looping = true;
            player.SetAudioAttributes(new AudioAttributes.Builder()!
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Movie)!
                .Build()!);
            player.Prepared += (_, _) =>
            {
                try
                {
                    if (playback.Closed) return;
                    _log.Debug("prepared, starting");
                    RequestAudioFocus();
                    player.Start();
                    playback.StartFrameLoop();
                }
                catch (Exception t)
                {
                    _log.Debug($"start failed: {t.GetType().Name}: {t.Message}");
                }
            };
            player.Error += (_, e) =>
            {
                e.Handled = true;
                _log.Debug($"player error {e.What}/{e.Extra}");
                if (!playback.Closed) onError($"playback error {e.What}/{e.Extra}");
            };
            player.PrepareAsync();
        }
        catch (Exception t)
        {
            StopPlayback();
            onError(t.Message);
        }
    }

    private void StopPlayback()
    {
        var playback = _playback;
        _playback = null;
        if (playback == null) return;

        playback.Close(); // stops the frame loop, which then releases the retriever
        try
        {
            playback.Player?.Stop();
        }
        catch (Exception)
        {
            // Not started yet.
        }
        playback.Player?.Release();
        playback.Player?.Dispose();
        AbandonAudioFocus();
    }

    private static int ReadInt(MediaMetadataRetriever retriever, MetadataKey key) =>
        int.TryParse(retriever.ExtractMetadata(key), out var value) ? value : 0;

    // The videos have sound, so take audio focus: a music app pauses instead of playing over them.
    private void RequestAudioFocus()
    {
#pragma warning disable CA1422
        (_context.GetSystemService(Context.AudioService) as AudioManager)?.RequestAudioFocus(null, Android.Media.Stream.Music, AudioFocus.Gain);
#pragma warning restore CA1422
    }

    private void AbandonAudioFocus()
    {
#pragma warning disable CA1422
        (_context.GetSystemService(Context.AudioService) as AudioManager)?.AbandonAudioFocus(null);
#pragma warning restore CA1422
    }

    /// <summary>One playing video: a loop that fetches the picture at the player's position and passes it on.</summary>
    private sealed class Playback
    {
        private readonly MediaMetadataRetriever _retriever;
        private readonly int _outWidth;
        private readonly int _outHeight;
        private readonly long _minIntervalMs;
        private readonly Action<SKBitmap> _onFrame;
        private readonly Logger _log;
        private volatile bool _closed;

        public Playback(MediaMetadataRetriever retriever, int width, int height, int rotation, int maxFps, Action<SKBitmap> onFrame, Logger log)
        {
            _retriever = retriever;
            _minIntervalMs = 1000L / maxFps;
            _onFrame = onFrame;
            _log = log;

            // Frames come back upright, so size them from the upright dimensions.
            var upright = rotation is 90 or 270 ? (Width: height, Height: width) : (Width: width, Height: height);
            var fit = Math.Min(1f, Math.Min(TargetWidth / upright.Width, TargetHeight / upright.Height));
            _outWidth = Math.Max((int)(upright.Width * fit), 2);
            _outHeight = Math.Max((int)(upright.Height * fit), 2);
        }

        public MediaPlayer? Player { get; set; }

        public bool Closed => _closed;

        public void Close() => _closed = true;

        public void StartFrameLoop() => Task.Run(FrameLoop);

        private void FrameLoop()
        {
            var sent = 0;
            var failures = 0;
            long fetchMsTotal = 0;
            try
            {
                while (!_closed)
                {
                    var started = System.Environment.TickCount64;
                    try
                    {
                        var positionMs = Player?.CurrentPosition ?? 0;
                        using var frame = Fetch(positionMs * 1000L);
                        if (frame != null && !_closed)
                        {
                            var skia = ToSkia(frame);
                            fetchMsTotal += System.Environment.TickCount64 - started;
                            _onFrame(skia);
                            if (++sent % 25 == 1)
                                _log.Debug($"{sent} pictures sent, fetch+convert avg {fetchMsTotal / sent} ms");
                        }
                        else if (frame == null && ++failures <= 5)
                        {
                            _log.Debug("no picture for this position");
                        }
                    }
                    catch (Exception t)
                    {
                        // Skip the picture (and never let it escape: this runs on its own thread).
                        if (++failures <= 5) _log.Debug($"picture failed: {t.GetType().Name}: {t.Message}");
                    }

                    var wait = _minIntervalMs - (System.Environment.TickCount64 - started);
                    Thread.Sleep((int)Math.Max(wait, 20));
                }
            }
            finally
            {
                try
                {
                    _retriever.Release();
                    _retriever.Dispose();
                }
                catch (Exception)
                {
                    // Already gone.
                }
            }
        }

        private Bitmap? Fetch(long positionUs)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.OMr1)
                return _retriever.GetScaledFrameAtTime(positionUs, Option.Closest, _outWidth, _outHeight);

            // Older Android can't scale while decoding, so the frame comes at full size (the controller letterboxes it anyway).
            return _retriever.GetFrameAtTime(positionUs, Option.Closest);
        }

        private static SKBitmap ToSkia(Bitmap bitmap)
        {
            // The retriever normally gives ARGB_8888; anything else (e.g. RGB_565) is converted first.
            using var converted = bitmap.GetConfig() == Bitmap.Config.Argb8888 ? null : bitmap.Copy(Bitmap.Config.Argb8888!, false);
            var source = converted ?? bitmap;

            // A direct buffer, so what Java writes is what is read back. (ByteBuffer.Wrap(byte[]) hands Java a copy of the managed
            // array, and the pixels would never make it back: every picture came out blank.)
            using var buffer = Java.Nio.ByteBuffer.AllocateDirect(source.ByteCount)!;
            source.CopyPixelsToBuffer(buffer);
            buffer.Rewind();
            var bytes = new byte[source.ByteCount];
            buffer.Get(bytes);

            // Android's ARGB_8888 is laid out R, G, B, A in memory.
            var result = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, result.GetPixels(), Math.Min(bytes.Length, result.ByteCount));
            return result;
        }
    }
}
