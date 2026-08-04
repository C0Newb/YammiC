package co.cnewb.yammic.ymcsdk

import android.bluetooth.BluetoothGatt
import android.bluetooth.BluetoothGattCharacteristic
import android.bluetooth.BluetoothGattService
import android.content.Context
import co.cnewb.yammic.ymcsdk.packets.GattNotificationEvent
import kotlinx.coroutines.flow.SharedFlow

class YmcConnectionContext(
    bluetoothGatt: BluetoothGatt,
    bluetoothGattService: BluetoothGattService,
    val context: Context,
    private val btManager: YmcBluetoothConnectionManager,
) {
    val gatt: BluetoothGatt = bluetoothGatt
    val service: BluetoothGattService = bluetoothGattService
    val negotiatedMtu: Int get() = btManager.negotiatedMtu
    val notifications: SharedFlow<GattNotificationEvent> get() = btManager.notifications

    suspend fun write(char: BluetoothGattCharacteristic, value: ByteArray) {
        btManager.write(gatt, char, value)
    }

    suspend fun read(char: BluetoothGattCharacteristic): ByteArray? = btManager.read(gatt, char)

    /** Writes a framed payload to the Classic-Bluetooth SPP socket (Virtual Pit Board graphic chunks). */
    suspend fun writeSpp(framedPacket: ByteArray): Boolean = btManager.writeSpp(framedPacket)
}
