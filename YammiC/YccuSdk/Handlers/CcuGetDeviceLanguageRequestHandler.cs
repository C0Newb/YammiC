using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.App;
using CNCO.YccuSdk.Packets.Ccu;

namespace CNCO.YccuSdk.Handlers;

/// <summary>
/// Handles responses to the <see cref="CcuGetDeviceLanguageRequest"/> by responding with <see cref="Language"/>.
/// </summary>
public class CcuGetDeviceLanguageRequestHandler : CcuRequestHandler<CcuGetDeviceLanguageRequest>
{
    public CcuGetDeviceLanguageRequestHandler(YmcLanguage language)
    {
        Language = language;
    }

    /// <summary>
    /// Language sent to CCU upon request.
    /// </summary>
    public YmcLanguage Language { get; private set; }

    public override Task InvokeAsync(
        CcuGetDeviceLanguageRequest packet,
        IYmcPacketHandler packetHandler,
        YmcConnectionContext connectionContext) =>
        new AppGetDeviceLanguageResponse(packetHandler, Language).SendAsync(connectionContext);
}
