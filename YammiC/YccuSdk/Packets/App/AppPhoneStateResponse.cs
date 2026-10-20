using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// PhoneStateCombined (cmd 0x013C) - proactively reports phone state to the CCU.
///
/// All values are read live from the device on every <see cref="Serialize"/> call.
///
/// headsetConnected=true gates BluetoothMusicMetaData on the CCU side.
///
/// Wire: `[cmd 2B][byte0..3]` - 6 bytes, simple write, no requestId.
///   byte0: (battery% &amp; 0x7F) | (charging &lt;&lt; 7)
///   byte1: (phoneVolume% &amp; 0x7F) | (1 &lt;&lt; 7)        - enablePhoneVolume always true
///   byte2: (mediaVolume% &amp; 0x7F) | (1 &lt;&lt; 7)         - enableMediaVolume always true
///   byte3: (signalLevel &lt;&lt; 4) | (thermalState &lt;&lt; 2) | (headsetConnected &lt;&lt; 1) | enableOnHook
/// </summary>
public class AppPhoneStateResponse : AppOutgoingResponse
{
    private readonly IYmcPhoneStatusProvider _phoneStatus;
    private readonly bool _headsetConnected;

    public AppPhoneStateResponse(IYmcPacketHandler packetHandler, IYmcPhoneStatusProvider phoneStatus, bool headsetConnected = true)
        : base(packetHandler, YmcCommandCode.PhoneStateCombined)
    {
        _phoneStatus = phoneStatus;
        _headsetConnected = headsetConnected;
    }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId)
    {
        var batteryPercent = _phoneStatus.BatteryPercent;
        var charging = _phoneStatus.IsCharging;
        var batteryTempCelsius = _phoneStatus.BatteryTemperatureCelsius;

        var phoneVolumePercent = _phoneStatus.GetVolumePercent(BikeVolumeType.Phone);
        var mediaVolumePercent = _phoneStatus.GetVolumePercent(BikeVolumeType.Media);

        var signalLevel = _phoneStatus.CellSignalLevel;
        var thermalState = ThermalStateFromBatteryTemp(batteryTempCelsius);

        var byte0 = (byte)((Math.Clamp(batteryPercent, 0, 100) & 0x7F) | ((charging ? 1 : 0) << 7));
        var byte1 = (byte)((Math.Clamp(phoneVolumePercent, 0, 100) & 0x7F) | 0x80);
        var byte2 = (byte)((Math.Clamp(mediaVolumePercent, 0, 100) & 0x7F) | 0x80);
        var byte3 = (byte)(
            ((int)signalLevel << 4) |
            ((int)thermalState << 2) |
            ((_headsetConnected ? 1 : 0) << 1) |
            1); // enableOnHook always true

        return Concat(Preamble, new[] { byte0, byte1, byte2, byte3 });
    }

    public static YmcPhoneThermalState ThermalStateFromBatteryTemp(float tempCelsius) => tempCelsius switch
    {
        < 40.0f => YmcPhoneThermalState.Low,
        < 45.0f => YmcPhoneThermalState.Middle,
        _ => YmcPhoneThermalState.High,
    };
}
