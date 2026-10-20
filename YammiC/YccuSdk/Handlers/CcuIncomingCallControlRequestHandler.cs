using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.Ccu;
using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk.Handlers;

public class CcuIncomingCallControlRequestHandler : CcuRequestHandler<CcuIncomingCallControlRequest>
{
    private readonly IYmcCallController _callController;

    public CcuIncomingCallControlRequestHandler(IYmcCallController callController)
    {
        _callController = callController;
    }

    public override Task InvokeAsync(
        CcuIncomingCallControlRequest packet,
        IYmcPacketHandler packetHandler,
        YmcConnectionContext connectionContext)
    {
        switch (packet.ControlAction)
        {
            case CcuIncomingCallControlRequest.Action.Answer:
                AcceptRingingCall();
                break;
            case CcuIncomingCallControlRequest.Action.OnHook:
            case CcuIncomingCallControlRequest.Action.Reject:
                EndCall();
                break;
            case CcuIncomingCallControlRequest.Action.Unknown:
                break;
        }
        return Task.CompletedTask;
    }

    private void AcceptRingingCall()
    {
        if (!_callController.CanControlCalls)
        {
            // Can't!
            return;
        }
        _callController.AcceptRingingCall();
    }

    private void EndCall()
    {
        if (!_callController.CanControlCalls)
        {
            // Can't!
            return;
        }
        _callController.EndCall();
    }
}
