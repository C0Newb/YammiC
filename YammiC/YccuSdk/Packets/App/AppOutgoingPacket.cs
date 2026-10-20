namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Representing a packet sent by us to the CCU.
/// </summary>
public abstract class AppOutgoingPacket : YmcPacket
{
    protected AppOutgoingPacket(IYmcPacketHandler packetHandler, YmcCommandCode commandCode)
        : base(commandCode, packetHandler)
    {
    }

    /// <summary>Frame preamble (command code)</summary>
    protected byte[] Preamble => ShortBytes(CommandCode);

    /// <summary>Packet transport style.</summary>
    public abstract Transport PacketTransport { get; }

    /// <summary>Packet transport styles.</summary>
    public enum Transport
    {
        Simple,
        Ddt,

        /// <summary>
        /// Written to the Classic-Bluetooth SPP socket instead of a BLE GATT characteristic.
        /// Used only for <see cref="YmcCommandCode.SendVirtualPitBoardGraphic"/> -
        /// the reference SDK sends Virtual Pit Board graphic bytes over the same RFCOMM link used
        /// for the OTA/auth handoff, while every other MPN command stays on BLE.
        /// </summary>
        Spp,
    }

    /// <summary>Serialize the packet to a byte array to transmit via BlueTooth.</summary>
    public abstract byte[] Serialize(byte requestId);

    /// <summary>Sends this packet out.</summary>
    public Task SendAsync(YmcConnectionContext connectionContext) => PacketHandler.SendAsync(connectionContext, this);
}

/// <summary>
/// Our request for action and/or possible response from the CCU.
/// </summary>
public abstract class AppOutgoingRequest : AppOutgoingPacket
{
    protected AppOutgoingRequest(IYmcPacketHandler packetHandler, YmcCommandCode commandCode)
        : base(packetHandler, commandCode)
    {
    }
}

/// <summary>
/// App's response to the CCU's request.
/// </summary>
public abstract class AppOutgoingResponse : AppOutgoingPacket
{
    protected AppOutgoingResponse(IYmcPacketHandler packetHandler, YmcCommandCode commandCode)
        : base(packetHandler, commandCode)
    {
    }
}
