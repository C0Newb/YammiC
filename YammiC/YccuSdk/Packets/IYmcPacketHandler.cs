using CNCO.YccuSdk.Packets.App;

namespace CNCO.YccuSdk.Packets;

public interface IYmcPacketHandler
{
    /// <summary>
    /// Sends a packet to the CCU.
    /// </summary>
    Task SendAsync(YmcConnectionContext connectionContext, AppOutgoingPacket request);
}
