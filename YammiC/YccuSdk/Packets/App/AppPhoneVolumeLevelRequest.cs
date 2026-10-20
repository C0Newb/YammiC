using CNCO.YccuSdk.Models;

namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Reports the current volume level to the CCU so its display stays in sync.
/// Wire: `[cmd 2B][volume 1B][type 1B]` - no requestId.
/// </summary>
public class AppPhoneVolumeLevelRequest : AppOutgoingRequest
{
    public AppPhoneVolumeLevelRequest(IYmcPacketHandler packetHandler, BikeVolumeType type, int volumePercent)
        : base(packetHandler, YmcCommandCode.PhoneVolumeLevel)
    {
        Type = type;
        VolumePercent = volumePercent;
    }

    public BikeVolumeType Type { get; }
    public int VolumePercent { get; }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) =>
        Concat(Preamble, new[] { (byte)Math.Clamp(VolumePercent, 0, 100), (byte)Type });
}
