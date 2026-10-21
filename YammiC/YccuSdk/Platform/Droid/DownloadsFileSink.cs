using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Android.Content;
using Android.OS;
using Android.Provider;
using CNCO.YccuSdk.Logging;

namespace CNCO.YccuSdk.Platform.Droid;

/// <summary>
/// Appends every log line to a new <c>yammic-YYYY_MM_DD-HH_mm_ss.log</c> (see <see cref="LogFileNames"/>) in <c>Downloads/YammiC</c> (the Kotlin app's
/// DownloadsLogWriter), created through MediaStore so it needs no storage permission. Writes happen on a background
/// task so logging never blocks the caller; failures are swallowed, since logging is best effort.
/// </summary>
public sealed class DownloadsFileSink : ILogSink
{
    public const string Label = "Downloads/YammiC";

    private readonly Context _context;
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    public DownloadsFileSink(Context context)
    {
        _context = context.ApplicationContext ?? context;
        _ = Task.Run(WriteLoopAsync);
    }

    public void Write(LogLevel level, string tag, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        _lines.Writer.TryWrite($"[{timestamp}] {level.ToString().ToUpperInvariant()[0]}/{tag}: {message}\n");
    }

    private async Task WriteLoopAsync()
    {
        Stream? output = null;
        try
        {
            output = OpenLogFile();
            await foreach (var line in _lines.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(line);
                    await output.WriteAsync(bytes).ConfigureAwait(false);
                    await output.FlushAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Best effort.
                }
            }
        }
        catch (Exception)
        {
            // Couldn't create the file (e.g. storage unavailable): drop logs rather than queue forever.
            _lines.Writer.TryComplete();
        }
        finally
        {
            output?.Dispose();
        }
    }

    private Stream OpenLogFile()
    {
        var resolver = _context.ContentResolver ?? throw new InvalidOperationException("No content resolver");
        var fileName = LogFileNames.For(DateTime.Now);

        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, "text/plain");
        values.Put(MediaStore.IMediaColumns.RelativePath, Android.OS.Environment.DirectoryDownloads + "/YammiC");

        var uri = resolver.Insert(MediaStore.Downloads.ExternalContentUri!, values)
            ?? throw new InvalidOperationException("Unable to create log file");
        return resolver.OpenOutputStream(uri, "wa") ?? throw new InvalidOperationException("Unable to open log file");
    }
}
