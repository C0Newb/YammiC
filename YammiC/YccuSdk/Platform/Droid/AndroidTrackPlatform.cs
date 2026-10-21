using Android.Content;
using Android.Locations;
using Android.OS;
using CNCO.YccuSdk.Tracks;
using AndroidLocation = Android.Locations.Location;

namespace CNCO.YccuSdk.Platform.Droid;

/// <summary>
/// Track and session files in the app's own folder (no storage permission needed).
/// </summary>
public sealed class AndroidTrackFileStore : ITrackFileStore
{
    private readonly string _root;

    public AndroidTrackFileStore(Context context)
    {
        _root = Path.Combine((context.GetExternalFilesDir(null) ?? context.FilesDir)!.AbsolutePath, "YammiC");
    }

    private string Folder(string folder)
    {
        var path = Path.Combine(_root, Path.GetFileName(folder));
        Directory.CreateDirectory(path);
        return path;
    }

    // Names never contain directories: keep only the last part.
    private string PathOf(string folder, string name) => Path.Combine(Folder(folder), Path.GetFileName(name));

    public IReadOnlyList<string> List(string folder) =>
        Directory.GetFiles(Folder(folder)).Select(Path.GetFileName).OfType<string>().OrderBy(n => n, StringComparer.Ordinal).ToList();

    public string? Read(string folder, string name)
    {
        var path = PathOf(folder, name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void Write(string folder, string name, string text) => File.WriteAllText(PathOf(folder, name), text);

    public void Append(string folder, string name, string text)
    {
        // Flushed to disk on every call so a crash leaves what was recorded.
        using var stream = new FileStream(PathOf(folder, name), FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream);
        writer.Write(text);
        writer.Flush();
        stream.Flush(true);
    }

    public void Delete(string folder, string name)
    {
        var path = PathOf(folder, name);
        if (File.Exists(path)) File.Delete(path);
    }
}

/// <summary>
/// GPS fixes from the platform location manager (GPS provider only, so no Google Play Services are needed).
/// </summary>
public sealed class AndroidLocationSource : ILocationSource
{
    /// <summary>
    /// Set by the Activity: asks the user for location access (true = the "all the time" step).
    /// </summary>
    public static Action<bool>? PermissionRequester { get; set; }

    private readonly Context _context;
    private readonly LocationManager? _manager;
    private readonly Listener _listener;
    private HandlerThread? _thread;

    public AndroidLocationSource(Context context)
    {
        _context = context;
        _manager = context.GetSystemService(Context.LocationService) as LocationManager;
        _listener = new Listener();
    }

    public bool HasPermission => AndroidPermissions.IsGranted(_context, Android.Manifest.Permission.AccessFineLocation);

    public bool HasBackgroundPermission =>
        HasPermission && (Build.VERSION.SdkInt < BuildVersionCodes.Q || AndroidPermissions.IsGranted(_context, Android.Manifest.Permission.AccessBackgroundLocation));

    public GpsFix? LastKnown
    {
        get
        {
            if (_manager == null || !HasPermission) return null;
            try
            {
                var last = _manager.GetLastKnownLocation(LocationManager.GpsProvider) ?? _manager.GetLastKnownLocation(LocationManager.NetworkProvider);
                return last == null ? null : ToFix(last);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public void RequestPermission(bool background) => PermissionRequester?.Invoke(background);

    public void Start(double hz, Action<GpsFix> onFix)
    {
        if (_manager == null || !HasPermission) return;
        Stop();
        _listener.OnFix = onFix;
        _thread = new HandlerThread("yammic-gps");
        _thread.Start();
        var interval = (long)Math.Max(100, 1000.0 / Math.Max(hz, 0.1));
        _manager.RequestLocationUpdates(LocationManager.GpsProvider, interval, 0f, _listener, _thread.Looper!);
    }

    public void Stop()
    {
        try
        {
            _manager?.RemoveUpdates(_listener);
        }
        catch (Exception)
        {
            // Already stopped.
        }
        _listener.OnFix = null;
        _thread?.QuitSafely();
        _thread = null;
    }

    private static GpsFix ToFix(AndroidLocation l) => new(
        DateTimeOffset.FromUnixTimeMilliseconds(l.Time).UtcDateTime,
        l.Latitude,
        l.Longitude,
        l.HasSpeed ? l.Speed : 0,
        l.HasBearing ? l.Bearing : 0,
        l.HasAccuracy ? l.Accuracy : 99,
        l.ElapsedRealtimeNanos);

    private sealed class Listener : Java.Lang.Object, ILocationListener
    {
        public Action<GpsFix>? OnFix;

        public void OnLocationChanged(AndroidLocation location) => OnFix?.Invoke(ToFix(location));

        public void OnProviderDisabled(string provider)
        {
        }

        public void OnProviderEnabled(string provider)
        {
        }

        public void OnStatusChanged(string? provider, Availability status, Bundle? extras)
        {
        }
    }
}

/// <summary>
/// A short buzz when a session is armed.
/// </summary>
public sealed class AndroidVibrator : IVibrator
{
    private readonly Context _context;

    public AndroidVibrator(Context context) => _context = context;

    public void Vibrate()
    {
        try
        {
            Vibrator? vibrator = Build.VERSION.SdkInt >= BuildVersionCodes.S
                ? (_context.GetSystemService(Context.VibratorManagerService) as VibratorManager)?.DefaultVibrator
                : _context.GetSystemService(Context.VibratorService) as Vibrator;
            vibrator?.Vibrate(VibrationEffect.CreateWaveform(new long[] { 0, 300, 150, 300 }, -1));
        }
        catch (Exception)
        {
            // No vibrator, or not allowed: the VPB message still tells the rider.
        }
    }
}
