using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk;

public class YmcConnectionContext
{
    private readonly IYmcBluetoothConnectionManager _btManager;
    private readonly Guid _serviceUuid;

    public YmcConnectionContext(IYmcBluetoothConnectionManager btManager, Guid serviceUuid)
    {
        _btManager = btManager;
        _serviceUuid = serviceUuid;
    }

    public int NegotiatedMtu => _btManager.NegotiatedMtu;

    public YmcNotificationBus Notifications => _btManager.Notifications;

    public bool HasCharacteristic(Guid characteristicUuid) => _btManager.HasCharacteristic(_serviceUuid, characteristicUuid);

    public Task WriteAsync(Guid characteristicUuid, byte[] value) => _btManager.WriteAsync(_serviceUuid, characteristicUuid, value);

    public Task<byte[]?> ReadAsync(Guid characteristicUuid) => _btManager.ReadAsync(_serviceUuid, characteristicUuid);

    /// <summary>Writes a framed payload to the Classic-Bluetooth SPP socket (Virtual Pit Board graphic chunks).</summary>
    public Task<bool> WriteSppAsync(byte[] framedPacket) => _btManager.WriteSppAsync(framedPacket);
}
