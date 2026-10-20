#if WINDOWS_BLUETOOTH
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace CCU_Emulator.Transport.Platforms.Windows;

/// <summary>
/// The CCU's Classic Bluetooth link: connects to the phone's SPP server (UUID 00008911-…). The phone
/// must be paired with this PC in Windows Bluetooth settings first, and the phone app must be listening.
/// </summary>
internal sealed class WindowsSppClient : IDisposable
{
    public static readonly Guid SppServiceUuid = Guid.Parse("00008911-0000-1000-8000-00805f9b34fb");

    public event Action<byte[]>? DataReceived;
    public event Action<bool>? ConnectionChanged;
    public event Action<string>? Diagnostic;

    private BluetoothDevice? _device;
    private StreamSocket? _socket;
    private DataWriter? _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _up;

    public async Task ConnectAsync(string phoneAddress, CancellationToken ct)
    {
        var address = ParseAddress(phoneAddress);
        _device = await BluetoothDevice.FromBluetoothAddressAsync(address)
            ?? throw new InvalidOperationException($"Bluetooth device {phoneAddress} not found. Pair the phone with this PC in Windows settings first.");

        var services = await _device.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(SppServiceUuid), BluetoothCacheMode.Uncached);
        if (services.Error != BluetoothError.Success || services.Services.Count == 0)
            throw new InvalidOperationException($"The phone does not expose the SPP service ({services.Error}). Is the phone app running and listening?");

        var service = services.Services[0];
        _socket = new StreamSocket();
        await _socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName).AsTask(ct);
        _writer = new DataWriter(_socket.OutputStream);
        SetUp(true);
        Diagnostic?.Invoke($"SPP connected to {phoneAddress}");
        _ = ReadLoopAsync(_socket);
    }

    private async Task ReadLoopAsync(StreamSocket socket)
    {
        try
        {
            using var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
            while (true)
            {
                var loaded = await reader.LoadAsync(4096);
                if (loaded == 0) break;
                var buffer = new byte[loaded];
                reader.ReadBytes(buffer);
                DataReceived?.Invoke(buffer);
            }
        }
        catch (Exception e)
        {
            Diagnostic?.Invoke($"SPP read ended: {e.Message}");
        }
        finally
        {
            SetUp(false);
        }
    }

    public async Task WriteAsync(byte[] data)
    {
        var writer = _writer ?? throw new InvalidOperationException("SPP link is not connected");
        await _writeLock.WaitAsync();
        try
        {
            writer.WriteBytes(data);
            await writer.StoreAsync();
            await writer.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void SetUp(bool up)
    {
        if (_up == up) return;
        _up = up;
        ConnectionChanged?.Invoke(up);
    }

    private static ulong ParseAddress(string text)
    {
        try { return Convert.ToUInt64(text.Replace(":", "").Replace("-", ""), 16); }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            throw new InvalidOperationException($"'{text}' is not a Bluetooth address (expected AA:BB:CC:DD:EE:FF).");
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _socket?.Dispose();
        _device?.Dispose();
        _writer = null;
        _socket = null;
        _device = null;
        SetUp(false);
    }
}
#endif
