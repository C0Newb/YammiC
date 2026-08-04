package co.cnewb.yammic.ui

enum class ConnectButtonState { IDLE, CONNECTING, CONNECTED }

fun deriveConnectButtonState(
    connectionLabel: String,
    isBusy: Boolean,
): ConnectButtonState =
    when {
        connectionLabel == "Done" -> ConnectButtonState.CONNECTED
        isBusy -> ConnectButtonState.CONNECTING
        else -> ConnectButtonState.IDLE
    }
