using CCU_Emulator.Protocol;

namespace CCU_Emulator.Emulation;

public sealed partial class CcuSession
{
    public void Press(MusicAction action) => SendCcu(CcuPackets.MusicControl(action), $"MusicControl ({action})");

    public void Press(VolumeAction action) => SendCcu(CcuPackets.VolumeControl(action), $"VolumeControl ({action})");

    /// <summary>Answer and Reject need a ringing call; HangUp needs a ringing or active call.</summary>
    public bool Press(CallAction action)
    {
        var allowed = action == CallAction.HangUp ? State.CallActive || State.CallRinging : State.CallRinging;
        if (!allowed)
        {
            Log.Info("Buttons", $"{action} ignored: no matching call");
            return false;
        }
        SendCcu(CcuPackets.CallControl(action), $"IncomingCallControl ({action})");
        return true;
    }

    /// <summary>The rider dismisses a notification on the dash: tell the phone and drop it locally.</summary>
    public void DismissNotification(int id)
    {
        SendCcu(CcuPackets.NotificationRemoved(id), "NotificationRemoved");
        State.RemoveNotification(id);
    }

    public Task DropConnectionAsync() => _transport.DisconnectAsync();
}
