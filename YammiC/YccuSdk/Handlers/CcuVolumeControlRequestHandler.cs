using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.App;
using CNCO.YccuSdk.Packets.Ccu;
using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk.Handlers;

public class CcuVolumeControlRequestHandler : CcuRequestHandler<CcuVolumeControlRequest>
{
    private readonly Logger _log;
    private readonly IYmcVolumeController _volume;

    public CcuVolumeControlRequestHandler(IYmcVolumeController volume, Logger logger)
    {
        _volume = volume;
        _log = logger.Section("VolumeControl");
    }

    public override async Task InvokeAsync(
        CcuVolumeControlRequest packet,
        IYmcPacketHandler packetHandler,
        YmcConnectionContext connectionContext
    )
    {
        _log.Debug($"action={packet.ControlAction}");
        BikeVolumeType? affectedType;
        switch (packet.ControlAction)
        {
            case CcuVolumeControlRequest.Action.MediaUp:
                _volume.Adjust(BikeVolumeType.Media, raise: true);
                affectedType = BikeVolumeType.Media;
                break;

            case CcuVolumeControlRequest.Action.MediaDown:
                _volume.Adjust(BikeVolumeType.Media, raise: false);
                affectedType = BikeVolumeType.Media;
                break;

            case CcuVolumeControlRequest.Action.PhoneUp:
                _volume.Adjust(BikeVolumeType.Phone, raise: true);
                affectedType = BikeVolumeType.Phone;
                break;

            case CcuVolumeControlRequest.Action.PhoneDown:
                _volume.Adjust(BikeVolumeType.Phone, raise: false);
                affectedType = BikeVolumeType.Phone;
                break;

            // MuteMicrophone, MuteSpeaker, Unknown: nothing to do (yet).
            default:
                affectedType = null;
                break;
        }
        if (affectedType == null)
        {
            return;
        }
        var type = affectedType.Value;

        var pct = _volume.GetVolumePercent(type);
        _log.Debug($"reporting {type} volume={pct}%");
        await new AppPhoneVolumeLevelRequest(packetHandler, type, pct).SendAsync(connectionContext);
    }
}
