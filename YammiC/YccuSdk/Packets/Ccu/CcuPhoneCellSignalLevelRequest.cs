namespace CNCO.YccuSdk.Packets.Ccu;

public class CcuPhoneCellSignalLevelRequest : CcuIncomingRequest
{
    public CcuPhoneCellSignalLevelRequest(IYmcPacketHandler packetHandler, byte[] data)
        : base(YmcCommandCode.PhoneCellSignalLevelRequest, packetHandler, data)
    {
    }

    protected override void Deserialize(byte[] data)
    {
    }
}
