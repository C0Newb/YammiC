#pragma warning disable CA1416, CA1422, CS0618 // Supported-OS analysis (minSdk 26) and the pre-API-33 GATT overloads we deliberately still use.

using Android;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Packets;
using Java.Util;

namespace CNCO.YccuSdk.Platform.Droid;

/// <summary>
/// Android implementation of <see cref="IYmcBluetoothConnectionManager"/>: BLE GATT via android.bluetooth plus the
/// Classic-Bluetooth SPP server used for the credential handoff and Virtual Pit Board graphics.
/// </summary>
public sealed class AndroidBluetoothConnectionManager : IYmcBluetoothConnectionManager
{
    private static readonly UUID CccdUuid = UUID.FromString("00002902-0000-1000-8000-00805f9b34fb")!;
    private static readonly UUID SppServiceUuid = UUID.FromString("00008911-0000-1000-8000-00805f9b34fb")!;

    private readonly Context _context;
    private readonly BluetoothAdapter? _adapter;
    private readonly GattCallback _gattCallback;

    private BluetoothGatt? _activeGatt;
    private volatile BluetoothGatt? _connectingGatt;

    private TaskCompletionSource<bool>? _pendingConnect;
    private TaskCompletionSource<bool>? _pendingServices;
    private TaskCompletionSource<int>? _pendingMtu;
    private TaskCompletionSource<int>? _pendingWrite;
    private TaskCompletionSource<byte[]?>? _pendingRead;
    private TaskCompletionSource<int>? _pendingDescriptorWrite;
    private int? _lastConnectionStatus;
    private int? _lastConnectionState;

    private CancellationTokenSource? _sppCancellation;
    private BluetoothServerSocket? _sppServerSocket;
    private BluetoothSocket? _sppSocket;
    private readonly SemaphoreSlim _sppWriteMutex = new(1, 1);

    private readonly SemaphoreSlim _writeMutex = new(1, 1);

