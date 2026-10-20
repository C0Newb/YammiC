using System;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using CNCO.YccuSdk;

namespace YammiC.Android;

/// <summary>
/// Foreground service that keeps the process (and so the BLE connection) alive when the screen is off
/// or the app is backgrounded. The connection itself lives in the SDK's BikeService (see <see cref="BikeHost"/>),
/// not in the Activity or here.
///
/// Lifecycle:
///  - Started by Sync() when a connection attempt begins, and stopped when it ends (nothing runs while idle).
///  - The notification's Disconnect action (or StopSelf) also ends it.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public class BikeForegroundService : Service
{
    public const string ActionDisconnect = "co.cnewb.yammic.DISCONNECT";
    private const string ChannelId = "yammic_ble";
    private const int NotificationId = 1;

    private YmcConnectionBroker? _broker;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        CreateNotificationChannel();
        StartForeground(NotificationId, BuildNotification(connected: false), ForegroundService.TypeConnectedDevice);

        // Keep the notification in sync with the controller's connection state.
        _broker = BikeHost.Service?.Controller;
        if (_broker != null) _broker.StateChanged += OnBrokerStateChanged;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionDisconnect)
        {
            BikeHost.Service?.Controller.Disconnect();
            StopSelf();
        }
        // Not sticky: a restarted service would have no connection to keep alive.
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        lock (typeof(BikeForegroundService)) _requested = false;
        if (_broker != null) _broker.StateChanged -= OnBrokerStateChanged;
        base.OnDestroy();
    }

    private void OnBrokerStateChanged(BikeDebugState state) => UpdateNotification(_broker?.IsBikeConnected == true);

    private void CreateNotificationChannel()
    {
        var channel = new NotificationChannel(ChannelId, "YammiC Connection", NotificationImportance.Low) // low = no sound, appears in shade
        {
            Description = "Keeps the CCU BLE connection alive in the background",
        };
        channel.SetShowBadge(false);
        (GetSystemService(NotificationService) as NotificationManager)?.CreateNotificationChannel(channel);
    }

    private void UpdateNotification(bool connected) =>
        (GetSystemService(NotificationService) as NotificationManager)?.Notify(NotificationId, BuildNotification(connected));

    private Notification BuildNotification(bool connected)
    {
        var openApp = PendingIntent.GetActivity(
            this,
            0,
            new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop),
            PendingIntentFlags.Immutable)!;
        var disconnect = PendingIntent.GetService(
            this,
            0,
            new Intent(this, typeof(BikeForegroundService)).SetAction(ActionDisconnect),
            PendingIntentFlags.Immutable)!;

        return new Notification.Builder(this, ChannelId)
            .SetContentTitle("YammiC")!
            .SetContentText(connected ? "Connected to CCU" : "Connecting to CCU...")!
            .SetSmallIcon(Resource.Drawable.Icon)!
            .SetContentIntent(openApp)!
            .SetOngoing(true)!
            .AddAction(new Notification.Action.Builder(null, "Disconnect", disconnect).Build())!
            .Build()!;
    }

    private static bool _requested;

    /// <summary>
    /// Runs the service exactly while a connection is being made or is up, so there is no notification while idle. Idle
    /// listening for the bike needs no service: the manifest receiver (BikeBluetoothReceiver) wakes the app instead.
    /// </summary>
    public static void Sync(Context context, YmcConnectionBroker broker)
    {
        var wanted = broker.IsBikeConnected || broker.IsConnecting;
        try
        {
            lock (typeof(BikeForegroundService))
            {
                if (wanted == _requested) return;
                _requested = wanted;
            }
            if (wanted) context.StartForegroundService(new Intent(context, typeof(BikeForegroundService)));
            else context.StopService(new Intent(context, typeof(BikeForegroundService)));
        }
        catch (Exception)
        {
            // Android refuses to start a foreground service from the background (or before the Bluetooth
            // permissions are granted). The connection still runs, just without the keep-alive.
            lock (typeof(BikeForegroundService)) _requested = false;
        }
    }
}
