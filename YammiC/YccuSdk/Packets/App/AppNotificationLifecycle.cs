namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Manages the per-notification lifecycle state on the CCU (add, update, remove).
///
/// Wire format (Simple transport, 7 bytes):
/// <code>
/// bytes[0..1] = command code (big-endian)
/// bytes[2]    = requestId
/// bytes[3..6] = notificationId (4-byte big-endian)
/// </code>
///
/// - <see cref="Operation.Add"/> (0x0010): registers a new notification ID; send before <see cref="AppNotificationDataV2"/>
/// - <see cref="Operation.Update"/> (0x0011): signals content changed; send before an updated <see cref="AppNotificationDataV2"/>
/// - <see cref="Operation.Remove"/> (0x0012): clears the notification from the CCU display
/// </summary>
public class AppNotificationLifecycle : AppOutgoingRequest
{
    public AppNotificationLifecycle(IYmcPacketHandler packetHandler, Operation operation, int notificationId)
        : base(packetHandler, CommandCodeFor(operation))
    {
        PacketOperation = operation;
        NotificationId = notificationId;
    }

    public enum Operation
    {
        Add,
        Update,
        Remove,
    }

    public Operation PacketOperation { get; }
    public int NotificationId { get; }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) => Concat(Preamble, new[] { requestId }, IntBytes(NotificationId));

    private static YmcCommandCode CommandCodeFor(Operation operation) => operation switch
    {
        Operation.Add => YmcCommandCode.AddNotification,
        Operation.Update => YmcCommandCode.UpdateNotification,
        _ => YmcCommandCode.RemoveNotification,
    };
}
