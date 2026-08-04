package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Manages the per-notification lifecycle state on the CCU (add, update, remove).
 *
 * Wire format (Simple transport, 7 bytes):
 * ```
 * bytes[0..1] = command code (big-endian)
 * bytes[2]    = requestId
 * bytes[3..6] = notificationId (4-byte big-endian)
 * ```
 *
 * - [Operation.Add] (0x0010): registers a new notification ID; send before [AppNotificationDataV2]
 * - [Operation.Update] (0x0011): signals content changed; send before an updated [AppNotificationDataV2]
 * - [Operation.Remove] (0x0012): clears the notification from the CCU display
 */
class AppNotificationLifecycle(
    packetHandler: YmcPacketHandler,
    val operation: Operation,
    val notificationId: Int,
) : AppOutgoingRequest(packetHandler, operation.commandCode) {

    enum class Operation(val commandCode: YmcCommandCode) {
        Add(YmcCommandCode.AddNotification),
        Update(YmcCommandCode.UpdateNotification),
        Remove(YmcCommandCode.RemoveNotification),
    }

    override val transport = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray =
        preamble +
            byteArrayOf(requestId) +
            intBytes(notificationId)
}
