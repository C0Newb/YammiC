namespace CNCO.YccuSdk.Packets.Ccu;

public class CcuAuthenticationResponse : CcuIncomingResponse
{
    public CcuAuthenticationResponse(IYmcPacketHandler packetHandler, byte[] data)
        : base(YmcCommandCode.AuthenticationResponse, packetHandler, data)
    {
        Deserialize(data);
    }

    public bool Success { get; private set; }

    protected override void Deserialize(byte[] data)
    {
        Success = data.Length >= 3 && data[2] == 0x01;
    }
}
