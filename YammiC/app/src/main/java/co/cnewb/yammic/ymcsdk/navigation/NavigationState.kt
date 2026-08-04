package co.cnewb.yammic.ymcsdk.navigation

/**
 * Sealed class representing the lifecycle state of the navigation image channel.
 * Used by NavigationEngine to track connection and frame-sending status.
 */
sealed class NavigationState {
    /** Initial state; no connection attempt in progress. */
    object Idle : NavigationState()

    /** Connection handshake in progress. */
    object Connecting : NavigationState()

    /**
     * Connected and actively sending frames.
     *
     * @param fps Target frames-per-second (throttle). Typically 2–10.
     * @param lastFrameKb Size of the last successfully sent image frame, in kilobytes.
     *   Useful for monitoring bandwidth and adjusting fps dynamically.
     */
    data class Active(val fps: Int, val lastFrameKb: Int) : NavigationState()

    /**
     * Connection failed or was closed abnormally.
     *
     * @param message Diagnostic message (e.g., "Handshake timeout", "Semaphore acquire failed").
     */
    data class Error(val message: String) : NavigationState()
}
