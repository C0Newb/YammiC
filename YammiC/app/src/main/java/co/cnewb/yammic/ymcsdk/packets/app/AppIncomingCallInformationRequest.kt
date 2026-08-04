package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Wire: `[cmd 2B][nameLen 1B][name][phoneLen 1B][phone][isMissedCall 1B]`.
 * for how that assumption was disproven against a real CCU capture.
 */
class AppIncomingCallInformationRequest(
    packetHandler: YmcPacketHandler,
    val callerName: String,
    val phoneNumber: String,
    val isMissedCall: Boolean,
) : AppOutgoingResponse(packetHandler, YmcCommandCode.IncomingCallInformation) {
    override val transport = Transport.Ddt

    override fun serialize(requestId: Byte): ByteArray {
        val nameBytes = callerName.take(64).toByteArray(Charsets.UTF_8)
        val phoneBytes = phoneNumber.take(12).toByteArray(Charsets.UTF_8)
        val payload =
            byteArrayOf(nameBytes.size.toByte()) + nameBytes +
                byteArrayOf(phoneBytes.size.toByte()) + phoneBytes +
                byteArrayOf(if (isMissedCall) 1 else 0)
        return preamble + payload
    }
}
