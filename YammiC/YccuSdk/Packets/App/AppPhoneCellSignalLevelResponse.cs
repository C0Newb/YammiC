using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk.Packets.App;

public class AppPhoneCellSignalLevelResponse : AppOutgoingResponse
{
    private readonly IYmcPhoneStatusProvider _phoneStatus;

    public AppPhoneCellSignalLevelResponse(IYmcPacketHandler packetHandler, IYmcPhoneStatusProvider phoneStatus)
        : base(packetHandler, YmcCommandCode.PhoneCellSignalLevelResponse)
    {
        _phoneStatus = phoneStatus;
    }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) => Concat(Preamble, new[] { (byte)_phoneStatus.CellSignalLevel });
}
