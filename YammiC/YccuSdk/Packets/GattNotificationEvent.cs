namespace CNCO.YccuSdk.Packets;

public class GattNotificationEvent
{
    public GattNotificationEvent(Guid uuid, byte[] value)
    {
        Uuid = uuid;
        Value = value;
    }

    public Guid Uuid { get; }
    public byte[] Value { get; }
}
