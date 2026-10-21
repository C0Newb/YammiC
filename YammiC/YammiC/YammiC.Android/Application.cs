using System;
using System.IO;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using CNCO.YccuSdk;
using CNCO.YccuSdk.Bike;
using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Navigation;
using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Platform.Droid;
using CNCO.YammiC.Screens;
using YammiC.Services;

namespace YammiC.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        public override void OnCreate()
        {
            var startup = System.Diagnostics.Stopwatch.StartNew();
            base.OnCreate();

            var logger = new Logger("YammiC", new ILogSink[] { new LogcatSink(), new DownloadsFileSink(this) });

            // A crash otherwise leaves nothing in the log. These write the exception first (and give the file writer a moment).
            AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(logger, "unhandled", e.ExceptionObject as Exception);
            AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => LogCrash(logger, "unhandled (android)", e.Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => logger.Error("unobserved task exception", e.Exception);
            var platform = AndroidYmcPlatform.Create(this);
            var broker = new YmcConnectionBroker(
                platform,
                new YmcBasePacketHandler(logger),
                logger,
                logFileLabel: DownloadsFileSink.Label);
            broker.StateChanged += _ => BikeForegroundService.Sync(this, broker);
            var navigation = new NavigationSdk(broker, () => new NavRfcommChannel(logger), logger);

            // Call and media monitoring come from CNCO.Unify.Device (see UnifyMonitorAdapters.cs).
            var mediaSource = new UnifyMediaSource();
            var service = new BikeService(
                broker,
                navigation,
                platform,
                logger,
                new BikeServiceOptions
                {
                    CallSource = new UnifyCallStateSource(),
                    MediaSource = mediaSource,
                    MediaController = mediaSource,
                    CallController = new AndroidCallController(this),
                    DemoVpbImage = ReadAsset("sandwich.jpg"),
                    DemoNavigationAnimation = ReadAsset("rotating_sandwich.gif"),
                    ReelsPlatform = new AndroidReelsPlatform(this, logger),
                    ScreenRenderer = AvaloniaScreenRenderer.IsSupported ? new AvaloniaScreenRenderer() : null,
                    PowerSource = new UnifyPowerSource(),
                });
            service.Start();

            BikeHost.Service = service;
            App.Broker = broker;
            App.Controller = new BrokerBikeController(service);

            // Track Timer: GPS, tracks and sessions. The foreground service follows it so a session survives the screen going off.
            var tracks = new CNCO.YccuSdk.Tracks.TrackTimerService(
                new CNCO.YccuSdk.Tracks.BikeServiceTrackLink(service, logger),
                new AndroidTrackFileStore(this),
                new AndroidLocationSource(this),
                new AndroidVibrator(this),
                platform.Storage,
                logger);
            BikeHost.TrackTimer = tracks;
            App.TrackTimer = tracks;
            tracks.Changed += () => BikeForegroundService.Sync(this, broker);
            tracks.Start();

            // Also listen while the process is alive (the manifest receiver covers the process not running). Either one triggers a
            // connect; the second is ignored because one is already under way.
            try
            {
                var filter = new IntentFilter(BluetoothDevice.ActionAclConnected);
                var receiver = new BikeBluetoothReceiver();
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu) RegisterReceiver(receiver, filter, ReceiverFlags.Exported);
                else RegisterReceiver(receiver, filter);
                logger.Debug("bluetooth: listening for the bike connecting");
                logger.Debug($"startup: application ready in {startup.ElapsedMilliseconds} ms");
            }
            catch (Exception t)
            {
                logger.Debug($"bluetooth: can't listen for the bike connecting: {t.Message}");
            }
        }

        private static void LogCrash(Logger logger, string kind, Exception? exception)
        {
            logger.Error($"CRASH {kind}: {exception?.GetType().FullName}: {exception?.Message}", exception ?? new Exception("unknown"));
            System.Threading.Thread.Sleep(500);
        }

        private byte[]? ReadAsset(string name)
        {
            try
            {
                using var stream = Assets!.Open(name);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }
            catch (Exception)
            {
                return null;
            }
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
            .WithInterFont();
        }
    }
}
