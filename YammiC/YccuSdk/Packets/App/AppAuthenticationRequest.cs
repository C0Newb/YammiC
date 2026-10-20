namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Authenticates the app with the CCU.
/// </summary>
public class AppAuthenticationRequest : AppOutgoingRequest
{
    public AppAuthenticationRequest(IYmcPacketHandler packetHandler, string ccuId, string passKey)
        : base(packetHandler, YmcCommandCode.AuthenticationRequest)
    {
        CcuId = ccuId;
        PassKey = passKey;
    }

    public string CcuId { get; }
    public string PassKey { get; }

    public override Transport PacketTransport => Transport.Ddt;

    /*
     * Format: `[cmd 2B][ccuId 14B][passKey 6B]`.
     * The DDT length-prefix format (frameRequest) is NOT used. YCCU expects exactly 22 bytes.
     */
    public override byte[] Serialize(byte requestId) => Concat(Preamble, StringBytes(CcuId), StringBytes(PassKey));
}
