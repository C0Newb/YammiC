using System.Reflection;
using CNCO.YccuSdk.Packets;

namespace CNCO.YccuSdk.Logging;

/// <summary>
/// Decides which GATT chatter reaches the UI log. The commands listed by <see cref="SuppressUiLogAttribute"/> are
/// recognised on the way in (by their command code, also across the fragments of a bike-to-phone DDT v2/v3 session) and on
/// the way out (while one of them is being sent, see <see cref="BeginSend"/>).
/// </summary>
public static class UiLogFilter
{
    private static readonly Lazy<HashSet<int>> SuppressedCodes = new(FindSuppressedCodes);

    // DDT v3 sequence number -> whether that session is a suppressed command. Only the first fragment carries the code.
    private static readonly Dictionary<int, bool> V3Sessions = new();
    private static readonly object V3Lock = new();

    private static int _suppressedSends;

    private static HashSet<int> FindSuppressedCodes()
    {
        var codes = new HashSet<int>();
        foreach (var type in typeof(UiLogFilter).Assembly.GetTypes())
        {
            foreach (var attribute in type.GetCustomAttributes<SuppressUiLogAttribute>(inherit: false))
                codes.Add((int)attribute.Command & 0xFFFF);
        }
        return codes;
    }

    /// <summary>
    /// True when the command code (as it appears on the wire, 2 bytes big-endian) is marked <see cref="SuppressUiLogAttribute"/>.
    /// </summary>
    public static bool IsSuppressed(int commandCode) => SuppressedCodes.Value.Contains(commandCode & 0xFFFF);

    /// <summary>
    /// True when the notification belongs to a suppressed command and should not be shown in the UI log.
    /// </summary>
    public static bool IsSuppressedNotification(Guid characteristic, byte[] value)
    {
        if (characteristic == YmcBikeProtocol.MainRxUuid)
            return value.Length >= 2 && IsSuppressed((value[0] << 8) | value[1]);

        if (characteristic == YmcBikeProtocol.DdtV3Uuid || characteristic == YmcBikeProtocol.DdtV2Uuid)
        {
            if (value.Length < 4) return false;
            var sequence = (characteristic == YmcBikeProtocol.DdtV2Uuid ? 0x10000 : 0) | (value[0] << 8) | value[1];
            lock (V3Lock)
            {
                if (value[2] == 1 && value.Length >= 6)
                {
                    if (V3Sessions.Count > 64) V3Sessions.Clear();
                    V3Sessions[sequence] = IsSuppressed((value[4] << 8) | value[5]);
                }
                return V3Sessions.TryGetValue(sequence, out var suppressed) && suppressed;
            }
        }

        // Status notifications of the phone-to-bike DDT session belong to whatever is being sent.
        return characteristic == YmcBikeProtocol.StatusUuid && WritesSuppressed;
    }

    /// <summary>
    /// True while a suppressed command is being sent, so its GATT write callbacks stay out of the UI log.
    /// </summary>
    public static bool WritesSuppressed => Volatile.Read(ref _suppressedSends) > 0;

    /// <summary>
    /// Marks the send of <paramref name="commandCode"/> in progress; dispose when it is done.
    /// </summary>
    public static IDisposable BeginSend(int commandCode)
    {
        if (!IsSuppressed(commandCode)) return NoScope.Instance;
        Interlocked.Increment(ref _suppressedSends);
        return new SendScope();
    }

    private sealed class SendScope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Decrement(ref _suppressedSends);
        }
    }

    private sealed class NoScope : IDisposable
    {
        public static readonly NoScope Instance = new();

        public void Dispose()
        {
        }
    }
}
