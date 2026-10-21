using Android.Content;
using CNCO.YccuSdk.Logging;

namespace CNCO.YccuSdk.Platform.Droid;

public static class AndroidYmcPlatform
{
    /// <summary>
    /// Builds the Android <see cref="YmcPlatform"/>; credentials/device name are stored under the app's files dir.
    /// </summary>
    public static YmcPlatform Create(Context context)
    {
        var phone = new AndroidPhoneStatusProvider(context);
        var storagePath = Path.Combine(context.FilesDir!.AbsolutePath, "yammic.session.json");
        return new YmcPlatform
        {
            Bluetooth = new AndroidBluetoothConnectionManager(context),
            PhoneStatus = phone,
            Volume = phone,
            Storage = new JsonFileKeyValueStore(storagePath),
        };
    }
}

public class LogcatSink : ILogSink
{
    public void Write(LogLevel level, string tag, string message)
    {
        switch (level)
        {
            case LogLevel.Debug: Android.Util.Log.Debug(tag, message); break;
            case LogLevel.Info: Android.Util.Log.Info(tag, message); break;
            case LogLevel.Warn: Android.Util.Log.Warn(tag, message); break;
            default: Android.Util.Log.Error(tag, message); break;
        }
    }
}
