using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk.Packets.App;

public class AppPhoneThermalStateNotification : AppOutgoingResponse
{
    private readonly IYmcPhoneStatusProvider _phoneStatus;

    public AppPhoneThermalStateNotification(IYmcPacketHandler packetHandler, IYmcPhoneStatusProvider phoneStatus)
        : base(packetHandler, YmcCommandCode.PhoneThermalState)
    {
        _phoneStatus = phoneStatus;
    }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId)
    {
        var thermalState = AppPhoneStateResponse.ThermalStateFromBatteryTemp(_phoneStatus.BatteryTemperatureCelsius);
        return Concat(Preamble, new[] { (byte)thermalState });
    }
}
