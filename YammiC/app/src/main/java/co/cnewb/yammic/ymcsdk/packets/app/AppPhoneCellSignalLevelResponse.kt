package co.cnewb.yammic.ymcsdk.packets.app

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.provider.Settings
import android.telephony.TelephonyManager
import co.cnewb.yammic.ymcsdk.models.YmcCellSignalLevel
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

class AppPhoneCellSignalLevelResponse(
    packetHandler: YmcPacketHandler,
    private val context: Context,
) : AppOutgoingResponse(packetHandler, YmcCommandCode.PhoneCellSignalLevelResponse) {
    override val transport = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray {
        val telephonyManager = context.getSystemService(TelephonyManager::class.java)
        val signalLevel = readSignalLevel(telephonyManager)
        return preamble + byteArrayOf(signalLevel.wireValue)
    }

    private fun readSignalLevel(telephonyManager: TelephonyManager?): YmcCellSignalLevel {
        telephonyManager ?: return YmcCellSignalLevel.NoConnection
        if (context.checkSelfPermission(Manifest.permission.READ_PHONE_STATE) !=
            PackageManager.PERMISSION_GRANTED
        ) {
            return YmcCellSignalLevel.NoConnection
        }
        val airplaneMode = Settings.System.getInt(context.contentResolver, "airplane_mode_on", 0) != 0
        if (airplaneMode) return YmcCellSignalLevel.NoConnection
        if (telephonyManager.simState != TelephonyManager.SIM_STATE_READY) return YmcCellSignalLevel.NoConnection
        return when (telephonyManager.signalStrength?.level ?: 0) {
            0 -> YmcCellSignalLevel.NoSignal
            1 -> YmcCellSignalLevel.Lv1
            2 -> YmcCellSignalLevel.Lv2
            3 -> YmcCellSignalLevel.Lv3
            4 -> YmcCellSignalLevel.Lv4
            else -> YmcCellSignalLevel.NoConnection
        }
    }
}
