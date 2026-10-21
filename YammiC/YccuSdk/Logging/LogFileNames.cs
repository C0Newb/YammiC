using System.Globalization;

namespace CNCO.YccuSdk.Logging;

/// <summary>
/// Names for the log files the app writes.
/// </summary>
public static class LogFileNames
{
    /// <summary>
    /// <c>yammic-YYYY_MM_DD-HH_mm_ss.log</c> for a local time: readable, and sorts in time order. (The line timestamps inside the file
    /// are local time too.) Android adds its own extension when it saves the file, so it may end up as <c>.log.txt</c>.
    /// </summary>
    public static string For(DateTime localTime) =>
        "yammic-" + localTime.ToString("yyyy_MM_dd-HH_mm_ss", CultureInfo.InvariantCulture) + ".log";
}