    public AndroidBluetoothConnectionManager(Context context)
    {
        _context = context;
        _adapter = (context.GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;
        _gattCallback = new GattCallback(this);
    }

    public YmcNotificationBus Notifications { get; } = new();

    public int NegotiatedMtu { get; private set; } = 23;

    public bool IsBluetoothEnabled => _adapter != null && _adapter.IsEnabled;

    public int BondedDeviceCount => _adapter?.BondedDevices?.Count ?? 0;

    public event Action? Disconnected;

    public event Action<string>? LogMessage;

    public event Action<string>? QuietLogMessage;

    private void OnLog(string message) => LogMessage?.Invoke(message);

    public void AssertConnectPermission()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            AndroidPermissions.AssertGranted(_context, Manifest.Permission.BluetoothConnect);
        }
    }

    // -- SPP -------------------------------------------------------------------

    public Task<YmcAuthentication?> OpenSppServerAsync()
    {
        var creds = new TaskCompletionSource<YmcAuthentication?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (_adapter == null)
            {
                OnLog("SPP: adapter null");
                creds.TrySetResult(null);
                return creds.Task;
            }
            var server = _adapter.ListenUsingRfcommWithServiceRecord("YammiC", SppServiceUuid)
                ?? throw new InvalidOperationException("ListenUsingRfcommWithServiceRecord returned null");
            _sppServerSocket = server;
            OnLog("SPP: server open, waiting for YCCU Classic BT connection");
            _sppCancellation = new CancellationTokenSource();
            var token = _sppCancellation.Token;
            _ = Task.Run(() =>
            {
                try
                {
                    var socket = server.Accept(30_000)!;
                    OnLog($"SPP: YCCU connected via Classic BT from {socket.RemoteDevice?.Address}");
                    try
                    {
                        server.Close();
                    }
                    catch (Exception)
                    {
                    }
                    _sppSocket = socket;
                    var pair = ReadSppBleConnectInfo(socket);
                    if (pair != null)
                    {
                        OnLog($"SPP: received ccuId={pair.CcuId} passKey=***");
                        creds.TrySetResult(pair);
                    }
                    else
                    {
                        creds.TrySetResult(null);
                    }
                    // Keep the socket open after the handoff: Virtual Pit Board graphic chunks
                    // (and any other OTA-socket traffic) are written to it for the rest of the
                    // session. Drain/log anything the CCU sends back on this channel - the reference
                    // SDK's replies for graphic chunks actually arrive over BLE, not here, but we
                    // don't want an unread stream to back up and stall the socket.
                    DrainSppSocket(socket, token);
                }
                catch (Exception e)
                {
                    OnLog($"SPP: {e.Message}");
                    creds.TrySetResult(null);
                }
            });
        }
        catch (Exception e)
        {
            OnLog($"SPP: server failed: {e.Message}");
            creds.TrySetResult(null);
        }
        return creds.Task;
    }

    private YmcAuthentication? ReadSppBleConnectInfo(BluetoothSocket socket)
    {
        try
        {
            var stream = socket.InputStream!;
            int b;
            do
            {
                b = stream.ReadByte();
                if (b == -1) return null;
            } while (b != 0x01);
            var lenBuf = new byte[4];
            var n = 0;
            while (n < 4)
            {
                var r = stream.Read(lenBuf, n, 4 - n);
                if (r <= 0) return null;
                n += r;
            }
            var len = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | lenBuf[3];
            if (len < 22)
            {
                OnLog($"SPP: payload too short ({len})");
                return null;
            }
            var payload = new byte[len];
            n = 0;
            while (n < len)
            {
                var r = stream.Read(payload, n, len - n);
                if (r <= 0) return null;
                n += r;
            }
            // payload[0-1]=command(0x013A), [2-15]=ccuId, [16-21]=passKey
            var ccuId = System.Text.Encoding.UTF8.GetString(payload, 2, 14);
            var passKey = System.Text.Encoding.UTF8.GetString(payload, 16, 6);
            return new YmcAuthentication(ccuId, passKey);
        }
        catch (Exception e)
        {
            OnLog($"SPP: read error: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Writes a Virtual-Pit-Board-style framed payload (<c>[0x01][len4B][payload][0x04]</c>) to the
    /// still-open SPP socket from <see cref="OpenSppServerAsync"/>. Returns false if there's no live SPP connection.
    /// </summary>
    public async Task<bool> WriteSppAsync(byte[] framedPacket)
    {
        await _sppWriteMutex.WaitAsync().ConfigureAwait(false);
        try
        {
            var socket = _sppSocket;
            if (socket == null) return false;
            try
            {
                await Task.Run(() => socket.OutputStream!.Write(framedPacket, 0, framedPacket.Length)).ConfigureAwait(false);
                return true;
            }
            catch (Exception e)
            {
                OnLog($"SPP write failed: {e.Message}");
                return false;
            }
        }
        finally
        {
            _sppWriteMutex.Release();
        }
    }

    /// <summary>
    /// Reads (and discards) any further framed messages the CCU sends on the OTA/SPP socket.
    /// </summary>
    private void DrainSppSocket(BluetoothSocket socket, CancellationToken cancellationToken)
    {
        try
        {
            var stream = socket.InputStream!;
            while (!cancellationToken.IsCancellationRequested)
            {
                var b = stream.ReadByte();
                if (b == -1) break;
                if (b != 0x01) continue;
                var lenBuf = new byte[4];
                var n = 0;
                while (n < 4)
                {
                    var r = stream.Read(lenBuf, n, 4 - n);
                    if (r <= 0) return;
                    n += r;
                }
                var len = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | lenBuf[3];
                if (len < 0 || len > 1_000_000)
                {
                    OnLog($"SPP: drain saw implausible length {len}, stopping drain");
                    return;
                }
                var skipped = 0;
                var buf = new byte[Math.Min(len, 4096)];
                while (skipped < len)
                {
                    var r = stream.Read(buf, 0, Math.Min(buf.Length, len - skipped));
                    if (r <= 0) return;
                    skipped += r;
                }
                OnLog($"SPP: drained unsolicited {len}-byte frame");
            }
        }
        catch (Exception)
        {
            // Socket closed or errored; nothing to do here, Disconnect() owns cleanup.
        }
    }

    // -- Scanning --------------------------------------------------------------

    public async Task<IYmcBleDevice?> ScanForDeviceAsync(string targetName, CancellationToken cancellationToken)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            AndroidPermissions.AssertGranted(_context, Manifest.Permission.BluetoothScan);
            AndroidPermissions.AssertGranted(_context, Manifest.Permission.BluetoothConnect);
        }
        AndroidPermissions.AssertGranted(_context, Manifest.Permission.AccessFineLocation);

        var scanner = _adapter?.BluetoothLeScanner;
        if (scanner == null) return null;

        var found = new TaskCompletionSource<BluetoothDevice?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var filter = new ScanFilter.Builder().SetDeviceName(targetName)!.Build()!;
        var settings = new ScanSettings.Builder().SetScanMode(Android.Bluetooth.LE.ScanMode.LowLatency)!.Build()!;
        OnLog($"scanForDevice: starting scan filterName={targetName}");

        var callback = new ScanCallbackImpl(this, scanner, found);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(60_000));
        using var registration = timeout.Token.Register(() => found.TrySetCanceled());
        try
        {
            scanner.StartScan(new List<ScanFilter> { filter }, settings, callback);
            var device = await found.Task.ConfigureAwait(false);
            return device == null ? null : new AndroidBleDevice(device);
        }
        catch (System.OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            OnLog("scanForDevice: timeout");
            return null;
        }
        finally
        {
            try
            {
                scanner.StopScan(callback);
            }
            catch (Exception)
            {
            }
        }
    }

    private sealed class ScanCallbackImpl : ScanCallback
    {
        private readonly AndroidBluetoothConnectionManager _owner;
        private readonly BluetoothLeScanner _scanner;
        private readonly TaskCompletionSource<BluetoothDevice?> _found;

        public ScanCallbackImpl(
            AndroidBluetoothConnectionManager owner,
            BluetoothLeScanner scanner,
            TaskCompletionSource<BluetoothDevice?> found)
        {
            _owner = owner;
            _scanner = scanner;
            _found = found;
        }

        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result)
        {
            if (result?.Device == null || _found.Task.IsCompleted) return;
            _owner.OnLog(
                $"scan result: name={result.Device.Name} address={result.Device.Address} rssi={result.Rssi} connectable={result.IsConnectable}");
            if (!result.IsConnectable) return;
            _scanner.StopScan(this);
            _found.TrySetResult(result.Device);
        }

        public override void OnScanFailed(ScanFailure errorCode)
        {
            if (_found.Task.IsCompleted) return;
            _owner.OnLog($"scan failed: errorCode={errorCode}");
            _found.TrySetResult(null);
        }
    }

    private sealed class AndroidBleDevice : IYmcBleDevice
    {
        public AndroidBleDevice(BluetoothDevice device)
        {
            Device = device;
        }

        public BluetoothDevice Device { get; }

        public string? Name => Device.Name;

        public string Address => Device.Address ?? "";

        public int BondState => (int)Device.BondState;
    }

    // -- GATT connect ----------------------------------------------------------

    public async Task ConnectAsync(IYmcBleDevice device)
    {
        var bluetoothDevice = ((AndroidBleDevice)device).Device;
        InvalidOperationException? lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var g = await ConnectOnceAsync(bluetoothDevice).ConfigureAwait(false);
            if (g != null)
            {
                _activeGatt = g;
                return;
            }
            var status = _lastConnectionStatus?.ToString() ?? "unknown";
            var st = _lastConnectionState?.ToString() ?? "unknown";
            lastError = new InvalidOperationException($"GATT connection failed (status={status} state={st})");
            if (attempt < 1)
            {
                OnLog($"Reconnect attempt {attempt + 1} failed, retrying");
                await Task.Delay(TimeSpan.FromMilliseconds(750)).ConfigureAwait(false);
            }
        }
        throw lastError ?? new InvalidOperationException("GATT connection failed");
    }

    private async Task<BluetoothGatt?> ConnectOnceAsync(BluetoothDevice device)
    {
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingConnect = connected;
        _lastConnectionStatus = null;
        _lastConnectionState = null;
        OnLog($"connectOnce: {device.Name} / {device.Address} bondState={device.BondState}");

        var g = device.ConnectGatt(_context, false, _gattCallback, BluetoothTransports.Le);
        if (g == null)
        {
            OnLog("connectOnce: connectGatt returned null");
            return null;
        }

        // Guard must be set before we await so that OnConnectionStateChange for THIS gatt
        // is accepted, while stale callbacks from a previous (closed) gatt are rejected.
        _connectingGatt = g;

        try
        {
            var (completed, success) = await connected.Task.WithTimeoutAsync(TimeSpan.FromMilliseconds(10_000)).ConfigureAwait(false);
            if (completed && success)
            {
                OnLog("connectOnce: connected");
                return g;
            }
            OnLog($"connectOnce: failed status={_lastConnectionStatus} state={_lastConnectionState}");
            try
            {
                g.Close();
            }
            catch (Exception)
            {
            }
            return null;
        }
        finally
        {
            // Clear the guard before signalling completion so late callbacks from g are
            // already rejected before the caller can start the next attempt.
            _connectingGatt = null;
            connected.TrySetResult(false);
        }
    }

    // -- GATT setup ------------------------------------------------------------

    public async Task RequestMtuAsync(int mtu)
    {
        var gatt = _activeGatt ?? throw new InvalidOperationException("Not connected");
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingMtu = pending;
        OnLog($"requestMtu: requesting {mtu}");
        if (!gatt.RequestMtu(mtu)) throw new InvalidOperationException("requestMtu() returned false");
        var (completed, negotiated) = await pending.Task.WithTimeoutAsync(TimeSpan.FromMilliseconds(10_000)).ConfigureAwait(false);
        if (completed) NegotiatedMtu = negotiated;
        OnLog($"requestMtu: negotiated={NegotiatedMtu}");
    }

    public async Task DiscoverServicesAsync()
    {
        var gatt = _activeGatt ?? throw new InvalidOperationException("Not connected");
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingServices = pending;
        OnLog("discoverServices: requesting");
        if (!gatt.DiscoverServices()) throw new InvalidOperationException("discoverServices() returned false");
        var (completed, _) = await pending.Task.WithTimeoutAsync(TimeSpan.FromMilliseconds(10_000)).ConfigureAwait(false);
        if (!completed) throw new InvalidOperationException("Service discovery timed out");
        OnLog("discoverServices: done");
    }

    public bool HasService(Guid serviceUuid) => _activeGatt?.GetService(ToJava(serviceUuid)) != null;

    public bool HasCharacteristic(Guid serviceUuid, Guid characteristicUuid) =>
        _activeGatt?.GetService(ToJava(serviceUuid))?.GetCharacteristic(ToJava(characteristicUuid)) != null;

    public async Task SubscribeToNotificationsAsync(Guid serviceUuid)
    {
        var gatt = _activeGatt ?? throw new InvalidOperationException("Not connected");
        var service = gatt.GetService(ToJava(serviceUuid)) ?? throw new InvalidOperationException("Service not found");
        var characteristics = YmcBikeProtocol.NotifyUuids
            .Select(uuid => service.GetCharacteristic(ToJava(uuid)))
            .Where(c => c != null)
            .Select(c => c!)
            .Where(c => (c.Properties & (GattProperty.Notify | GattProperty.Indicate)) != 0)
            .ToList();
        OnLog($"subscribeToNotifications: {characteristics.Count} characteristics");
        foreach (var characteristic in characteristics)
        {
            await EnableNotificationsAsync(gatt, characteristic).ConfigureAwait(false);
            OnLog($"Subscribed {characteristic.Uuid}");
        }
    }

    private async Task EnableNotificationsAsync(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
    {
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingDescriptorWrite = pending;
        if (!gatt.SetCharacteristicNotification(characteristic, true))
        {
            OnLog($"enableNotifications: setCharacteristicNotification failed for {characteristic.Uuid}");
            return;
        }
        var descriptor = characteristic.GetDescriptor(CccdUuid);
        if (descriptor == null) return;
        var value = (characteristic.Properties & GattProperty.Indicate) != 0
            ? BluetoothGattDescriptor.EnableIndicationValue!.ToArray()
            : BluetoothGattDescriptor.EnableNotificationValue!.ToArray();

        bool started;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            started = gatt.WriteDescriptor(descriptor, value) == (int)CurrentBluetoothStatusCodes.Success;
        }
        else
        {
            descriptor.SetValue(value);
            started = gatt.WriteDescriptor(descriptor);
        }
        if (!started) throw new InvalidOperationException($"Descriptor write failed for {characteristic.Uuid}");

        var (completed, _) = await pending.Task.WithTimeoutAsync(TimeSpan.FromMilliseconds(5_000)).ConfigureAwait(false);
        if (!completed) throw new InvalidOperationException($"Descriptor write timed out for {characteristic.Uuid}");
    }

    // -- Write / Read ----------------------------------------------------------

    public async Task WriteAsync(Guid serviceUuid, Guid characteristicUuid, byte[] value)
    {
        AssertConnectPermission();
        var gatt = _activeGatt ?? throw new InvalidOperationException("Not connected");
        var characteristic = FindCharacteristic(gatt, serviceUuid, characteristicUuid);

        await _writeMutex.WaitAsync().ConfigureAwait(false);
        try
        {
            var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingWrite = pending;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            {
                var status = gatt.WriteCharacteristic(characteristic, value, (int)GattWriteType.Default);
                if (status != (int)CurrentBluetoothStatusCodes.Success)
                {
                    throw new InvalidOperationException($"writeCharacteristic failed for {characteristic.Uuid}: status={status}");
                }
            }
            else
            {
                characteristic.SetValue(value);
                if (!gatt.WriteCharacteristic(characteristic))
                    throw new InvalidOperationException($"writeCharacteristic failed for {characteristic.Uuid}");
            }
            var (completed, _) = await pending.Task.WithTimeoutAsync(TimeSpan.FromMilliseconds(5_000)).ConfigureAwait(false);
            if (!completed) throw new InvalidOperationException($"writeCharacteristic timed out for {characteristic.Uuid}");
        }
        finally
        {
            _writeMutex.Release();
        }
    }

    public async Task<byte[]?> ReadAsync(Guid serviceUuid, Guid characteristicUuid)
    {
        AssertConnectPermission();
        var gatt = _activeGatt ?? throw new InvalidOperationException("Not connected");
        var characteristic = FindCharacteristic(gatt, serviceUuid, characteristicUuid);

        await _writeMutex.WaitAsync().ConfigureAwait(false);
        try
        {
            var pending = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRead = pending;
            if (!gatt.ReadCharacteristic(characteristic))
                throw new InvalidOperationException($"readCharacteristic failed for {characteristic.Uuid}");
            var (completed, value) = await pending.Task.WithTimeoutAsync(TimeSpan.FromMilliseconds(5_000)).ConfigureAwait(false);
            return completed ? value : null;
        }
        finally
        {
            _writeMutex.Release();
        }
    }

    private static BluetoothGattCharacteristic FindCharacteristic(BluetoothGatt gatt, Guid serviceUuid, Guid characteristicUuid) =>
        gatt.GetService(ToJava(serviceUuid))?.GetCharacteristic(ToJava(characteristicUuid))
            ?? throw new InvalidOperationException($"Characteristic {characteristicUuid} not found");

    private static UUID ToJava(Guid guid) => UUID.FromString(guid.ToString())!;

    // -- Disconnect ------------------------------------------------------------

    public void Disconnect()
    {
        _sppCancellation?.Cancel();
        _sppCancellation = null;
        try
        {
            _sppServerSocket?.Close();
        }
        catch (Exception)
        {
        }
        _sppServerSocket = null;
        try
        {
            _sppSocket?.Close();
        }
        catch (Exception)
        {
        }
        _sppSocket = null;
        _pendingConnect?.TrySetCanceled();
        _pendingServices?.TrySetCanceled();
        _pendingMtu?.TrySetCanceled();
        _pendingWrite?.TrySetCanceled();
        _pendingRead?.TrySetCanceled();
        _pendingDescriptorWrite?.TrySetCanceled();
        _connectingGatt = null;

        // NOTE: the Kotlin source guarded this block with "permission NOT granted", which looks inverted
        // (it would only ever try to disconnect when it is not allowed to). Ported as "granted".
        if (Build.VERSION.SdkInt < BuildVersionCodes.S ||
            AndroidPermissions.IsGranted(_context, Manifest.Permission.BluetoothConnect))
        {
            try
            {
                _activeGatt?.Disconnect();
            }
            catch (Exception)
            {
                // Don't care.
            }
            try
            {
                _activeGatt?.Close();
            }
            catch (Exception)
            {
                // Don't care.
            }
        }
        _activeGatt = null;
        NegotiatedMtu = 23;
    }

    private void DispatchNotification(UUID? uuid, byte[]? value)
    {
        if (uuid == null || value == null) return;
        var notifyLine = $"NOTIFY {uuid} {value.ToHexString()}";
        var notifyUuid = Guid.Parse(uuid.ToString()!);
        if (UiLogFilter.IsSuppressedNotification(notifyUuid, value)) QuietLogMessage?.Invoke(notifyLine);
        else OnLog(notifyLine);
        Notifications.Publish(new GattNotificationEvent(notifyUuid, value));
    }

    // -- GATT callback ---------------------------------------------------------

    private sealed class GattCallback : BluetoothGattCallback
    {
        private readonly AndroidBluetoothConnectionManager _o;

        public GattCallback(AndroidBluetoothConnectionManager owner)
        {
            _o = owner;
        }

        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
        {
            if (gatt == null) return;
            if (!ReferenceEquals(gatt, _o._connectingGatt) && !ReferenceEquals(gatt, _o._activeGatt) &&
                gatt.Handle != _o._connectingGatt?.Handle && gatt.Handle != _o._activeGatt?.Handle)
            {
                _o.OnLog($"onConnectionStateChange: stale gatt rejected status={status} newState={newState}");
                try
                {
                    gatt.Close();
                }
                catch (Exception)
                {
                }
                return;
            }
            _o._lastConnectionStatus = (int)status;
            _o._lastConnectionState = (int)newState;
            _o.OnLog($"onConnectionStateChange status={(int)status} newState={(int)newState}");
            if (newState == ProfileState.Connected)
            {
                _o._pendingConnect?.TrySetResult(true);
            }
            else if (newState == ProfileState.Disconnected)
            {
                _o._pendingConnect?.TrySetResult(false);
                if (_o._activeGatt != null) _o.Disconnected?.Invoke();
            }
            else if (status != GattStatus.Success)
            {
                _o._pendingConnect?.TrySetResult(false);
            }
        }

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
        {
            _o.OnLog($"onServicesDiscovered status={(int)status} count={gatt?.Services?.Count ?? 0}");
            _o._pendingServices?.TrySetResult(status == GattStatus.Success);
        }

        public override void OnMtuChanged(BluetoothGatt? gatt, int mtu, GattStatus status)
        {
            _o.OnLog($"onMtuChanged status={(int)status} mtu={mtu}");
            if (status == GattStatus.Success)
            {
                _o.NegotiatedMtu = mtu;
                _o._pendingMtu?.TrySetResult(mtu);
            }
            else
            {
                _o._pendingMtu?.TrySetResult(_o.NegotiatedMtu);
            }
        }

        public override void OnCharacteristicChanged(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic)
        {
            _o.DispatchNotification(characteristic?.Uuid, characteristic?.GetValue());
        }

        public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value)
        {
            _o.DispatchNotification(characteristic.Uuid, value);
        }

        public override void OnCharacteristicRead(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, GattStatus status)
        {
            _o.OnLog($"onCharacteristicRead uuid={characteristic?.Uuid} status={(int)status}");
            _o._pendingRead?.TrySetResult(status == GattStatus.Success ? characteristic?.GetValue() : null);
        }

        public override void OnCharacteristicRead(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value, GattStatus status)
        {
            _o.OnLog($"onCharacteristicRead uuid={characteristic.Uuid} status={(int)status} len={value.Length}");
            _o._pendingRead?.TrySetResult(status == GattStatus.Success ? value : null);
        }

        public override void OnCharacteristicWrite(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, GattStatus status)
        {
            var writeLine = $"onCharacteristicWrite uuid={characteristic?.Uuid} status={(int)status} len={characteristic?.GetValue()?.Length ?? 0}";
            if (UiLogFilter.WritesSuppressed) _o.QuietLogMessage?.Invoke(writeLine);
            else _o.OnLog(writeLine);
            _o._pendingWrite?.TrySetResult((int)status);
        }

        public override void OnDescriptorWrite(BluetoothGatt? gatt, BluetoothGattDescriptor? descriptor, GattStatus status)
        {
            _o.OnLog($"onDescriptorWrite uuid={descriptor?.Uuid} status={(int)status} char={descriptor?.Characteristic?.Uuid}");
            _o._pendingDescriptorWrite?.TrySetResult((int)status);
        }
    }
}
