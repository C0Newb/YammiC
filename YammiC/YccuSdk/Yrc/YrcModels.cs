namespace CNCO.YccuSdk.Yrc;

/// <summary>What a data request or response carries.</summary>
public static class YrcInfoKind
{
    public const int Common = 1;
    public const int Names = 2;
    public const int MinMax = 3;
    public const int Current = 4;
}

/// <summary>One setting inside a ride mode (power, traction control, ...): its value and allowed range.</summary>
public sealed record YrcValue(int Kind, int Current, int Min, int Max);

/// <summary>One ride mode (a "profile"): which mode it is, its name, and its settings.</summary>
public sealed record YrcProfile(int Mode, int SavedNo, int ModeType, string Name, IReadOnlyList<YrcValue> Values)
{
    public string ModeName => YrcNames.Mode(Mode);
}

/// <summary>One parsed message from the bike (or one we send).</summary>
public sealed record YrcInfo(
    int Result,
    IReadOnlyList<int> DataKinds,
    int KwStatusFlag,
    bool YccsModelFlag,
    bool YccsAtFlag,
    bool TrackFlag,
    int SusInfo,
    int CurrentMode,
    int CurrentSusMode,
    int StartTrigger,
    IReadOnlyList<YrcProfile> Profiles);

/// <summary>Names for the numeric codes in the ride-settings messages.</summary>
public static class YrcNames
{
    public const int ModeAll = 255;

    public static string Mode(int mode) => mode switch
    {
        0 => "None",
        >= 16 and <= 21 => $"MT{mode - 15}",
        >= 22 and <= 27 => $"AT{mode - 21}",
        >= 28 and <= 31 => $"TR{mode - 27} (Track {mode - 27})",
        >= 32 and <= 46 => $"SUS{mode - 31:00}",
        255 => "All",
        _ => $"Mode {mode}",
    };

    public static string Parameter(int kind) => kind switch
    {
        1 => "PWR",
        2 => "TCS",
        3 => "SCS",
        4 => "LIF",
        5 => "BSR",
        6 => "LCS",
        7 => "QSS",
        8 => "DSS",
        9 => "BC",
        10 => "EBM",
        11 => "SUS",
        >= 49 and <= 72 => $"SUS {kind - 48}",
        _ => $"Setting {kind}",
    };

    public static string ModeType(int type) => type switch
    {
        1 => "MT (no YCCS)",
        2 => "MT (YCCS)",
        3 => "AT",
        4 => "Track",
        5 => "Suspension",
        _ => $"Type {type}",
    };
}
