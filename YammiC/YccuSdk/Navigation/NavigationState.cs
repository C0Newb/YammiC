namespace CNCO.YccuSdk.Navigation;

/// <summary>
/// Lifecycle state of the navigation image channel.
/// Used by NavigationEngine to track connection and frame-sending status.
/// </summary>
public abstract record NavigationState
{
    private NavigationState()
    {
    }

    /// <summary>
    /// Initial state; no connection attempt in progress.
    /// </summary>
    public sealed record Idle : NavigationState;

    /// <summary>
    /// Connection handshake in progress.
    /// </summary>
    public sealed record Connecting : NavigationState;

    /// <summary>
    /// Connected and actively sending frames.
    /// </summary>
    /// <param name="Fps">Target frames-per-second (throttle). Typically 2-10.</param>
    /// <param name="LastFrameKb">
    /// Size of the last successfully sent image frame, in kilobytes.
    /// Useful for monitoring bandwidth and adjusting fps dynamically.
    /// </param>
    public sealed record Active(int Fps, int LastFrameKb) : NavigationState;

    /// <summary>
    /// Connection failed or was closed abnormally.
    /// </summary>
    /// <param name="Message">Diagnostic message (e.g., "Handshake timeout", "Semaphore acquire failed").</param>
    public sealed record Error(string Message) : NavigationState;
}
