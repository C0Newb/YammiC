using System.Linq;
using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace YammiC.Android;

[Activity(
    Label = "YammiC",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    private const int BluetoothPermissionRequest = 1;
    private const int OptionalPermissionRequest = 2;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RequestBluetoothPermissions();
    }

    protected override void OnResume()
    {
        base.OnResume();
        // The user may have just come back from granting notification access / permissions in settings.
        BikeHost.Service?.RestartMonitors();
    }

    // Needed to connect at all.
    private string[] RequiredPermissions =>
        Build.VERSION.SdkInt >= BuildVersionCodes.S
            ? new[] { Manifest.Permission.BluetoothScan, Manifest.Permission.BluetoothConnect, Manifest.Permission.AccessFineLocation }
            : new[] { Manifest.Permission.AccessFineLocation };

    // Nice to have: call relaying and answering, caller names, and the foreground service notification.
    private string[] OptionalPermissions =>
        Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? new[] { Manifest.Permission.ReadPhoneState, Manifest.Permission.ReadCallLog, Manifest.Permission.ReadContacts, Manifest.Permission.AnswerPhoneCalls, Manifest.Permission.PostNotifications }
            : new[] { Manifest.Permission.ReadPhoneState, Manifest.Permission.ReadCallLog, Manifest.Permission.ReadContacts, Manifest.Permission.AnswerPhoneCalls };

    private string[] Missing(string[] permissions) =>
        permissions.Where(p => CheckSelfPermission(p) != Permission.Granted).ToArray();

    private void RequestBluetoothPermissions()
    {
        var missing = Missing(RequiredPermissions);
        if (missing.Length == 0)
        {
            OnBluetoothPermissionsGranted();
            return;
        }
        RequestPermissions(missing, BluetoothPermissionRequest);
    }

    private void OnBluetoothPermissionsGranted()
    {
        App.Broker?.SetPermissionsGranted(true);
        RequestOptionalPermissions();
    }

    private void RequestOptionalPermissions()
    {
        var missing = Missing(OptionalPermissions);
        if (missing.Length > 0) RequestPermissions(missing, OptionalPermissionRequest);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        switch (requestCode)
        {
            case BluetoothPermissionRequest:
                var granted = Missing(RequiredPermissions).Length == 0;
                App.Broker?.SetPermissionsGranted(granted);
                if (granted) OnBluetoothPermissionsGranted();
                break;

            case OptionalPermissionRequest:
                // Call monitoring couldn't start before READ_PHONE_STATE was granted.
                BikeHost.Service?.RestartPhoneCallMonitor();
                break;
        }
    }
}
