namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// VehicleSettingDatetime (cmd 0x000A) - sync the CCU clock.
/// `[cmd 2B][requestId 1B][time 8B big-endian seconds]`.
///
/// The CCU expects Unix time in SECONDS (not milliseconds), adjusted by timezone offset.
/// Official app sends: (currentTimeMillis + timezone_offset_ms) / 1000
/// </summary>
public class AppGetDeviceTimeResponse : AppOutgoingResponse
{
    private readonly long _currentTimeMillis;

    public AppGetDeviceTimeResponse(IYmcPacketHandler packetHandler, long? currentTimeMillis = null)
        : base(packetHandler, YmcCommandCode.GetDatetimeResponse)
    {
        _currentTimeMillis = currentTimeMillis ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId)
    {
        // Zone offset + DST offset == the current local UTC offset.
        var timezoneOffsetMs = (long)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMilliseconds;
        var timeInSeconds = (_currentTimeMillis + timezoneOffsetMs) / 1000;

        return Concat(
            Preamble,
            new[] { requestId },
            new[]
            {
                (byte)((timeInSeconds >> 56) & 0xFF),
                (byte)((timeInSeconds >> 48) & 0xFF),
                (byte)((timeInSeconds >> 40) & 0xFF),
                (byte)((timeInSeconds >> 32) & 0xFF),
                (byte)((timeInSeconds >> 24) & 0xFF),
                (byte)((timeInSeconds >> 16) & 0xFF),
                (byte)((timeInSeconds >> 8) & 0xFF),
                (byte)(timeInSeconds & 0xFF),
            });
    }
}
