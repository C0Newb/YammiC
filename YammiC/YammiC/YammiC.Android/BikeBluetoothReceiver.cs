using System;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.OS;

namespace YammiC.Android;

/// <summary>
/// ACL_CONNECTED for any Bluetooth device. The SDK decides whether it is the bike (and logs each one). Declared in the manifest
/// so the system starts the app's process when the bike connects, even if the app isn't running.
/// </summary>
// Exported: the Bluetooth stack (not this app, not the OS core) sends ACL_CONNECTED, and a non-exported receiver only hears
// from the app itself and the OS core. The broadcast is protected, so only the system can actually send it.
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { BluetoothDevice.ActionAclConnected })]
public class BikeBluetoothReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != BluetoothDevice.ActionAclConnected) return;

        BluetoothDevice? device;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            device = (BluetoothDevice?)intent.GetParcelableExtra(BluetoothDevice.ExtraDevice, Java.Lang.Class.FromType(typeof(BluetoothDevice)));
#pragma warning disable CA1422
        else
            device = (BluetoothDevice?)intent.GetParcelableExtra(BluetoothDevice.ExtraDevice);
#pragma warning restore CA1422

        string? name = null;
        string? address = null;
        try
        {
            name = device?.Name;
            address = device?.Address;
        }
        catch (Java.Lang.SecurityException)
        {
            // BLUETOOTH_CONNECT isn't granted: nothing can be identified.
        }

        BikeHost.Service?.OnBluetoothDeviceConnected(name, address);
    }
}
