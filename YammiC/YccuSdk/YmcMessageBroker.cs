using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Packets;
using CNCO.YccuSdk.Packets.App;
using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk;

/// <summary>
/// Used to build and send request/response packets.
/// </summary>
public class YmcMessageBroker
{
    private readonly IYmcPhoneStatusProvider _phoneStatus;

    public YmcMessageBroker(IYmcPacketHandler packetHandler, YmcConnectionContext connectionContext, IYmcPhoneStatusProvider phoneStatus)
    {
        PacketHandler = packetHandler;
        ConnectionContext = connectionContext;
        _phoneStatus = phoneStatus;
    }

    public IYmcPacketHandler PacketHandler { get; }

    public YmcConnectionContext ConnectionContext { get; }

    public Task SendAuthenticationRequestAsync(string ccuId, string passKey) =>
        new AppAuthenticationRequest(PacketHandler, ccuId, passKey).SendAsync(ConnectionContext);

    public Task SendIdentificationRequestAsync() => new AppIdentifyRequest(PacketHandler).SendAsync(ConnectionContext);

    public Task SendPhoneStateAsync(bool headsetConnected = true) =>
        new AppPhoneStateResponse(PacketHandler, _phoneStatus, headsetConnected).SendAsync(ConnectionContext);

    public Task SendPhoneThermalStateAsync() =>
        new AppPhoneThermalStateNotification(PacketHandler, _phoneStatus).SendAsync(ConnectionContext);

    /// <summary>
    /// Declares which notification categories this app will forward to the CCU. Sent once at
    /// connect time; the CCU uses this to decide what UI/space to reserve, so categories left
    /// <c>false</c> here won't be shown even if <see cref="SendNotificationAsync"/> later sends one of that type.
    /// </summary>
    /// <param name="incomingCall">
    /// CCU should expect <see cref="AppIncomingCallInformationRequest"/> / <see cref="AppCallChangeNotification"/>
    /// pushes for ringing and active calls.
    /// </param>
    /// <param name="missedCall">
    /// CCU should expect a missed-call <see cref="AppIncomingCallInformationRequest"/> push
    /// (<c>isMissedCall = true</c>) once a ring goes unanswered.
    /// </param>
    /// <param name="social">CCU should expect general social/messaging app notifications via <see cref="SendNotificationAsync"/>.</param>
    /// <param name="other">CCU should expect notifications that don't fit any other declared category.</param>
    public Task SendNotificationCategoryAsync(
        bool incomingCall = false,
        bool missedCall = false,
        bool social = false,
        bool other = false) =>
        new AppNotificationCategoryRequest(
            PacketHandler,
            other: other,
            incomingCall: incomingCall,
            missedCall: missedCall,
            social: social).SendAsync(ConnectionContext);

    /// <summary>
    /// Reports the current phone-side volume for <paramref name="type"/> so the CCU display stays in sync.
    /// </summary>
    public Task SendPhoneVolumeLevelAsync(BikeVolumeType type, int volumePercent) =>
        new AppPhoneVolumeLevelRequest(PacketHandler, type, volumePercent).SendAsync(ConnectionContext);

    /// <summary>
    /// Pushes caller info for a ringing call, or a missed-call record once the ring goes unanswered.
    /// </summary>
    public Task SendIncomingCallInformationAsync(string callerName, string phoneNumber, bool isMissedCall = false) =>
        new AppIncomingCallInformationRequest(PacketHandler, callerName, phoneNumber, isMissedCall).SendAsync(ConnectionContext);

    /// <summary>
    /// Tells the CCU a call just started (off-hook) or ended (on-hook).
    /// </summary>
    public Task SendCallChangeAsync(bool started) =>
        new AppCallChangeNotification(PacketHandler, started).SendAsync(ConnectionContext);

    public Task SendMusicMetaDataResponseAsync(YmcMediaMetaData metadata) =>
        new AppMusicMetaDataResponse(PacketHandler, metadata).SendAsync(ConnectionContext);

    public Task AddNotificationAsync(int notificationId) =>
        new AppNotificationLifecycle(PacketHandler, AppNotificationLifecycle.Operation.Add, notificationId)
            .SendAsync(ConnectionContext);

    public Task UpdateNotificationAsync(int notificationId) =>
        new AppNotificationLifecycle(PacketHandler, AppNotificationLifecycle.Operation.Update, notificationId)
            .SendAsync(ConnectionContext);

    public Task RemoveNotificationAsync(int notificationId) =>
        new AppNotificationLifecycle(PacketHandler, AppNotificationLifecycle.Operation.Remove, notificationId)
            .SendAsync(ConnectionContext);

    public Task SendNotificationAsync(
        int id,
        string title,
        string detail,
        AppNotificationDataV2.Category category = AppNotificationDataV2.Category.Message,
        string subTitle = "",
        DateTime? timestamp = null) =>
        new AppNotificationDataV2(PacketHandler, id, category, title, subTitle, detail, timestamp)
            .SendAsync(ConnectionContext);
}
