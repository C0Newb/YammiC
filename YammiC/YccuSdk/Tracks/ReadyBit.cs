using CNCO.YccuSdk.Can;

namespace CNCO.YccuSdk.Tracks;

public enum ReadyBitState
{
    /// <summary>No fresh data: do not act on it.</summary>
    Unknown,
    NotReady,
    Ready,
}

/// <summary>
/// Reads the bike's "timer ready" flag from the raw CAN readings: ID 0x03BF, byte 4, bit 5. The ID is from the official app's wire
/// handling and is not yet confirmed on every bike, so the raw bytes are logged by the caller.
/// </summary>
public static class ReadyBit
{
    public const int CanId = 0x03BF;
    public static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(3);

    public static ReadyBitState Read(IReadOnlyList<CanRawReading> raw, DateTime utcNow)
    {
        foreach (var reading in raw)
        {
            if (reading.Id != CanId) continue;
            if (utcNow - reading.ReceivedAt > FreshFor || reading.Data.Length < 5) return ReadyBitState.Unknown;
            return ((reading.Data[4] >> 5) & 1) == 1 ? ReadyBitState.Ready : ReadyBitState.NotReady;
        }
        return ReadyBitState.Unknown;
    }
}
