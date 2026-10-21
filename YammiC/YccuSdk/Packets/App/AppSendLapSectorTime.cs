namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Sends one lap (and sector gap) time for the CCU to show. Wire (simple write): `[cmd 2B: 0x0251]` then
/// `[requestId][lapNo][lapTime 3B][referenceTime 3B][sectorNo][gap1 2B][gap2 2B][gap3 2B][gap4 2B][correction]`.
/// Times are in hundredths of a second. The answer (0x0252) is `[requestId][1 = OK, 0 = NG]`.
/// </summary>
public class AppSendLapSectorTime : AppOutgoingRequest
{
    /// <summary>
    /// Lap number meaning "none".
    /// </summary>
    public const int NoLap = 255;

    /// <summary>
    /// Lap or reference time meaning "none".
    /// </summary>
    public const int NoTime = 1048575;

    /// <summary>
    /// Gap value meaning "this sector was not measured".
    /// </summary>
    public const int GapNotMeasured = 12287;

    /// <summary>
    /// Gap value meaning "invalid".
    /// </summary>
    public const int GapInvalid = 16383;

    /// <summary>
    /// The gap range is sent shifted up by this much so it stays positive.
    /// </summary>
    public const int GapOffset = 5999;

    private readonly int _lapNo;
    private readonly int _lapTime;
    private readonly int _referenceTime;
    private readonly int _sectorNo;
    private readonly int[] _gaps;
    private readonly int _correction;

    public AppSendLapSectorTime(
        IYmcPacketHandler packetHandler,
        int lapNo,
        int lapTimeCs,
        int referenceTimeCs,
        int sectorNo,
        int[] gapsCs,
        int correction = 255)
        : base(packetHandler, YmcCommandCode.SendLapSectorTime)
    {
        _lapNo = lapNo;
        _lapTime = lapTimeCs;
        _referenceTime = referenceTimeCs;
        _sectorNo = sectorNo;
        _gaps = gapsCs;
        _correction = correction;
    }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId)
    {
        var bytes = new List<byte> { requestId, (byte)_lapNo };
        bytes.AddRange(Int24(_lapTime));
        bytes.AddRange(Int24(_referenceTime));
        bytes.Add((byte)_sectorNo);
        for (var i = 0; i < 4; i++)
        {
            var gap = i < _gaps.Length ? _gaps[i] : GapNotMeasured;
            if (gap != GapNotMeasured && gap != GapInvalid) gap += GapOffset;
            bytes.AddRange(ShortBytes(gap));
        }
        bytes.Add((byte)_correction);
        return Concat(Preamble, bytes.ToArray());
    }

    /// <summary>
    /// Checks the ranges the bike accepts; returns a message, or null when everything is in range.
    /// </summary>
    public static string? Validate(int lapNo, int lapTimeCs, int referenceTimeCs, int sectorNo, int[] gapsCs)
    {
        if (lapNo != NoLap && lapNo is < 0 or > 99) return $"Lap number {lapNo} out of range (0-99)";
        if (lapTimeCs != NoTime && lapTimeCs is < 0 or > 359999) return $"Lap time {lapTimeCs} out of range (0-359999)";
        if (referenceTimeCs != NoTime && referenceTimeCs is < 0 or > 359999) return $"Reference time {referenceTimeCs} out of range (0-359999)";
        if (sectorNo is < 0 or > 3) return $"Sector number {sectorNo} out of range (0-3)";
        for (var i = 0; i < gapsCs.Length; i++)
        {
            var g = gapsCs[i];
            if (g != GapNotMeasured && g != GapInvalid && g is < -5999 or > 5999) return $"Gap {i + 1} {g} out of range (-5999-5999)";
        }
        return null;
    }
}
