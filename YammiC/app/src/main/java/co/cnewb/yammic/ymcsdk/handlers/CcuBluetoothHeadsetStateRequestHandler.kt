package co.cnewb.yammic.ymcsdk.handlers

import co.cnewb.yammic.ymcsdk.YmcConnectionContext
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import co.cnewb.yammic.ymcsdk.packets.app.AppBluetoothHeadsetStateResponse
import co.cnewb.yammic.ymcsdk.packets.ccu.CcuBluetoothHeadsetStateRequest

/**
 * Handles [CcuBluetoothHeadsetStateRequest] by reporting whether a Bluetooth headset is connected.
 * The CCU gates music metadata delivery behind receiving [connected] = true.
 *
 * Update [connected] if headset state changes at runtime.
 */
class CcuBluetoothHeadsetStateRequestHandler(
    var connected: Boolean = true,
) : CcuRequestHandler<CcuBluetoothHeadsetStateRequest>() {
    override suspend fun invoke(
        packet: CcuBluetoothHeadsetStateRequest,
        packetHandler: YmcPacketHandler,
        connectionContext: YmcConnectionContext,
    ) {
        AppBluetoothHeadsetStateResponse(packetHandler, connected).send(connectionContext)
    }
}
