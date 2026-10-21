namespace CNCO.YccuSdk.Navigation;

/// <summary>
/// Pixel size of the CCU's navigation display, which images must match to render correctly.
/// </summary>
public readonly record struct NavDisplaySize(int Width, int Height)
{
    /// <summary>
    /// Size used until the CCU has announced its part number.
    /// </summary>
    public static readonly NavDisplaySize Default = new(480, 240);

    /// <summary>
    /// Maps the CCU part number from SEC_DATA to its display size: `006-B3952` is the base 480x234 layout and
    /// `006-B4160` / `006-B4920` are the 480x240 ones. Anything else (including an unreadable number) falls back to
    /// 480x234, the safer of the two.
    /// </summary>
    public static NavDisplaySize FromPartNumber(string? partNumber)
    {
        if (partNumber != null)
        {
            if (partNumber.StartsWith("006-B4160", StringComparison.Ordinal) ||
                partNumber.StartsWith("006-B4920", StringComparison.Ordinal))
                return new NavDisplaySize(480, 240);
        }
        return new NavDisplaySize(480, 234);
    }
}

/// <summary>
/// DAY_NIGHT_MODE_UPDATE values (StreetCross's NaviLiteDayNightModeType).
/// </summary>
public enum NavDayNightMode : byte
{
    Auto = 0,
    Day = 1,
    Night = 2,
}
