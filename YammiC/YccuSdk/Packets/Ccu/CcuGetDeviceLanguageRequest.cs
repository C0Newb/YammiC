namespace CNCO.YccuSdk.Packets.Ccu;

public class CcuGetDeviceLanguageRequest : CcuIncomingRequest
{
    public CcuGetDeviceLanguageRequest(IYmcPacketHandler packetHandler, byte[] data)
        : base(YmcCommandCode.LanguageRequest, packetHandler, data)
    {
    }

    protected override void Deserialize(byte[] data)
    {
        // Is there any data?
    }
}
