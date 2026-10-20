using CNCO.YccuSdk.Models;

namespace CNCO.YccuSdk;

/// <summary>
/// Snapshot of the connection / credentials / log state. Immutable; use <c>with</c> to derive updates.
/// </summary>
public record BikeDebugState
{
    public bool PermissionsGranted { get; init; }
    public bool IsBusy { get; init; }
    public string ConnectionLabel { get; init; } = "Idle";
    public string LastEvent { get; init; } = "Ready";
    public string LastError { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public string CcuId { get; init; } = "";
    public string PassKey { get; init; } = "";
    public string PhoneUuid { get; init; } = "";
    public IReadOnlyList<string> Logs { get; init; } = Array.Empty<string>();
    public string LogFileLabel { get; init; } = "Downloads/YammiC";

    public YmcAuthentication Credentials => new(CcuId.Trim(), PassKey.Trim());
}
