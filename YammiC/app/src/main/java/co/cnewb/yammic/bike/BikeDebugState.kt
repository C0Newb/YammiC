package co.cnewb.yammic.bike

import co.cnewb.yammic.ymcsdk.models.YmcAuthentication

data class BikeDebugState(
    val permissionsGranted: Boolean = false,
    val isBusy: Boolean = false,
    val connectionLabel: String = "Idle",
    val lastEvent: String = "Ready",
    val lastError: String = "",
    val deviceName: String = "YCCU_01234567890123",
    val ccuId: String = "01234567890123",
    val passKey: String = "012345",
    val phoneUuid: String = "0123456789abcdef0123456789abcdef",
    val logs: List<String> = emptyList(),
    val logFileLabel: String = "Downloads/YammiC",
) {
    val credentials: YmcAuthentication
        get() =
            YmcAuthentication(
                ccuId = ccuId.trim(),
                passKey = passKey.trim(),
            )
}
