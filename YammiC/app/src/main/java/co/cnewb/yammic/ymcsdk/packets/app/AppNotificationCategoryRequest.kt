package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Declares to the CCU which notification categories this app will forward.
 * Sent once at connection time. Wire: [cmd 2B][12 bool bytes].
 * Byte order: other, incomingCall, missedCall, voicemail, social, schedule,
 *             email, news, healthAndFitness, businessAndFinance, location, entertainment.
 */
class AppNotificationCategoryRequest(
    packetHandler: YmcPacketHandler,
    val other: Boolean = false,
    val incomingCall: Boolean = false,
    val missedCall: Boolean = false,
    val voicemail: Boolean = false,
    val social: Boolean = false,
    val schedule: Boolean = false,
    val email: Boolean = false,
    val news: Boolean = false,
    val healthAndFitness: Boolean = false,
    val businessAndFinance: Boolean = false,
    val location: Boolean = false,
    val entertainment: Boolean = false,
) : AppOutgoingRequest(packetHandler, YmcCommandCode.NotificationCategory) {
    override val transport = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray = preamble + byteArrayOf(
        other.asByte(),
        incomingCall.asByte(),
        missedCall.asByte(),
        voicemail.asByte(),
        social.asByte(),
        schedule.asByte(),
        email.asByte(),
        news.asByte(),
        healthAndFitness.asByte(),
        businessAndFinance.asByte(),
        location.asByte(),
        entertainment.asByte(),
    )

    private fun Boolean.asByte(): Byte = if (this) 1 else 0
}
