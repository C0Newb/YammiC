using Android.Bluetooth;
using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Navigation;
using Java.Util;

namespace CNCO.YccuSdk.Platform.Droid;

/// <summary>
/// A <see cref="INavByteChannel"/> over a Bluetooth RFCOMM (SPP) link to the CCU bike device.
/// Single responsibility: move bytes; it owns no protocol knowledge.
///
/// Ported from Pillion's RfcommByteChannel.
/// </summary>
public class NavRfcommChannel : INavByteChannel
{
    // NaviLite SPP service UUID (0x7220).
    private static readonly UUID SppUuid = UUID.FromString("00007220-0000-1000-8000-00805F9B34FB")!;

    private readonly Logger _log;
    private BluetoothSocket? _socket;
    private System.IO.Stream? _input;
    private System.IO.Stream? _output;

    public NavRfcommChannel(Logger? logger = null)
    {
        _log = (logger ?? new Logger("NavRfcommChannel", Array.Empty<ILogSink>())).Section("NavRfcommChannel");
    }

    // BLUETOOTH_CONNECT is requested by the Activity before start.
    public void Open()
    {
        var adapter = BluetoothAdapter.DefaultAdapter ?? throw new InvalidOperationException("no Bluetooth adapter");
        // Also worth checking: this app opens its own Classic-BT SPP RFCOMM socket for VPB
        // (AndroidBluetoothConnectionManager, UUID 8911) alongside this one (UUID 7220) - two
        // simultaneous RFCOMM links to the same remote device. If both drop together in the
        // logs, that's the CCU's Classic-BT stack, not this channel.
        var ccu = adapter.BondedDevices?.FirstOrDefault(d =>
            d.Name != null && (d.Name.StartsWith("YCCU") || d.Name.Contains("CCU")))
            ?? throw new InvalidOperationException("CCU device (YCCU...) is not paired");
        _log.Debug($"connecting to {ccu.Name} ({ccu.Address})");
        // cancelDiscovery() needs BLUETOOTH_SCAN on Android 12+; it's only a connect-speed
        // optimization (nothing is discovering here), so ignore it if the permission is absent.
        try
        {
            adapter.CancelDiscovery();
        }
        catch (Exception)
        {
        }
        var s = ccu.CreateInsecureRfcommSocketToServiceRecord(SppUuid)!;
        s.Connect();
        _socket = s;
        _input = s.InputStream;
        _output = s.OutputStream;
        _log.Debug("connected");
    }

    public void Write(byte[] bytes)
    {
        var output = _output ?? throw new InvalidOperationException("channel not open");
        output.Write(bytes, 0, bytes.Length);
        output.Flush();
    }

    public int Read(byte[] buffer) =>
        (_input ?? throw new InvalidOperationException("channel not open")).Read(buffer, 0, buffer.Length) is var read && read > 0 ? read : -1;

    public void Close()
    {
        _log.Debug("closing");
        try
        {
            _socket?.Close();
        }
        catch (Exception)
        {
        }
        _socket = null;
        _input = null;
        _output = null;
    }
}
