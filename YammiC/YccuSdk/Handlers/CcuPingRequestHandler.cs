using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.App;
using CNCO.YccuSdk.Packets.Ccu;

namespace CNCO.YccuSdk.Handlers;

/// <summary>
/// Handles responding to the CCU's keep-alive (ping) requests.
/// This is critical.
/// </summary>
public class CcuPingRequestHandler : CcuRequestHandler<CcuPingRequest>
{
    public override Task InvokeAsync(
        CcuPingRequest packet,
        IYmcPacketHandler packetHandler,
        YmcConnectionContext connectionContext) =>
        new AppPongResponse(packetHandler).SendAsync(connectionContext);
}
