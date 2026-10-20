namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Reply to a <see cref="Ccu.CcuVolumeControllableRequest"/>.
/// Wire: `[cmd 2B][canControl 1B]` - no requestId.
/// </summary>
public class AppVolumeControllableResponse : AppOutgoingResponse
{
    public AppVolumeControllableResponse(IYmcPacketHandler packetHandler, bool canControl)
        : base(packetHandler, YmcCommandCode.VolumeControllableResponse)
    {
        CanControl = canControl;
    }

    public bool CanControl { get; }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) =>
        Concat(Preamble, new byte[] { CanControl ? (byte)0x01 : (byte)0x00 });
}
