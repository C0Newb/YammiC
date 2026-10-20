namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Tells the CCU whether a phone call is currently active (off-hook) or has ended (on-hook).
/// Wire: `[cmd 2B][state 1B]` where state is 1=Started, 2=Ended.
/// </summary>
public class AppCallChangeNotification : AppOutgoingResponse
{
    private readonly bool _started;

    public AppCallChangeNotification(IYmcPacketHandler packetHandler, bool started)
        : base(packetHandler, YmcCommandCode.CallChangeNotification)
    {
        _started = started;
    }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) => Concat(Preamble, new byte[] { _started ? (byte)0x01 : (byte)0x02 });
}
