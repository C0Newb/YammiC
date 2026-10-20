namespace CNCO.YccuSdk.Packets.Ccu;

public class CcuPingRequest : CcuIncomingRequest
{
    public CcuPingRequest(IYmcPacketHandler packetHandler, byte[] data)
        : base(YmcCommandCode.PingRequest, packetHandler, data)
    {
    }

    protected override void Deserialize(byte[] data)
    {
        // Is there any data?
    }
}
