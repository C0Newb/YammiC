namespace CNCO.YccuSdk.Screens;

/// <summary>
/// How scrolling text moves: it waits at the start, slides until its end shows, waits there, then starts over.
/// </summary>
public static class ScreenMarquee
{
    /// <summary>
    /// How long the text rests at its start before it moves.
    /// </summary>
    public static readonly TimeSpan StartPause = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// How long the end of the text rests before it goes back to the start.
    /// </summary>
    public static readonly TimeSpan EndPause = TimeSpan.FromSeconds(1.5);

    public const double PixelsPerSecond = 40;

    /// <summary>
    /// How far (in pixels, to the left) the text has moved <paramref name="elapsed"/> after it appeared.
    /// <paramref name="overflow"/> is how much wider the text is than its box; text that fits never moves.
    /// </summary>
    public static double Offset(double overflow, TimeSpan elapsed)
    {
        if (overflow <= 0) return 0;

        var travel = TimeSpan.FromSeconds(overflow / PixelsPerSecond);
        var cycle = StartPause + travel + EndPause;
        var t = TimeSpan.FromTicks(Math.Max(elapsed.Ticks, 0) % cycle.Ticks);

        if (t < StartPause) return 0;
        if (t < StartPause + travel) return (t - StartPause).TotalSeconds * PixelsPerSecond;
        return overflow;
    }
}
