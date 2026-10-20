using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.App;
using CNCO.YccuSdk.Packets.Ccu;

namespace CNCO.YccuSdk.Handlers;

/// <summary>
/// Handles responses to the <see cref="CcuGetDeviceTimeRequest"/> by responding with the system time.
/// </summary>
public class CcuGetDeviceTimeRequestHandler : CcuRequestHandler<CcuGetDeviceTimeRequest>
{
    public override Task InvokeAsync(
        CcuGetDeviceTimeRequest packet,
        IYmcPacketHandler packetHandler,
        YmcConnectionContext connectionContext) =>
        new AppGetDeviceTimeResponse(packetHandler).SendAsync(connectionContext);
}
