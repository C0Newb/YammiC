namespace CNCO.YccuSdk.Packets.Ccu;

public class CcuBluetoothHeadsetStateRequest : CcuIncomingRequest
{
    public CcuBluetoothHeadsetStateRequest(IYmcPacketHandler packetHandler, byte[] data)
        : base(YmcCommandCode.BluetoothHeadsetStateRequest, packetHandler, data)
    {
    }

    protected override void Deserialize(byte[] data)
    {
        // Data?
    }
}
