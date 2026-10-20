namespace CNCO.YccuSdk.Packets.Ccu;

public class CcuVolumeControlRequest : CcuIncomingRequest
{
    public CcuVolumeControlRequest(IYmcPacketHandler packetHandler, byte[] data)
        : base(YmcCommandCode.PhoneVolumeControlRequest, packetHandler, data)
    {
        Deserialize(data);
    }

    public enum Action { Unknown, PhoneUp, PhoneDown, MuteMicrophone, MuteSpeaker, MediaUp, MediaDown }

    public Action ControlAction { get; private set; } = Action.Unknown;

    protected override void Deserialize(byte[] data)
    {
        if (data.Length < 3)
        {
            throw new MalformedPacketException("Volume control request is missing the control byte.");
        }
        ControlAction = data[2] switch
        {
            0x01 => Action.PhoneUp,
            0x02 => Action.PhoneDown,
            0x03 => Action.MuteMicrophone,
            0x04 => Action.MuteSpeaker,
            0x05 => Action.MediaUp,
            0x06 => Action.MediaDown,
            _ => throw new MalformedPacketException("Volume control request has an unknown control byte."),
        };
    }
}
