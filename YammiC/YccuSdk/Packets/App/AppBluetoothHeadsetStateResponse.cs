namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Reply to a <see cref="Ccu.CcuBluetoothHeadsetStateRequest"/>.
/// Wire: `[cmd 2B][connected 1B]` - no requestId.
/// </summary>
public class AppBluetoothHeadsetStateResponse : AppOutgoingResponse
{
    public AppBluetoothHeadsetStateResponse(IYmcPacketHandler packetHandler, bool connected)
        : base(packetHandler, YmcCommandCode.BluetoothHeadsetStateResponse)
    {
        Connected = connected;
    }

    public bool Connected { get; }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) =>
        Concat(Preamble, new byte[] { Connected ? (byte)0x01 : (byte)0x00 });
}
