package co.cnewb.yammic.ymcsdk.packets.app

import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.BatteryManager
import co.cnewb.yammic.ymcsdk.models.YmcPhoneThermalState
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

class AppPhoneThermalStateNotification(
    packetHandler: YmcPacketHandler,
    private val context: Context,
) : AppOutgoingResponse(packetHandler, YmcCommandCode.PhoneThermalState) {
    override val transport = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray {
        val batteryIntent = context.registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
        val tempCelsius = (batteryIntent?.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, 0) ?: 0) / 10.0f
        val thermalState = when {
            tempCelsius < 40.0f -> YmcPhoneThermalState.Low
            tempCelsius < 45.0f -> YmcPhoneThermalState.Middle
            else -> YmcPhoneThermalState.High
        }
        return preamble + byteArrayOf(thermalState.wireValue)
    }
}
