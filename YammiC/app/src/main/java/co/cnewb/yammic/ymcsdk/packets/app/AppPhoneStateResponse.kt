package co.cnewb.yammic.ymcsdk.packets.app

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.media.AudioManager
import android.os.BatteryManager
import android.provider.Settings
import android.telephony.TelephonyManager
import co.cnewb.yammic.ymcsdk.models.YmcCellSignalLevel
import co.cnewb.yammic.ymcsdk.models.YmcPhoneThermalState
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * PhoneStateCombined (cmd 0x013C) — proactively reports phone state to the CCU.
 *
 * All values are read live from the device on every [serialize] call.
 *
 * headsetConnected=true gates BluetoothMusicMetaData on the CCU side.
 *
 * Wire: `[cmd 2B][byte0..3]` — 6 bytes, simple write, no requestId.
 *   byte0: (battery% & 0x7F) | (charging << 7)
 *   byte1: (phoneVolume% & 0x7F) | (1 << 7)        — enablePhoneVolume always true
 *   byte2: (mediaVolume% & 0x7F) | (1 << 7)         — enableMediaVolume always true
 *   byte3: (signalLevel << 4) | (thermalState << 2) | (headsetConnected << 1) | enableOnHook
 */
class AppPhoneStateResponse(
    packetHandler: YmcPacketHandler,
    private val context: Context,
    private val headsetConnected: Boolean = true,
) : AppOutgoingResponse(packetHandler, YmcCommandCode.PhoneStateCombined) {
    override val transport = Transport.Simple

    override fun serialize(requestId: Byte): ByteArray {
        val audioManager = context.getSystemService(AudioManager::class.java)
        val telephonyManager = context.getSystemService(TelephonyManager::class.java)

        val batteryIntent = context.registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
        val level = batteryIntent?.getIntExtra(BatteryManager.EXTRA_LEVEL, -1) ?: -1
        val scale = batteryIntent?.getIntExtra(BatteryManager.EXTRA_SCALE, -1) ?: -1
        val batteryPercent = if (level >= 0 && scale > 0) (level * 100) / scale else 100
        val charging = (batteryIntent?.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0) ?: 0) != 0
        val batteryTempCelsius = (batteryIntent?.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, 0) ?: 0) / 10.0f

        val phoneMax = audioManager?.getStreamMaxVolume(AudioManager.STREAM_VOICE_CALL) ?: 1
        val phoneCurrent = audioManager?.getStreamVolume(AudioManager.STREAM_VOICE_CALL) ?: 0
        val phoneVolumePercent = if (phoneMax > 0) (phoneCurrent * 100) / phoneMax else 0

        val mediaMax = audioManager?.getStreamMaxVolume(AudioManager.STREAM_MUSIC) ?: 1
        val mediaCurrent = audioManager?.getStreamVolume(AudioManager.STREAM_MUSIC) ?: 0
        val mediaVolumePercent = if (mediaMax > 0) (mediaCurrent * 100) / mediaMax else 0

        val signalLevel = readSignalLevel(telephonyManager)
        val thermalState = thermalStateFromBatteryTemp(batteryTempCelsius)

        val byte0 = ((batteryPercent.coerceIn(0, 100) and 0x7F) or ((if (charging) 1 else 0) shl 7)).toByte()
        val byte1 = ((phoneVolumePercent.coerceIn(0, 100) and 0x7F) or 0x80).toByte()
        val byte2 = ((mediaVolumePercent.coerceIn(0, 100) and 0x7F) or 0x80).toByte()
        val byte3 =
            (
                (signalLevel.wireValue.toInt() shl 4) or
                    (thermalState.wireValue.toInt() shl 2) or
                    ((if (headsetConnected) 1 else 0) shl 1) or
                    1 // enableOnHook always true
            ).toByte()

        return preamble + byteArrayOf(byte0, byte1, byte2, byte3)
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

    fun thermalStateFromBatteryTemp(tempCelsius: Float): YmcPhoneThermalState =
        when {
            tempCelsius < 40.0f -> YmcPhoneThermalState.Low
            tempCelsius < 45.0f -> YmcPhoneThermalState.Middle
            else -> YmcPhoneThermalState.High
        }
}
