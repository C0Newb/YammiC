namespace CNCO.YccuSdk.Platform;

/// <summary>
/// The host-provided services the SDK runs on. On Android build one with
/// <c>AndroidYmcPlatform.Create(context)</c>.
/// </summary>
public sealed class YmcPlatform
{
    public required IYmcBluetoothConnectionManager Bluetooth { get; init; }

    public required IYmcPhoneStatusProvider PhoneStatus { get; init; }

    public required IYmcVolumeController Volume { get; init; }

    public required IYmcKeyValueStore Storage { get; init; }
}
