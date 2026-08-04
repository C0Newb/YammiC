package co.cnewb.yammic.ymcsdk.packets.app

import co.cnewb.yammic.ymcsdk.YmcConnectionContext
import co.cnewb.yammic.ymcsdk.packets.YmcCommandCode
import co.cnewb.yammic.ymcsdk.packets.YmcPacket
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler

/**
 * Representing a packet sent by us to the CCU.
 */
abstract class AppOutgoingPacket : YmcPacket {
    constructor(
        packetHandler: YmcPacketHandler,
        commandCode: YmcCommandCode,
    ) : super(commandCode, packetHandler)

    /** Frame preamble (command code) */
    protected val preamble: ByteArray = shortBytes(commandCode)

    /** Packet transport style. */
    abstract val transport: Transport

    /** Packet transport styles. */
    enum class Transport {
        Simple,
        Ddt,

        /**
         * Written to the Classic-Bluetooth SPP socket instead of a BLE GATT characteristic.
         * Used only for [co.cnewb.yammic.ymcsdk.packets.YmcCommandCode.SendVirtualPitBoardGraphic] —
         * the reference SDK sends Virtual Pit Board graphic bytes over the same RFCOMM link used
         * for the OTA/auth handoff, while every other MPN command stays on BLE.
         */
        Spp,
    }

    /** Serialize the packet to a byte array to transmit via BlueTooth. */
    abstract fun serialize(requestId: Byte): ByteArray

    /** Sends this packet out. */
    suspend fun send(connectionContext: YmcConnectionContext) {
        this.packetHandler.send(connectionContext, this)
    }
}
