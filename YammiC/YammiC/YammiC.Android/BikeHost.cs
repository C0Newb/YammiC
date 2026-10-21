using CNCO.YccuSdk.Bike;
using CNCO.YccuSdk.Tracks;

namespace YammiC.Android;

/// <summary>
/// Process-wide holder for the SDK service, so the Application (which builds it), the foreground
/// service and the Activity all see the same instance. Replaces the Kotlin service binder.
/// </summary>
internal static class BikeHost
{
    public static BikeService? Service { get; set; }

    public static TrackTimerService? TrackTimer { get; set; }
}
