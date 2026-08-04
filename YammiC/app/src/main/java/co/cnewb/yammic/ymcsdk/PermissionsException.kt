package co.cnewb.yammic.ymcsdk

import android.content.Context
import android.content.pm.PackageManager
import androidx.core.app.ActivityCompat

class PermissionsException(
    val permission: String,
) : SecurityException("Permission not granted: $permission") {
    companion object {
        fun assertGranted(context: Context, permission: String) {
            if (ActivityCompat.checkSelfPermission(context, permission) != PackageManager.PERMISSION_GRANTED) {
                throw PermissionsException(permission)
            }
        }
    }
}