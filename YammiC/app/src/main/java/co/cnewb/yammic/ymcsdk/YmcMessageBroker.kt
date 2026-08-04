package co.cnewb.yammic.ymcsdk

import android.content.Context
import co.cnewb.yammic.ymcsdk.models.YmcMediaMetaData
import co.cnewb.yammic.ymcsdk.packets.YmcPacketHandler
import co.cnewb.yammic.ymcsdk.models.BikeVolumeType
import co.cnewb.yammic.ymcsdk.packets.app.AppAuthenticationRequest
import co.cnewb.yammic.ymcsdk.packets.app.AppCallChangeNotification
import co.cnewb.yammic.ymcsdk.packets.app.AppIdentifyRequest
import co.cnewb.yammic.ymcsdk.packets.app.AppIncomingCallInformationRequest
import co.cnewb.yammic.ymcsdk.packets.app.AppMusicMetaDataResponse
import co.cnewb.yammic.ymcsdk.packets.app.AppNotificationDataV2
import co.cnewb.yammic.ymcsdk.packets.app.AppNotificationLifecycle
import co.cnewb.yammic.ymcsdk.packets.app.AppNotificationCategoryRequest
import co.cnewb.yammic.ymcsdk.packets.app.AppPhoneStateResponse
import co.cnewb.yammic.ymcsdk.packets.app.AppPhoneThermalStateNotification
import co.cnewb.yammic.ymcsdk.packets.app.AppPhoneVolumeLevelRequest
import java.util.Calendar

/**
 * Used to build and send request/response packets.
 */
class YmcMessageBroker(
    val packetHandler: YmcPacketHandler,
    val connectionContext: YmcConnectionContext,
    val context: Context,
) {
    public suspend fun sendAuthenticationRequest(
        ccuId: String,
        passKey: String,
    ) = AppAuthenticationRequest(packetHandler, ccuId, passKey).send(connectionContext)

    public suspend fun sendIdentificationRequest() = AppIdentifyRequest(packetHandler).send(connectionContext)

    public suspend fun sendPhoneState(headsetConnected: Boolean = true) =
        AppPhoneStateResponse(packetHandler, context, headsetConnected).send(connectionContext)

    public suspend fun sendPhoneThermalState() =
        AppPhoneThermalStateNotification(packetHandler, context).send(connectionContext)

    /**
     * Declares which notification categories this app will forward to the CCU. Sent once at
     * connect time; the CCU uses this to decide what UI/space to reserve, so categories left
     * `false` here won't be shown even if [sendNotification] later sends one of that type.
     *
     * @param incomingCall CCU should expect [AppIncomingCallInformationRequest] /
     *   [AppCallChangeNotification] pushes for ringing and active calls (sent from
     *   `PhoneCallMonitor`).
     * @param missedCall CCU should expect a missed-call [AppIncomingCallInformationRequest]
     *   push (`isMissedCall = true`) once a ring goes unanswered.
     * @param social CCU should expect general social/messaging app notifications via
     *   [sendNotification].
     * @param other CCU should expect notifications that don't fit any other declared category.
     */
    public suspend fun sendNotificationCategory(
        incomingCall: Boolean = false,
        missedCall: Boolean = false,
        social: Boolean = false,
        other: Boolean = false,
    ) = AppNotificationCategoryRequest(
        packetHandler,
        other = other,
        incomingCall = incomingCall,
        missedCall = missedCall,
        social = social,
    ).send(connectionContext)

    /** Reports the current phone-side volume for [type] so the CCU display stays in sync. */
    public suspend fun sendPhoneVolumeLevel(
        type: BikeVolumeType,
        volumePercent: Int,
    ) = AppPhoneVolumeLevelRequest(packetHandler, type, volumePercent).send(connectionContext)

    /** Pushes caller info for a ringing call, or a missed-call record once the ring goes unanswered. */
    public suspend fun sendIncomingCallInformation(
        callerName: String,
        phoneNumber: String,
        isMissedCall: Boolean = false,
    ) = AppIncomingCallInformationRequest(packetHandler, callerName, phoneNumber, isMissedCall).send(connectionContext)

    /** Tells the CCU a call just started (off-hook) or ended (on-hook). */
    public suspend fun sendCallChange(started: Boolean) =
        AppCallChangeNotification(packetHandler, started).send(connectionContext)

    public suspend fun sendMusicMetaDataResponse(metadata: YmcMediaMetaData) =
        AppMusicMetaDataResponse(packetHandler, metadata).send(connectionContext)

    public suspend fun addNotification(notificationId: Int) =
        AppNotificationLifecycle(packetHandler, AppNotificationLifecycle.Operation.Add, notificationId)
            .send(connectionContext)

    public suspend fun updateNotification(notificationId: Int) =
        AppNotificationLifecycle(packetHandler, AppNotificationLifecycle.Operation.Update, notificationId)
            .send(connectionContext)

    public suspend fun removeNotification(notificationId: Int) =
        AppNotificationLifecycle(packetHandler, AppNotificationLifecycle.Operation.Remove, notificationId)
            .send(connectionContext)

    public suspend fun sendNotification(
        id: Int,
        category: AppNotificationDataV2.Category = AppNotificationDataV2.Category.Message,
        title: String,
        subTitle: String = "",
        detail: String,
        timestamp: Calendar = Calendar.getInstance(),
    ) = AppNotificationDataV2(packetHandler, id, category, title, subTitle, detail, timestamp)
        .send(connectionContext)
}
