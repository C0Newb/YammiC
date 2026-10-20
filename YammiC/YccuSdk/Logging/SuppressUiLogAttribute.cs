using CNCO.YccuSdk.Packets;

namespace CNCO.YccuSdk.Logging;

/// <summary>
/// Keeps the Bluetooth traffic of a command out of the UI's log (it still goes to the log file and logcat). Put it on the
/// packet class (or whatever class owns the command) for every noisy command - a stream of CAN responses or
/// Virtual Pit Board chunks would otherwise bury the log and make the page unresponsive. <see cref="UiLogFilter"/> finds
/// the attribute by reflection.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SuppressUiLogAttribute : Attribute
{
    public SuppressUiLogAttribute(YmcCommandCode command) => Command = command;

    public YmcCommandCode Command { get; }
}
