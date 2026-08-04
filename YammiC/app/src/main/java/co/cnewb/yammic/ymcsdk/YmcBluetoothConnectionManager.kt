package co.cnewb.yammic.ymcsdk

import android.Manifest
import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothGatt
import android.bluetooth.BluetoothGattCallback
import android.bluetooth.BluetoothGattCharacteristic
import android.bluetooth.BluetoothGattDescriptor
import android.bluetooth.BluetoothGattService
import android.bluetooth.BluetoothProfile
import android.bluetooth.BluetoothServerSocket
import android.bluetooth.BluetoothSocket
import android.bluetooth.le.ScanCallback
import android.bluetooth.le.ScanFilter
import android.bluetooth.le.ScanResult
import android.bluetooth.le.ScanSettings
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import androidx.annotation.RequiresPermission
import androidx.core.app.ActivityCompat
import co.cnewb.yammic.ymcsdk.packets.GattNotificationEvent
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import java.util.UUID
import kotlin.coroutines.resume
import kotlin.time.Duration.Companion.milliseconds

class YmcBluetoothConnectionManager(
    private val context: Context,
    private val bluetoothAdapter: BluetoothAdapter?,
    private val scope: CoroutineScope,
    private val onLog: (String) -> Unit,
    private val onDisconnected: () -> Unit,
) {
    private val _notifications = MutableSharedFlow<GattNotificationEvent>(extraBufferCapacity = 64, replay = 0)
    val notifications: SharedFlow<GattNotificationEvent> = _notifications.asSharedFlow()

    var negotiatedMtu: Int = 23
        private set

    private var activeGatt: BluetoothGatt? = null

    @Volatile private var connectingGatt: BluetoothGatt? = null

    private var pendingConnect: CompletableDeferred<Boolean>? = null
    private var pendingServices: CompletableDeferred<Boolean>? = null
    private var pendingMtu: CompletableDeferred<Int>? = null
    private var pendingWrite: CompletableDeferred<Int>? = null
    private var pendingRead: CompletableDeferred<ByteArray?>? = null
    private var pendingDescriptorWrite: CompletableDeferred<Int>? = null
    private var lastConnectionStatus: Int? = null
    private var lastConnectionState: Int? = null

    private var sppJob: Job? = null
    private var sppServerSocket: BluetoothServerSocket? = null
    private var sppSocket: BluetoothSocket? = null
    private val sppWriteMutex = Mutex()

    private val writeMutex = Mutex()

    // ── SPP ───────────────────────────────────────────────────────────────────

    @RequiresPermission(Manifest.permission.BLUETOOTH_CONNECT)
    fun openSppServer(): CompletableDeferred<Pair<String, String>> {
        val creds = CompletableDeferred<Pair<String, String>>()
        try {
            val adapter =
                bluetoothAdapter ?: run {
                    onLog("SPP: adapter null")
                    creds.cancel()
                    return creds
                }
            val server = adapter.listenUsingRfcommWithServiceRecord("YammiC", SPP_SERVICE_UUID)
            sppServerSocket = server
            onLog("SPP: server open, waiting for YCCU Classic BT connection")
            sppJob =
                scope.launch(Dispatchers.IO) {
                    try {
                        val socket = server.accept(30_000)
                        onLog("SPP: YCCU connected via Classic BT from ${socket.remoteDevice.address}")
                        try {
                            server.close()
                        } catch (_: Exception) {
                        }
                        sppSocket = socket
                        val pair = readSppBleConnectInfo(socket)
                        if (pair != null) {
                            onLog("SPP: received ccuId=${pair.first} passKey=***")
                            creds.complete(pair)
                        } else {
                            creds.cancel()
                        }
                        // Keep the socket open after the handoff: Virtual Pit Board graphic chunks
                        // (and any other OTA-socket traffic) are written to it for the rest of the
                        // session. Drain/log anything the CCU sends back on this channel — the reference
                        // SDK's replies for graphic chunks actually arrive over BLE, not here, but we
                        // don't want an unread stream to back up and stall the socket.
                        drainSppSocket(socket)
                    } catch (e: Exception) {
                        onLog("SPP: ${e.message}")
                        creds.cancel()
                    }
                }
        } catch (e: Exception) {
            onLog("SPP: server failed: ${e.message}")
            creds.cancel()
        }
        return creds
    }

    private fun readSppBleConnectInfo(socket: BluetoothSocket): Pair<String, String>? {
        return try {
            val stream = socket.inputStream
            var b: Int
            do {
                b = stream.read()
                if (b == -1) return null
            } while (b != 0x01)
            val lenBuf = ByteArray(4)
            var n = 0
            while (n < 4) {
                val r = stream.read(lenBuf, n, 4 - n)
                if (r < 0) return null
                n += r
            }
            val len =
                ((lenBuf[0].toInt() and 0xFF) shl 24) or ((lenBuf[1].toInt() and 0xFF) shl 16) or
                    ((lenBuf[2].toInt() and 0xFF) shl 8) or (lenBuf[3].toInt() and 0xFF)
            if (len < 22) {
                onLog("SPP: payload too short ($len)")
                return null
            }
            val payload = ByteArray(len)
            n = 0
            while (n < len) {
                val r = stream.read(payload, n, len - n)
                if (r < 0) return null
                n += r
            }
            // payload[0-1]=command(0x013A), [2-15]=ccuId, [16-21]=passKey
            val ccuId = String(payload, 2, 14, Charsets.UTF_8)
            val passKey = String(payload, 16, 6, Charsets.UTF_8)
            Pair(ccuId, passKey)
        } catch (e: Exception) {
            onLog("SPP: read error: ${e.message}")
            null
        }
    }

    /**
     * Writes a Virtual-Pit-Board-style framed payload (`[0x01][len4B][payload][0x04]`) to the
     * still-open SPP socket from [openSppServer]. Returns false if there's no live SPP connection.
     */
    suspend fun writeSpp(framedPacket: ByteArray): Boolean =
        sppWriteMutex.withLock {
            val socket = sppSocket ?: return@withLock false
            try {
                withContext(Dispatchers.IO) { socket.outputStream.write(framedPacket) }
                true
            } catch (e: Exception) {
                onLog("SPP write failed: ${e.message}")
                false
            }
        }

    /** Reads (and discards) any further framed messages the CCU sends on the OTA/SPP socket. */
    private fun drainSppSocket(socket: BluetoothSocket) {
        scope.launch(Dispatchers.IO) {
            try {
                val stream = socket.inputStream
                while (true) {
                    val b = stream.read()
                    if (b == -1) break
                    if (b != 0x01) continue
                    val lenBuf = ByteArray(4)
                    var n = 0
                    while (n < 4) {
                        val r = stream.read(lenBuf, n, 4 - n)
                        if (r < 0) return@launch
                        n += r
                    }
                    val len =
                        ((lenBuf[0].toInt() and 0xFF) shl 24) or ((lenBuf[1].toInt() and 0xFF) shl 16) or
                            ((lenBuf[2].toInt() and 0xFF) shl 8) or (lenBuf[3].toInt() and 0xFF)
                    if (len < 0 || len > 1_000_000) {
                        onLog("SPP: drain saw implausible length $len, stopping drain")
                        return@launch
                    }
                    var skipped = 0
                    val buf = ByteArray(minOf(len, 4096))
                    while (skipped < len) {
                        val r = stream.read(buf, 0, minOf(buf.size, len - skipped))
                        if (r < 0) return@launch
                        skipped += r
                    }
                    onLog("SPP: drained unsolicited $len-byte frame")
                }
            } catch (_: Exception) {
                // Socket closed or errored; nothing to do here, disconnect() owns cleanup.
            }
        }
    }

    // ── Scanning ──────────────────────────────────────────────────────────────

    @SuppressLint("MissingPermission")
    suspend fun scanForDevice(targetName: String): BluetoothDevice? {
        PermissionsException.assertGranted(context, Manifest.permission.BLUETOOTH_SCAN)
        PermissionsException.assertGranted(context, Manifest.permission.BLUETOOTH_CONNECT)
        PermissionsException.assertGranted(context, Manifest.permission.ACCESS_FINE_LOCATION)

        return withTimeoutOrNull(30_000.milliseconds) {
            suspendCancellableCoroutine { cont ->
                val adapter =
                    bluetoothAdapter ?: run {
                        cont.resume(null)
                        return@suspendCancellableCoroutine
                    }
                val scanner =
                    adapter.bluetoothLeScanner ?: run {
                        cont.resume(null)
                        return@suspendCancellableCoroutine
                    }
                val filter = ScanFilter.Builder().setDeviceName(targetName).build()
                val settings = ScanSettings.Builder().setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY).build()
                onLog("scanForDevice: starting scan filterName=$targetName")

                val callback =
                    object : ScanCallback() {
                        override fun onScanResult(
                            callbackType: Int,
                            result: ScanResult,
                        ) {
                            if (!cont.isCompleted) {
                                onLog(
                                    "scan result: name=${result.device.name} address=${result.device.address} rssi=${result.rssi} connectable=${result.isConnectable}",
                                )
                                if (!result.isConnectable) return
                                scanner.stopScan(this)
                                cont.resume(result.device)
                            }
                        }

                        override fun onScanFailed(errorCode: Int) {
                            if (!cont.isCompleted) {
                                onLog("scan failed: errorCode=$errorCode")
                                cont.resume(null)
                            }
                        }
                    }
                cont.invokeOnCancellation {
                    try {
                        scanner.stopScan(callback)
                    } catch (_: Exception) {
                    }
                }
                scanner.startScan(listOf(filter), settings, callback)
            }
        } ?: run {
            onLog("scanForDevice: timeout")
            null
        }
    }

    // ── GATT connect ──────────────────────────────────────────────────────────

    @RequiresPermission(Manifest.permission.BLUETOOTH_CONNECT)
    suspend fun connect(device: BluetoothDevice): BluetoothGatt {
        var lastError: IllegalStateException? = null
        repeat(2) { attempt ->
            val g = connectOnce(device)
            if (g != null) {
                activeGatt = g
                return g
            }
            val status = lastConnectionStatus?.toString() ?: "unknown"
            val st = lastConnectionState?.toString() ?: "unknown"
            lastError = IllegalStateException("GATT connection failed (status=$status state=$st)")
            if (attempt < 1) {
                onLog("Reconnect attempt ${attempt + 1} failed, retrying")
                delay(750.milliseconds)
            }
        }
        throw lastError ?: IllegalStateException("GATT connection failed")
    }

    @RequiresPermission(Manifest.permission.BLUETOOTH_CONNECT)
    private suspend fun connectOnce(device: BluetoothDevice): BluetoothGatt? {
        val connected = CompletableDeferred<Boolean>()
        pendingConnect = connected
        lastConnectionStatus = null
        lastConnectionState = null
        onLog("connectOnce: ${device.name} / ${device.address} bondState=${device.bondState}")

        val g =
            device.connectGatt(context, false, gattCallback, BluetoothDevice.TRANSPORT_LE)
                ?: run {
                    onLog("connectOnce: connectGatt returned null")
                    return null
                }

        // Guard must be set before we await so that onConnectionStateChange for THIS gatt
        // is accepted, while stale callbacks from a previous (closed) gatt are rejected.
        connectingGatt = g

        return try {
            if (withTimeoutOrNull(10_000.milliseconds) { connected.await() } == true) {
                onLog("connectOnce: connected")
                g
            } else {
                onLog("connectOnce: failed status=$lastConnectionStatus state=$lastConnectionState")
                try {
                    g.close()
                } catch (_: Exception) {
                }
                null
            }
        } finally {
            // Clear the guard before signalling completion so late callbacks from g are
            // already rejected before the caller can start the next attempt.
            connectingGatt = null
            if (!connected.isCompleted) connected.complete(false)
        }
    }

    // ── GATT setup ────────────────────────────────────────────────────────────

    @RequiresPermission(Manifest.permission.BLUETOOTH_CONNECT)
    suspend fun requestMtu(
        gatt: BluetoothGatt,
        mtu: Int,
    ) {
        pendingMtu = CompletableDeferred()
        onLog("requestMtu: requesting $mtu")
        if (!gatt.requestMtu(mtu)) throw IllegalStateException("requestMtu() returned false")
        negotiatedMtu = withTimeoutOrNull(10_000.milliseconds) { pendingMtu?.await() } ?: negotiatedMtu
        onLog("requestMtu: negotiated=$negotiatedMtu")
    }

    @RequiresPermission(Manifest.permission.BLUETOOTH_CONNECT)
    suspend fun discoverServices(gatt: BluetoothGatt) {
        pendingServices = CompletableDeferred()
        onLog("discoverServices: requesting")
        if (!gatt.discoverServices()) throw IllegalStateException("discoverServices() returned false")
        withTimeoutOrNull(10_000.milliseconds) { pendingServices?.await() }
            ?: throw IllegalStateException("Service discovery timed out")
        onLog("discoverServices: done")
    }

    @RequiresPermission(Manifest.permission.BLUETOOTH_CONNECT)
    suspend fun subscribeToNotifications(
        gatt: BluetoothGatt,
        service: BluetoothGattService,
    ) {
        val notifyUuids =
            listOf(
                "9c810d26-b605-4306-8c1c-755a1ba3066c", // main RX — most CCU responses
                "75ab2788-87f6-4f61-ba9c-6f85a12600d1", // DDT status
                "77808451-e42f-4f6b-83ed-3ab44aa3d16a", // DDT hi-bandwidth
                "347456ee-a588-4dd5-bd98-62be3672e27a", // DDT large
                "e974b9a1-fa31-43c6-91ae-09931f3f6ef1", // DDT v2
                "ab8d233a-cc58-4932-b397-a3751911e021", // YRC RX
                "56dc2d58-cbe1-4ba9-a104-e9b8eb4da95c", // DDT v3 (indication)
            )
        val characteristics =
            notifyUuids
                .mapNotNull { uuid ->
                    service.getCharacteristic(UUID.fromString(uuid))
                }.filter { char ->
                    val props = char.properties
                    props and BluetoothGattCharacteristic.PROPERTY_NOTIFY != 0 ||
                        props and BluetoothGattCharacteristic.PROPERTY_INDICATE != 0
                }
        onLog("subscribeToNotifications: ${characteristics.size} characteristics")
        for (char in characteristics) {
            enableNotifications(gatt, char)
            onLog("Subscribed ${char.uuid}")
        }
    }

    @RequiresPermission(Manifest.permission.BLUETOOTH_CONNECT)
    private suspend fun enableNotifications(
        gatt: BluetoothGatt,
        char: BluetoothGattCharacteristic,
    ) {
        pendingDescriptorWrite = CompletableDeferred()
        if (!gatt.setCharacteristicNotification(char, true)) {
            onLog("enableNotifications: setCharacteristicNotification failed for ${char.uuid}")
            return
        }
        val descriptor = char.getDescriptor(CCCD_UUID) ?: return
        descriptor.value =
            if (char.properties and BluetoothGattCharacteristic.PROPERTY_INDICATE != 0) {
                BluetoothGattDescriptor.ENABLE_INDICATION_VALUE
            } else {
                BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE
            }
        if (!gatt.writeDescriptor(descriptor)) throw IllegalStateException("Descriptor write failed for ${char.uuid}")
        withTimeoutOrNull(5_000.milliseconds) { pendingDescriptorWrite?.await() }
            ?: throw IllegalStateException("Descriptor write timed out for ${char.uuid}")
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    @Suppress("MissingPermission", "Asserted")
    suspend fun write(
        gatt: BluetoothGatt,
        char: BluetoothGattCharacteristic,
        value: ByteArray,
    ) {
        PermissionsException.assertGranted(context, Manifest.permission.BLUETOOTH_CONNECT)
        writeMutex.withLock {
            pendingWrite = CompletableDeferred()
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                val status = gatt.writeCharacteristic(char, value, BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT)
                if (status != BluetoothGatt.GATT_SUCCESS) {
                    throw IllegalStateException("writeCharacteristic failed for ${char.uuid}: status=$status")
                }
            } else {
                @Suppress("DEPRECATION", "Old APIs")
                char.value = value
                @Suppress("DEPRECATION", "Old APIs")
                if (!gatt.writeCharacteristic(char)) throw IllegalStateException("writeCharacteristic failed for ${char.uuid}")
            }
            withTimeoutOrNull(5_000.milliseconds) { pendingWrite?.await() }
                ?: throw IllegalStateException("writeCharacteristic timed out for ${char.uuid}")
        }
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    @Suppress("MissingPermission", "Asserted")
    suspend fun read(
        gatt: BluetoothGatt,
        char: BluetoothGattCharacteristic,
    ): ByteArray? {
        PermissionsException.assertGranted(context, Manifest.permission.BLUETOOTH_CONNECT)
        return writeMutex.withLock {
            pendingRead = CompletableDeferred()
            if (!gatt.readCharacteristic(char)) {
                throw IllegalStateException("readCharacteristic failed for ${char.uuid}")
            }
            withTimeoutOrNull(5_000.milliseconds) { pendingRead?.await() }
        }
    }

    // ── Disconnect ────────────────────────────────────────────────────────────

    fun disconnect() {
        sppJob?.cancel()
        sppJob = null
        try {
            sppServerSocket?.close()
        } catch (_: Exception) {
        }
        sppServerSocket = null
        try {
            sppSocket?.close()
        } catch (_: Exception) {
        }
        sppSocket = null
        pendingConnect?.cancel()
        pendingServices?.cancel()
        pendingMtu?.cancel()
        pendingWrite?.cancel()
        pendingRead?.cancel()
        pendingDescriptorWrite?.cancel()
        connectingGatt = null

        if (ActivityCompat.checkSelfPermission(
                context,
                Manifest.permission.BLUETOOTH_CONNECT,
            ) != PackageManager.PERMISSION_GRANTED
        ) {
            try {
                activeGatt?.disconnect()
            } catch (_: Exception) {
                // Don't care.
            }
            try {
                activeGatt?.close()
            } catch (_: Exception) {
                // Don't care.
            }
        }
        activeGatt = null
        negotiatedMtu = 23
    }

    // ── GATT callback ─────────────────────────────────────────────────────────

    private val gattCallback =
        object : BluetoothGattCallback() {
            @RequiresPermission(Manifest.permission.BLUETOOTH_CONNECT)
            override fun onConnectionStateChange(
                gatt: BluetoothGatt,
                status: Int,
                newState: Int,
            ) {
                if (gatt !== connectingGatt && gatt !== activeGatt) {
                    onLog("onConnectionStateChange: stale gatt rejected status=$status newState=$newState")
                    try {
                        gatt.close()
                    } catch (_: Exception) {
                    }
                    return
                }
                lastConnectionStatus = status
                lastConnectionState = newState
                onLog("onConnectionStateChange status=$status newState=$newState")
                when {
                    newState == BluetoothProfile.STATE_CONNECTED -> {
                        pendingConnect?.complete(true)
                    }

                    newState == BluetoothProfile.STATE_DISCONNECTED -> {
                        pendingConnect?.complete(false)
                        if (activeGatt != null) onDisconnected()
                    }

                    status != BluetoothGatt.GATT_SUCCESS -> {
                        pendingConnect?.complete(false)
                    }
                }
            }

            override fun onServicesDiscovered(
                gatt: BluetoothGatt,
                status: Int,
            ) {
                onLog("onServicesDiscovered status=$status count=${gatt.services.size}")
                pendingServices?.complete(status == BluetoothGatt.GATT_SUCCESS)
            }

            override fun onMtuChanged(
                gatt: BluetoothGatt,
                mtu: Int,
                status: Int,
            ) {
                onLog("onMtuChanged status=$status mtu=$mtu")
                if (status == BluetoothGatt.GATT_SUCCESS) {
                    negotiatedMtu = mtu
                    pendingMtu?.complete(mtu)
                } else {
                    pendingMtu?.complete(negotiatedMtu)
                }
            }

            @Suppress("DEPRECATION")
            override fun onCharacteristicChanged(
                gatt: BluetoothGatt,
                char: BluetoothGattCharacteristic,
            ) {
                dispatchNotification(char.uuid, char.value)
            }

            override fun onCharacteristicChanged(
                gatt: BluetoothGatt,
                char: BluetoothGattCharacteristic,
                value: ByteArray,
            ) {
                dispatchNotification(char.uuid, value)
            }

            @Suppress("DEPRECATION")
            override fun onCharacteristicRead(
                gatt: BluetoothGatt,
                char: BluetoothGattCharacteristic,
                status: Int,
            ) {
                onLog("onCharacteristicRead uuid=${char.uuid} status=$status")
                pendingRead?.complete(if (status == BluetoothGatt.GATT_SUCCESS) char.value else null)
            }

            override fun onCharacteristicRead(
                gatt: BluetoothGatt,
                char: BluetoothGattCharacteristic,
                value: ByteArray,
                status: Int,
            ) {
                onLog("onCharacteristicRead uuid=${char.uuid} status=$status len=${value.size}")
                pendingRead?.complete(if (status == BluetoothGatt.GATT_SUCCESS) value else null)
            }

            @Suppress("DEPRECATION")
            override fun onCharacteristicWrite(
                gatt: BluetoothGatt,
                char: BluetoothGattCharacteristic,
                status: Int,
            ) {
                onLog("onCharacteristicWrite uuid=${char.uuid} status=$status len=${char.value?.size ?: 0}")
                pendingWrite?.complete(status)
            }

            override fun onDescriptorWrite(
                gatt: BluetoothGatt,
                descriptor: BluetoothGattDescriptor,
                status: Int,
            ) {
                onLog("onDescriptorWrite uuid=${descriptor.uuid} status=$status char=${descriptor.characteristic.uuid}")
                pendingDescriptorWrite?.complete(status)
            }
        }

    private fun dispatchNotification(
        uuid: UUID,
        value: ByteArray,
    ) {
        val hex = value.joinToString(" ") { "%02X".format(it) }
        onLog("NOTIFY $uuid $hex")
        _notifications.tryEmit(GattNotificationEvent(uuid, value))
    }

    companion object {
        private val CCCD_UUID: UUID = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb")
        private val SPP_SERVICE_UUID: UUID = UUID.fromString("00008911-0000-1000-8000-00805f9b34fb")
    }
}
