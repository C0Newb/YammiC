package co.cnewb.yammic.settings

import android.content.Context

/**
 * UI-only preference store for the Settings page's stub toggles. Nothing else in the app
 * reads these yet — Music metadata, Phone metadata, and Auto-reconnect have no backing
 * monitor/service logic wired to them (see the design spec's Non-goals). Wiring real
 * behavior to these values is a separate follow-up project.
 */
class SettingsPreferences(context: Context) {
    private val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    var musicMetadataEnabled: Boolean
        get() = prefs.getBoolean(KEY_MUSIC_METADATA, true)
        set(value) = prefs.edit().putBoolean(KEY_MUSIC_METADATA, value).apply()

    var phoneMetadataEnabled: Boolean
        get() = prefs.getBoolean(KEY_PHONE_METADATA, true)
        set(value) = prefs.edit().putBoolean(KEY_PHONE_METADATA, value).apply()

    var autoReconnectEnabled: Boolean
        get() = prefs.getBoolean(KEY_AUTO_RECONNECT, false)
        set(value) = prefs.edit().putBoolean(KEY_AUTO_RECONNECT, value).apply()

    private companion object {
        const val PREFS_NAME = "yammic_settings"
        const val KEY_MUSIC_METADATA = "music_metadata_enabled"
        const val KEY_PHONE_METADATA = "phone_metadata_enabled"
        const val KEY_AUTO_RECONNECT = "auto_reconnect_enabled"
    }
}
