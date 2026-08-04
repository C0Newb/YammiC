package co.cnewb.yammic.ymcsdk.navigation

import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothSocket
import co.cnewb.yammic.logging.Logger
import java.io.InputStream
import java.io.OutputStream
import java.util.UUID

/**
 * A [NavByteChannel] over a Bluetooth RFCOMM (SPP) link to the CCU bike device.
 * Single responsibility: move bytes; it owns no protocol knowledge.
 *
 * Ported from Pillion's RfcommByteChannel.
 */
class NavRfcommChannel(
    logger: Logger = Logger(tag = "NavRfcommChannel", sinks = emptyList()),
) : NavByteChannel {
    private val log = logger.section("NavRfcommChannel")
    private var socket: BluetoothSocket? = null
    private var input: InputStream? = null
    private var output: OutputStream? = null

    @SuppressLint("MissingPermission") // BLUETOOTH_CONNECT is requested by the Activity before start
    override fun open() {
        val adapter = BluetoothAdapter.getDefaultAdapter() ?: error("no Bluetooth adapter")
        // Also worth checking: this app opens its own Classic-BT SPP RFCOMM socket for VPB
        // (YmcBluetoothConnectionManager, UUID 8911) alongside this one (UUID 7220) — two
        // simultaneous RFCOMM links to the same remote device. If both drop together in the
        // logs, that's the CCU's Classic-BT stack, not this channel.
        val ccu = adapter.bondedDevices.firstOrNull { d ->
            val n = d.name
            n != null && (n.startsWith("YCCU") || n.contains("CCU"))
        } ?: error("CCU device (YCCU…) is not paired")
        log.debug("connecting to ${ccu.name} (${ccu.address})")
        // cancelDiscovery() needs BLUETOOTH_SCAN on Android 12+; it's only a connect-speed
        // optimization (nothing is discovering here), so ignore it if the permission is absent.
        runCatching { adapter.cancelDiscovery() }
        val s = ccu.createInsecureRfcommSocketToServiceRecord(SPP_UUID)
        s.connect()
        socket = s
        input = s.inputStream
        output = s.outputStream
        log.debug("connected")
    }

    override fun write(bytes: ByteArray) {
        val out = output ?: error("channel not open")
        out.write(bytes)
        out.flush()
    }

    override fun read(buffer: ByteArray): Int =
        (input ?: error("channel not open")).read(buffer)

    override fun close() {
        log.debug("closing")
        runCatching { socket?.close() }
        socket = null; input = null; output = null
    }

    private companion object {
        // NaviLite SPP service UUID (0x7220).
        val SPP_UUID: UUID = UUID.fromString("00007220-0000-1000-8000-00805F9B34FB")
    }
}
