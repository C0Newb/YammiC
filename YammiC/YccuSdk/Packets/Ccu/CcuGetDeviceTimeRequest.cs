namespace CNCO.YccuSdk.Packets.Ccu;

/// <summary>
/// CCU requests current date/time (repeats every ~5s until answered).
/// </summary>
public class CcuGetDeviceTimeRequest : CcuIncomingRequest
{
    public CcuGetDeviceTimeRequest(IYmcPacketHandler packetHandler, byte[] data)
        : base(YmcCommandCode.VehicleSettingRequest, packetHandler, data)
    {
    }

    protected override void Deserialize(byte[] data)
    {
        // Is there any data?
    }
}
