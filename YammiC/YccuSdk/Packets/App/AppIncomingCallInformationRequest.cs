using System.Text;

namespace CNCO.YccuSdk.Packets.App;

/// <summary>
/// Wire: `[cmd 2B][nameLen 1B][name][phoneLen 1B][phone][isMissedCall 1B]`.
/// </summary>
public class AppIncomingCallInformationRequest : AppOutgoingResponse
{
    public AppIncomingCallInformationRequest(
        IYmcPacketHandler packetHandler,
        string callerName,
        string phoneNumber,
        bool isMissedCall)
        : base(packetHandler, YmcCommandCode.IncomingCallInformation)
    {
        CallerName = callerName;
        PhoneNumber = phoneNumber;
        IsMissedCall = isMissedCall;
    }

    public string CallerName { get; }
    public string PhoneNumber { get; }
    public bool IsMissedCall { get; }

    public override Transport PacketTransport => Transport.Ddt;

    public override byte[] Serialize(byte requestId)
    {
        var nameBytes = Encoding.UTF8.GetBytes(Take(CallerName, 64));
        var phoneBytes = Encoding.UTF8.GetBytes(Take(PhoneNumber, 12));
        var payload = Concat(
            new[] { (byte)nameBytes.Length }, nameBytes,
            new[] { (byte)phoneBytes.Length }, phoneBytes,
            new byte[] { IsMissedCall ? (byte)1 : (byte)0 });
        return Concat(Preamble, payload);
    }
}
