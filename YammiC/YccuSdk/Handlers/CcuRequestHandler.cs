using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.Ccu;

namespace CNCO.YccuSdk.Handlers;

public abstract class CcuRequestHandler<T> where T : CcuIncomingPacket
{
    public abstract Task InvokeAsync(
        T packet,
        IYmcPacketHandler packetHandler,
        YmcConnectionContext connectionContext);
}
