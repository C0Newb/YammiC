package co.cnewb.yammic.ymcsdk.handlers

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.telecom.TelecomManager
import androidx.core.app.ActivityCompat
import co.cnewb.yammic.ymcsdk.YmcConnectionContext
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuIncomingCallControlRequest

class CcuIncomingCallControlRequestHandler(
    private val context: Context,
) : CcuRequestHandler<CcuIncomingCallControlRequest>() {
    private val telecomManager = context.getSystemService(TelecomManager::class.java)

    override suspend fun invoke(
        packet: CcuIncomingCallControlRequest,
        packetHandler: YmcPacketHandler,
        connectionContext: YmcConnectionContext,
    ) {
        when (packet.action) {
            CcuIncomingCallControlRequest.Action.Answer -> acceptRingingCall()
            CcuIncomingCallControlRequest.Action.OnHook -> endCall()
            CcuIncomingCallControlRequest.Action.Reject -> endCall()
            CcuIncomingCallControlRequest.Action.Unknown -> Unit
        }
    }

    private fun hasAnswerPhoneCallsPermission(): Boolean =
        ActivityCompat.checkSelfPermission(
            context,
            Manifest.permission.ANSWER_PHONE_CALLS,
        ) == PackageManager.PERMISSION_GRANTED

    @Suppress("DEPRECATION")
    private fun acceptRingingCall() {
        if (!hasAnswerPhoneCallsPermission()) {
            // Can't!
            return
        }
        telecomManager?.acceptRingingCall()
    }

    @Suppress("DEPRECATION")
    private fun endCall() {
        if (!hasAnswerPhoneCallsPermission()) {
            // Can't!
            return
        }
        telecomManager?.endCall()
    }
}
