using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CCU_Emulator.Emulation;

public enum TransportKind { LoopbackTcp, WindowsBle }

/// <summary>Live-editable switches for what the emulated CCU does. Bound directly by the Behavior tab.</summary>
public sealed partial class CcuBehavior : ObservableObject
{
    [ObservableProperty] public partial bool SendLibVersion { get; set; } = true;
    [ObservableProperty] public partial bool SendLanguageRequest { get; set; } = true;
    [ObservableProperty] public partial bool SendWeatherRequest { get; set; } = true;
    [ObservableProperty] public partial bool SendDeviceTimeRequest { get; set; } = true;
    [ObservableProperty] public partial bool SendHeadsetStateRequest { get; set; } = true;
    [ObservableProperty] public partial bool SendVolumeControllableRequest { get; set; } = true;
    [ObservableProperty] public partial bool SendCellSignalRequest { get; set; } = true;
    [ObservableProperty] public partial bool SendPing { get; set; } = true;
    [ObservableProperty] public partial int PingIntervalSeconds { get; set; } = 10;

    /// <summary>Answer VPB requests with 0x0232 (reject) instead of 0x0233.</summary>
    [ObservableProperty] public partial bool RejectVpbRequests { get; set; }

    /// <summary>Answer every authentication with failure.</summary>
    [ObservableProperty] public partial bool ForceAuthFailure { get; set; }
}

public sealed class CcuSettings
{
    /// <summary>14 ASCII characters.</summary>
    public string CcuId { get; set; } = "00000000000000";

    /// <summary>6 ASCII characters.</summary>
    public string PassKey { get; set; } = "000000";

    public string Mac { get; set; } = "00:00:00:00:00:00";

    public TransportKind Transport { get; set; } = TransportKind.LoopbackTcp;

    public string TcpBindAddress { get; set; } = "127.0.0.1";
    public int TcpPort { get; set; } = 48800;

    /// <summary>Connect the Classic SPP link after the BLE link comes up (needed for VPB images).</summary>
    public bool SppEnabled { get; set; } = true;

    /// <summary>Classic Bluetooth address of the phone, for the Windows SPP client. Empty = unset.</summary>
    public string PhoneBluetoothAddress { get; set; } = "";

    public CcuBehavior Behavior { get; set; } = new();

    [JsonIgnore]
    public string AdvertisedName => "YCCU_" + CcuId;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (CcuId.Length != 14 || !CcuId.All(char.IsAsciiLetterOrDigit)) errors.Add("CcuId must be 14 ASCII letters/digits.");
        if (PassKey.Length != 6 || !PassKey.All(char.IsAsciiLetterOrDigit)) errors.Add("PassKey must be 6 ASCII letters/digits.");
        return errors;
    }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Loads the settings, writing a default file first when none exists.</summary>
    public static CcuSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            var defaults = new CcuSettings();
            Save(path, defaults);
            return defaults;
        }
        return JsonSerializer.Deserialize<CcuSettings>(File.ReadAllText(path), Options) ?? new CcuSettings();
    }

    public static void Save(string path, CcuSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
