namespace CNCO.YccuSdk.Can;

/// <summary>
/// Decoded CAN values; null when the signal hasn't been received (or has gone stale). The IDs and scalings are from
/// the reference app's reading of a 2nd-generation unit and are NOT yet verified on a bike - compare them with the
/// raw bytes on the CAN page.
/// </summary>
public record CanValues
{
    public double? Rpm { get; init; }

    /// <summary>
    /// 0 = neutral, 1..6 = gear, 7 = between gears (see <see cref="CanSignals.GearText"/>).
    /// </summary>
    public int? Gear { get; init; }

    public double? WaterTempC { get; init; }
    public double? IntakeAirTempC { get; init; }

    /// <summary>
    /// Throttle position sensor, in the reference app's units (scale unverified).
    /// </summary>
    public double? Tps { get; init; }

    /// <summary>
    /// Accelerator position sensor, same units as <see cref="Tps"/>.
    /// </summary>
    public double? Aps { get; init; }

    public double? BankAngle { get; init; }
    public double? PitchRate { get; init; }

    /// <summary>
    /// Acceleration in the reference app's units (offset by -7 so that 0 is at rest).
    /// </summary>
    public double? AccelX { get; init; }

    public double? AccelY { get; init; }
    public double? FrontWheelSpeed { get; init; }
    public double? RearWheelSpeed { get; init; }

    public static CanValues Empty { get; } = new();

    /// <summary>
    /// Example values for the editor preview.
    /// </summary>
    public static CanValues Sample { get; } = new()
    {
        Rpm = 4350,
        Gear = 3,
        WaterTempC = 88,
        IntakeAirTempC = 31,
        Tps = 12.5,
        Aps = 13.0,
        BankAngle = 22.5,
        PitchRate = 1.5,
        AccelX = 0.15,
        AccelY = -0.08,
        FrontWheelSpeed = 62.4,
        RearWheelSpeed = 62.5,
    };
}

/// <summary>
/// What the CAN page lists: how each known ID is decoded.
/// </summary>
public sealed record CanSignalInfo(int Id, string Name, string Description);

public static class CanSignals
{
    /// <summary>
    /// Every CAN ID we know how to decode (the 2nd-generation set the reference app polls for the values above).
    /// </summary>
    public static IReadOnlyList<int> KnownIds { get; } = new[] { 0x0250, 0x0258, 0x0276, 0x0279, 0x027C, 0x0512 };

    /// <summary>
    /// What is requested until the user picks otherwise: just the throttle (0x0279), to keep Bluetooth traffic low.
    /// </summary>
    public static IReadOnlyList<int> DefaultIds { get; } = new[] { 0x0279 };

    public static IReadOnlyList<CanSignalInfo> Catalog { get; } = new CanSignalInfo[]
    {
        new(0x0250, "Acceleration", "X: u16(b0,b1)/1000-7; Y: u16(b2,b3)/1000-7"),
        new(0x0258, "Bank angle / pitch rate", "bank: nibbles of b0..b3, /100-90; pitch: u16(b6,b7)/100-300"),
        new(0x0276, "Wheel speeds", "front: u16(b0,b1)/100; rear: u16(b2,b3)/100"),
        new(0x0279, "Throttle / accelerator", "TPS: u16(b0,b1)*125/1024; APS: u16(b2,b3)*125/1024"),
        new(0x027C, "RPM / gear", "rpm: u16(b0,b1)*100/256; gear: b4 & 7 (0 = N, 7 = between gears)"),
        new(0x0512, "Temperatures", "water: b0*160/256-30; intake air: b1*160/256-30"),
    };

    /// <summary>
    /// How long a reading is trusted before its values are blanked.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Decodes whatever is fresh in <paramref name="latest"/> (id -> (data, time received)).
    /// </summary>
    public static CanValues Decode(IReadOnlyDictionary<int, (byte[] Data, DateTime At)> latest, DateTime now)
    {
        byte[]? Fresh(int id, int minLength) =>
            latest.TryGetValue(id, out var r) && now - r.At <= StaleAfter && r.Data.Length >= minLength ? r.Data : null;

        var values = CanValues.Empty;

        var rpm = Fresh(0x027C, 4);
        if (rpm != null)
            values = values with { Rpm = U16(rpm, 0) * 100 / 256.0 };

        // Observed on a bike (the reference app reads a different bit): byte 4 is 0 for neutral, 1..6 for the gears and 7 between gears.
        var gearData = Fresh(0x027C, 5);
        if (gearData != null)
            values = values with { Gear = gearData[4] & 7 };

        var temps = Fresh(0x0512, 2);
        if (temps != null)
            values = values with { WaterTempC = Temp(temps[0]), IntakeAirTempC = Temp(temps[1]) };

        var throttle = Fresh(0x0279, 4);
        if (throttle != null)
            values = values with { Tps = U16(throttle, 0) * 125 / 1024.0, Aps = U16(throttle, 2) * 125 / 1024.0 };

        var lean = Fresh(0x0258, 8);
        if (lean != null)
        {
            // The reference app rearranges four nibbles: (b0&15)*4096 + (b2&15)*256 + (b1&15)*16 + (b3>>4).
            var bank = (lean[0] & 15) * 4096 + (lean[2] & 15) * 256 + (lean[1] & 15) * 16 + ((lean[3] >> 4) & 15);
            values = values with { BankAngle = bank / 100.0 - 90, PitchRate = U16(lean, 6) / 100.0 - 300 };
        }

        var accel = Fresh(0x0250, 4);
        if (accel != null)
            values = values with { AccelX = U16(accel, 0) / 1000.0 - 7, AccelY = U16(accel, 2) / 1000.0 - 7 };

        var wheels = Fresh(0x0276, 4);
        if (wheels != null)
            values = values with { FrontWheelSpeed = U16(wheels, 0) / 100.0, RearWheelSpeed = U16(wheels, 2) / 100.0 };

        return values;
    }

    /// <summary>
    /// N for neutral, "-" while between gears (7), otherwise the gear number; a dash when unknown.
    /// </summary>
    public static string GearText(int? gear) => gear switch
    {
        null => "—",
        0 => "N",
        7 => "-",
        var g => g.Value.ToString(System.Globalization.CultureInfo.CurrentCulture),
    };

    private static int U16(byte[] data, int offset) => data[offset] * 256 + data[offset + 1];

    private static double Temp(byte raw) => raw * 160 / 256.0 - 30;
}
