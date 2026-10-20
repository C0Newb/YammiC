#pragma warning disable CA1416, CA1422, CS0618

using Android;
using Android.App;
using Android.Content;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using Android.Provider;
using Android.Telecom;
using Android.Telephony;
using Android.Views;
using MediaController = Android.Media.Session.MediaController;
using Stream = Android.Media.Stream;
using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Packets.Ccu;

namespace CNCO.YccuSdk.Platform.Droid;

/// <summary>
/// Battery, cell signal and volume, read live from Android system services on every call.
/// </summary>
public sealed class AndroidPhoneStatusProvider : IYmcPhoneStatusProvider, IYmcVolumeController
{
    private readonly Context _context;
    private readonly AudioManager? _audioManager;
    private readonly TelephonyManager? _telephonyManager;

    public AndroidPhoneStatusProvider(Context context)
    {
        _context = context;
        _audioManager = context.GetSystemService(Context.AudioService) as AudioManager;
        _telephonyManager = context.GetSystemService(Context.TelephonyService) as TelephonyManager;
    }

    private Intent? BatteryIntent => _context.RegisterReceiver(null, new IntentFilter(Intent.ActionBatteryChanged));

    public int BatteryPercent
    {
        get
        {
            var intent = BatteryIntent;
            var level = intent?.GetIntExtra(BatteryManager.ExtraLevel, -1) ?? -1;
            var scale = intent?.GetIntExtra(BatteryManager.ExtraScale, -1) ?? -1;
            return level >= 0 && scale > 0 ? level * 100 / scale : 100;
        }
    }

    public bool IsCharging => (BatteryIntent?.GetIntExtra(BatteryManager.ExtraPlugged, 0) ?? 0) != 0;

    public float BatteryTemperatureCelsius => (BatteryIntent?.GetIntExtra(BatteryManager.ExtraTemperature, 0) ?? 0) / 10.0f;

    public YmcCellSignalLevel CellSignalLevel
    {
        get
        {
            if (_telephonyManager == null) return YmcCellSignalLevel.NoConnection;
            if (!AndroidPermissions.IsGranted(_context, Manifest.Permission.ReadPhoneState)) return YmcCellSignalLevel.NoConnection;
            var airplaneMode = Settings.System.GetInt(_context.ContentResolver, "airplane_mode_on", 0) != 0;
            if (airplaneMode) return YmcCellSignalLevel.NoConnection;
            if (_telephonyManager.SimState != SimState.Ready) return YmcCellSignalLevel.NoConnection;
            return (_telephonyManager.SignalStrength?.Level ?? 0) switch
            {
                0 => YmcCellSignalLevel.NoSignal,
                1 => YmcCellSignalLevel.Lv1,
                2 => YmcCellSignalLevel.Lv2,
                3 => YmcCellSignalLevel.Lv3,
                4 => YmcCellSignalLevel.Lv4,
                _ => YmcCellSignalLevel.NoConnection,
            };
        }
    }

    public int GetVolumePercent(BikeVolumeType type)
    {
        if (_audioManager == null) return 0;
        var stream = StreamFor(type);
        var current = _audioManager.GetStreamVolume(stream);
        var max = _audioManager.GetStreamMaxVolume(stream);
        return max > 0 ? current * 100 / max : 0;
    }

    // Goes through the same path as the hardware volume keys (so Bluetooth absolute volume reaches a
    // connected headset) and passes no flags, so the system volume panel stays hidden.
    public void Adjust(BikeVolumeType type, bool raise) =>
        _audioManager?.AdjustSuggestedStreamVolume(raise ? Android.Media.Adjust.Raise : Android.Media.Adjust.Lower, StreamFor(type), (VolumeNotificationFlags)0);

    // Phone volume lives on the SCO stream while a Bluetooth headset carries call audio.
    private Stream StreamFor(BikeVolumeType type) =>
        type == BikeVolumeType.Media ? Stream.Music
        : _audioManager?.BluetoothScoOn == true ? (Stream)6 // STREAM_BLUETOOTH_SCO
        : Stream.VoiceCall;
}

/// <summary>Answers / ends calls through TelecomManager (needs ANSWER_PHONE_CALLS).</summary>
public sealed class AndroidCallController : IYmcCallController
{
    private readonly Context _context;
    private readonly TelecomManager? _telecomManager;

    public AndroidCallController(Context context)
    {
        _context = context;
        _telecomManager = context.GetSystemService(Context.TelecomService) as TelecomManager;
    }

    public bool CanControlCalls => AndroidPermissions.IsGranted(_context, Manifest.Permission.AnswerPhoneCalls);

    public void AcceptRingingCall() => _telecomManager?.AcceptRingingCall();

    public void EndCall()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.P) _telecomManager?.EndCall();
    }
}

/// <summary>Controls the active media session, falling back to media key events.</summary>
public sealed class AndroidMediaController : IYmcMediaController
{
    private readonly AudioManager? _audioManager;
    private readonly Func<MediaController?> _activeController;

    public AndroidMediaController(Context context, Func<MediaController?> activeController)
    {
        _audioManager = context.GetSystemService(Context.AudioService) as AudioManager;
        _activeController = activeController;
    }

    public bool TryTransportControl(CcuMusicControlRequest.Action action)
    {
        var controls = _activeController()?.GetTransportControls();
        if (controls == null) return false;
        switch (action)
        {
            case CcuMusicControlRequest.Action.Play: controls.Play(); break;
            case CcuMusicControlRequest.Action.Pause: controls.Pause(); break;
            case CcuMusicControlRequest.Action.NextTrack: controls.SkipToNext(); break;
            case CcuMusicControlRequest.Action.PreviousTrack: controls.SkipToPrevious(); break;
        }
        return true;
    }

    public void DispatchMediaKey(CcuMusicControlRequest.Action action)
    {
        var keyCode = action switch
        {
            CcuMusicControlRequest.Action.Play => Keycode.MediaPlay,
            CcuMusicControlRequest.Action.Pause => Keycode.MediaPause,
            CcuMusicControlRequest.Action.NextTrack => Keycode.MediaNext,
            CcuMusicControlRequest.Action.PreviousTrack => Keycode.MediaPrevious,
            _ => (Keycode?)null,
        };
        if (keyCode is not { } key) return;
        _audioManager?.DispatchMediaKeyEvent(new KeyEvent(KeyEventActions.Down, key));
        _audioManager?.DispatchMediaKeyEvent(new KeyEvent(KeyEventActions.Up, key));
    }
}
