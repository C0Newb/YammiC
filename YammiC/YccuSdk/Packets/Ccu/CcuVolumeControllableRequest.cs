namespace CNCO.YccuSdk.Packets.Ccu;

public class CcuVolumeControllableRequest : CcuIncomingRequest
{
    public CcuVolumeControllableRequest(IYmcPacketHandler packetHandler, byte[] data)
        : base(YmcCommandCode.VolumeControllableRequest, packetHandler, data)
    {
    }

    protected override void Deserialize(byte[] data)
    {
        // Any data?
    }
}
