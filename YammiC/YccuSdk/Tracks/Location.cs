namespace CNCO.YccuSdk.Tracks;

/// <summary>Where GPS fixes come from. The Android implementation uses the platform location manager.</summary>
public interface ILocationSource
{
    /// <summary>Whether location can be read now (in the foreground).</summary>
    bool HasPermission { get; }

    /// <summary>Whether location can still be read when the app is not showing (needed for the bike to start a session).</summary>
    bool HasBackgroundPermission { get; }

    /// <summary>The most recent fix the system knows of, if any.</summary>
    GpsFix? LastKnown { get; }

    /// <summary>
    /// Asks the system for location access: the normal permission, or (when <paramref name="background"/>) the "all the time" one,
    /// which on newer Android versions opens the app's settings. Never call this while a session is on.
    /// </summary>
    void RequestPermission(bool background);

    /// <summary>Starts delivering fixes about <paramref name="hz"/> times a second. Calling it again changes the rate.</summary>
    void Start(double hz, Action<GpsFix> onFix);

    void Stop();
}

/// <summary>For platforms without GPS (desktop, tests): permission is never granted.</summary>
public sealed class NoLocationSource : ILocationSource
{
    public bool HasPermission => false;
    public bool HasBackgroundPermission => false;
    public GpsFix? LastKnown => null;
    public void RequestPermission(bool background) { }
    public void Start(double hz, Action<GpsFix> onFix) { }
    public void Stop() { }
}

/// <summary>
/// Finds which GPS rates the phone really delivers. Android has no list of supported rates, so each candidate is requested for a
/// few seconds and the fixes that arrive are counted.
/// </summary>
public static class GpsRateProbe
{
    public static readonly IReadOnlyList<double> Candidates = new[] { 1.0, 2.0, 5.0, 10.0 };

    /// <summary>Fixes per second actually received (0 when there are too few to tell).</summary>
    public static double Measure(IReadOnlyList<GpsFix> fixes)
    {
        if (fixes.Count < 3) return 0;
        var seconds = (fixes[^1].ElapsedNanos - fixes[0].ElapsedNanos) / 1e9;
        return seconds <= 0 ? 0 : (fixes.Count - 1) / seconds;
    }

    /// <summary>A rate counts as supported when at least 80 % of it arrives.</summary>
    public static bool Supports(double requestedHz, double measuredHz) => measuredHz >= requestedHz * 0.8;

    /// <summary>Tries each candidate for <paramref name="perRate"/> and returns the supported ones (always including 1 Hz if any fixes arrived).</summary>
    public static async Task<IReadOnlyList<double>> ProbeAsync(ILocationSource source, TimeSpan perRate, CancellationToken cancellation = default)
    {
        var supported = new List<double>();
        try
        {
            foreach (var hz in Candidates)
            {
                var fixes = new List<GpsFix>();
                source.Start(hz, fix =>
                {
                    lock (fixes) fixes.Add(fix);
                });
                await Task.Delay(perRate, cancellation).ConfigureAwait(false);
                double measured;
                lock (fixes) measured = Measure(fixes.ToList());
                if (Supports(hz, measured)) supported.Add(hz);
            }
        }
        finally
        {
            source.Stop();
        }
        return supported;
    }
}

/// <summary>For platforms without a vibrator.</summary>
public sealed class NullVibrator : IVibrator
{
    public void Vibrate() { }
}
