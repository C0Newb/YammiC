namespace CNCO.YccuSdk.Packets.Ccu;

public class CcuMusicControlRequest : CcuIncomingRequest
{
    public CcuMusicControlRequest(IYmcPacketHandler packetHandler, byte[] data)
        : base(YmcCommandCode.MusicControlRequest, packetHandler, data)
    {
        Deserialize(data);
    }

    public enum Action { Unknown, NextTrack, PreviousTrack, Play, Pause }

    public Action ControlAction { get; private set; } = Action.Unknown;

    protected override void Deserialize(byte[] data)
    {
        if (data.Length < 3)
        {
            throw new MalformedPacketException("Music control request is missing the control byte.");
        }
        ControlAction = data[2] switch
        {
            0x01 => Action.NextTrack,
            0x02 => Action.PreviousTrack,
            0x03 => Action.Play,
            0x04 => Action.Pause,
            _ => throw new MalformedPacketException("Music control request has an unknown control byte."),
        };
    }
}
