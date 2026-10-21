namespace CNCO.YccuSdk.Packets.Ccu;

/// <summary>
/// Representing a packet sent by the CCU received by us.
/// </summary>
public abstract class CcuIncomingPacket : YmcPacket
{
    protected CcuIncomingPacket(
        YmcCommandCode commandCode,
        IYmcPacketHandler packetHandler,
        byte[] data
    )
        : base(commandCode, packetHandler) { }

    protected abstract void Deserialize(byte[] data);
}

/// <summary>
/// CCU's request for action and/or possible response.
/// </summary>
public abstract class CcuIncomingRequest : CcuIncomingPacket
{
    protected CcuIncomingRequest(
        YmcCommandCode commandCode,
        IYmcPacketHandler packetHandler,
        byte[] data
    )
        : base(commandCode, packetHandler, data) { }
}

/// <summary>
/// CCU's response to our request.
/// </summary>
public abstract class CcuIncomingResponse : CcuIncomingPacket
{
    protected CcuIncomingResponse(
        YmcCommandCode commandCode,
        IYmcPacketHandler packetHandler,
        byte[] data
    )
        : base(commandCode, packetHandler, data) { }
}
