using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.App;
using CNCO.YccuSdk.Packets.Ccu;
using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk.Handlers;

public class CcuPhoneCellSignalLevelRequestHandler : CcuRequestHandler<CcuPhoneCellSignalLevelRequest>
{
    private readonly IYmcPhoneStatusProvider _phoneStatus;

    public CcuPhoneCellSignalLevelRequestHandler(IYmcPhoneStatusProvider phoneStatus)
    {
        _phoneStatus = phoneStatus;
    }

    public override Task InvokeAsync(
        CcuPhoneCellSignalLevelRequest packet,
        IYmcPacketHandler packetHandler,
        YmcConnectionContext connectionContext) =>
        new AppPhoneCellSignalLevelResponse(packetHandler, _phoneStatus).SendAsync(connectionContext);
}
