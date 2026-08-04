package co.cnewb.yammic.ymcsdk

import android.content.Context
import android.content.SharedPreferences
import androidx.core.content.edit
import co.cnewb.yammic.bike.BikeDebugState

internal class YmcBikeSessionStore(
    context: Context,
) {
    private val prefs: SharedPreferences = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    fun loadState(logFileLabel: String): BikeDebugState =
        BikeDebugState(
            deviceName =
                prefs.getString(KEY_DEVICE_NAME, DEFAULT_DEVICE_NAME)
                    ?: DEFAULT_DEVICE_NAME,
            ccuId = prefs.getString(KEY_CCU_ID, DEFAULT_CCU_ID) ?: DEFAULT_CCU_ID,
            passKey = prefs.getString(KEY_PASS_KEY, DEFAULT_PASS_KEY) ?: DEFAULT_PASS_KEY,
            phoneUuid = prefs.getString(KEY_PHONE_UUID, DEFAULT_PHONE_UUID) ?: DEFAULT_PHONE_UUID,
            logFileLabel = logFileLabel,
        )

    fun saveCredentials(
        ccuId: String,
        passKey: String,
    ) {
        prefs.edit {
            putString(KEY_CCU_ID, ccuId.trim())
                .putString(KEY_PASS_KEY, passKey.trim())
        }
    }

    fun saveDeviceName(deviceName: String) {
        prefs.edit { putString(KEY_DEVICE_NAME, deviceName.trim()) }
    }

    fun savePhoneUuid(phoneUuid: String) {
        prefs.edit { putString(KEY_PHONE_UUID, phoneUuid.trim()) }
    }

    companion object {
        private const val PREFS_NAME = "yammic.session"
        private const val KEY_DEVICE_NAME = "device_name"
        private const val KEY_CCU_ID = "ccu_id"
        private const val KEY_PASS_KEY = "pass_key"
        private const val KEY_PHONE_UUID = "phone_uuid"

        // FOR THE LOVE OF GOD DO NOT COMMIT THIS!!!
        private const val DEFAULT_DEVICE_NAME = "YCCU_01234567890123"
        private const val DEFAULT_CCU_ID = "01234567890123"
        private const val DEFAULT_PASS_KEY = "012345"
        private const val DEFAULT_PHONE_UUID = "0123456789abcdef0123456789abcdef"
    }
}
