using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Packets;

namespace CNCO.YccuSdk.Platform;

/// <summary>A BLE device found by <see cref="IYmcBluetoothConnectionManager.ScanForDeviceAsync"/>.</summary>
public interface IYmcBleDevice
{
    string? Name { get; }
    string Address { get; }
    int BondState { get; }
}

/// <summary>
/// Everything the SDK needs from the host's Bluetooth stack: BLE GATT (scan, connect, MTU, discovery,
/// notifications, read/write) plus the Classic-Bluetooth SPP socket used for the credential handoff and
/// Virtual Pit Board graphics. One instance manages a single CCU connection at a time.
/// </summary>
public interface IYmcBluetoothConnectionManager
{
    /// <summary>Notifications received from the CCU's GATT characteristics.</summary>
    YmcNotificationBus Notifications { get; }

    int NegotiatedMtu { get; }

    bool IsBluetoothEnabled { get; }

    int BondedDeviceCount { get; }

    /// <summary>Raised when the active GATT connection drops.</summary>
    event Action? Disconnected;

    /// <summary>Raised for diagnostic lines produced by the Bluetooth stack wrapper.</summary>
    event Action<string>? LogMessage;

    /// <summary>Raised for diagnostic lines that belong in the log file but not in the UI (see <c>SuppressUiLogAttribute</c>).</summary>
    event Action<string>? QuietLogMessage;

    /// <summary>Throws <see cref="PermissionsException"/> if the host has not granted connect permission.</summary>
    void AssertConnectPermission();

    /// <summary>
    /// Opens the SPP server the CCU connects to on its own. Completes with the ccuId/passKey the CCU hands over,
    /// or null if the CCU never connected / the handoff failed. The socket stays open for later
    /// <see cref="WriteSppAsync"/> calls.
    /// </summary>
    Task<YmcAuthentication?> OpenSppServerAsync();

    /// <summary>Scans for a connectable BLE device advertising [targetName]. Null on timeout or failure.</summary>
    Task<IYmcBleDevice?> ScanForDeviceAsync(string targetName, CancellationToken cancellationToken);

    Task ConnectAsync(IYmcBleDevice device);

    Task RequestMtuAsync(int mtu);

    Task DiscoverServicesAsync();

    bool HasService(Guid serviceUuid);

    bool HasCharacteristic(Guid serviceUuid, Guid characteristicUuid);

    /// <summary>Enables notifications/indications on every known RX characteristic (serialised).</summary>
    Task SubscribeToNotificationsAsync(Guid serviceUuid);

    Task WriteAsync(Guid serviceUuid, Guid characteristicUuid, byte[] value);

    Task<byte[]?> ReadAsync(Guid serviceUuid, Guid characteristicUuid);

    /// <summary>Writes a framed payload to the SPP socket. False if there is no live SPP connection.</summary>
    Task<bool> WriteSppAsync(byte[] framedPacket);

    void Disconnect();
}
