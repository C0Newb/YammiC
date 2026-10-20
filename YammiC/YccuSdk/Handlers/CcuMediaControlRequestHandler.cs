using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.Ccu;
using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk.Handlers;

public class CcuMediaControlRequestHandler : CcuRequestHandler<CcuMusicControlRequest>
{
    private readonly Logger _log;
    private readonly IYmcMediaController _mediaController;

    public CcuMediaControlRequestHandler(IYmcMediaController mediaController, Logger logger)
    {
        _mediaController = mediaController;
        _log = logger.Section("MediaControl");
    }

    public override Task InvokeAsync(
        CcuMusicControlRequest packet,
        IYmcPacketHandler packetHandler,
        YmcConnectionContext connectionContext)
    {
        _log.Debug($"action={packet.ControlAction}");
        if (packet.ControlAction == CcuMusicControlRequest.Action.Unknown) return Task.CompletedTask;

        if (_mediaController.TryTransportControl(packet.ControlAction))
        {
            _log.Debug("dispatched via TransportControls");
        }
        else
        {
            _log.Debug("no active controller, falling back to key event");
            _mediaController.DispatchMediaKey(packet.ControlAction);
        }
        return Task.CompletedTask;
    }
}
