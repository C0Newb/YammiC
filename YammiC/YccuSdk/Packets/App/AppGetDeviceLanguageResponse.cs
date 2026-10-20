using CNCO.YccuSdk.Models;

namespace CNCO.YccuSdk.Packets.App;

public class AppGetDeviceLanguageResponse : AppOutgoingResponse
{
    public AppGetDeviceLanguageResponse(IYmcPacketHandler packetHandler, YmcLanguage language)
        : base(packetHandler, YmcCommandCode.GetDeviceLanguageResponse)
    {
        Language = language;
    }

    public YmcLanguage Language { get; }

    public override Transport PacketTransport => Transport.Simple;

    public override byte[] Serialize(byte requestId) => Concat(Preamble, new[] { requestId, (byte)Language });
}
