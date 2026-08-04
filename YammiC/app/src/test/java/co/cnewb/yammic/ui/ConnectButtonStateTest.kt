package co.cnewb.yammic.ui

import org.junit.Assert.assertEquals
import org.junit.Test

class ConnectButtonStateTest {
    @Test
    fun `idle when not busy and not done`() {
        assertEquals(ConnectButtonState.IDLE, deriveConnectButtonState("Idle", isBusy = false))
    }

    @Test
    fun `connecting while busy`() {
        assertEquals(ConnectButtonState.CONNECTING, deriveConnectButtonState("Scanning", isBusy = true))
    }

    @Test
    fun `connected once label reaches Done`() {
        assertEquals(ConnectButtonState.CONNECTED, deriveConnectButtonState("Done", isBusy = false))
    }

    @Test
    fun `idle after disconnect`() {
        assertEquals(ConnectButtonState.IDLE, deriveConnectButtonState("Disconnected", isBusy = false))
    }
}
