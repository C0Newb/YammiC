using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.App;
using CNCO.YccuSdk.Packets.Ccu;
using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk.Handlers;

/// <summary>
/// Handles <see cref="CcuVolumeControllableRequest"/> by confirming volume control is supported,
/// then immediately reporting the current media volume so the CCU display initialises correctly.
/// </summary>
public class CcuVolumeControllableRequestHandler : CcuRequestHandler<CcuVolumeControllableRequest>
{
    private readonly IYmcVolumeController _volume;

    public CcuVolumeControllableRequestHandler(IYmcVolumeController volume)
    {
        _volume = volume;
    }

    public override async Task InvokeAsync(
        CcuVolumeControllableRequest packet,
        IYmcPacketHandler packetHandler,
        YmcConnectionContext connectionContext)
    {
        await new AppVolumeControllableResponse(packetHandler, canControl: true).SendAsync(connectionContext).ConfigureAwait(false);

        // Media volume
        await new AppPhoneVolumeLevelRequest(packetHandler, BikeVolumeType.Media, _volume.GetVolumePercent(BikeVolumeType.Media))
            .SendAsync(connectionContext).ConfigureAwait(false);

        // Phone (call) volume
        await new AppPhoneVolumeLevelRequest(packetHandler, BikeVolumeType.Phone, _volume.GetVolumePercent(BikeVolumeType.Phone))
            .SendAsync(connectionContext).ConfigureAwait(false);
    }
}
