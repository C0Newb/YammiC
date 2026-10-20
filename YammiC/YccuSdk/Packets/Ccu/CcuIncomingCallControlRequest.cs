namespace CNCO.YccuSdk.Packets.Ccu;

public class CcuIncomingCallControlRequest : CcuIncomingRequest
{
    public CcuIncomingCallControlRequest(IYmcPacketHandler packetHandler, byte[] data)
        : base(YmcCommandCode.IncomingCallControl, packetHandler, data)
    {
        Deserialize(data);
    }

    public enum Action { Answer, Reject, OnHook, Unknown }

    public Action ControlAction { get; private set; } = Action.Unknown;

    protected override void Deserialize(byte[] data)
    {
        if (data.Length < 3)
        {
            throw new MalformedPacketException("IncomingCallControl request is missing the action byte.");
        }
        ControlAction = data[2] switch
        {
            0x00 => Action.Reject,
            0x01 => Action.Answer,
            0x02 => Action.OnHook,
            _ => Action.Unknown,
        };
    }
}
