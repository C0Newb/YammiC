package co.cnewb.yammic.ymcsdk.packets

enum class YmcCommandCode(
    val code: Int,
) {
    Unknown(-1),

    /**
     * App → CCU: app sends this to report an error processing a CCU request
     * (e.g. unsupported service, timeout). Wire: [cmd 2B][requestId 2B][errorCode 1B][message UTF-8].
     * CCU → App: CCU sends this to notify the app of a CCU-side error.
     */
    CommandError(-255),

    /** App → CCU: requests the last command error that occurred on the CCU side. (0xFF02) */
    LastOccurredCommandErrorRequest(-254),

    // ── App → CCU ─────────────────────────────────────────────────────────────

    /** App sends its identifier to the CCU. (0x0002) */
    IdentifyAppRequest(0x0002),

    /** App registers a new notification ID on the CCU. (0x0010) */
    AddNotification(0x0010),

    /** App signals that an existing notification's content has changed. (0x0011) */
    UpdateNotification(0x0011),

    /** App clears a notification from the CCU. (0x0012) */
    RemoveNotification(0x0012),

    /** CCU notifies the phone that the rider dismissed a notification from the meter. (0x0013) */
    NotificationRemovedNotification(0x0013),

    /** App sends the weather location name string before WeatherInfo packets. (0x0004) */
    WeatherLocation(0x0004),

    /** App sends one weather info slot in response to a WeatherRequest. (0x0005) */
    WeatherInfo(0x0005),

    /** App syncs the CCU clock in response to a VehicleSettingRequest. (0x000A) */
    GetDatetimeResponse(0x000A),

    /** App replies with the selected language (0x00 = English). (0x000C) */
    GetDeviceLanguageResponse(0x000C),

    /** App proactively reports battery level and charging state. (0x0115) */
    PhoneBattery(0x0115),

    /** App sends music metadata to the CCU via DDT session. (0x0116) */
    BluetoothMusicMetaData(0x0116),

    /** App replies to an AuthenticationRequest from the CCU. (0x0101) */
    AuthenticationRequest(0x0101),

    /** App replies to a BluetoothHeadsetStateRequest. (0x011C) */
    BluetoothHeadsetStateResponse(0x011C),

    /** App reports current volume level to the CCU. (0x011E) */
    PhoneVolumeLevel(0x011E),

    /** App replies to a VolumeControllableRequest. (0x012A) */
    VolumeControllableResponse(0x012A),

    /** App sends a notification (message or call alert) to the CCU display. (0x012F) */
    NotificationDataV2(0x012F),

    /** App replies to an AliveCheckRequest (keepalive pong). (0x0136) */
    PongResponse(0x0136),

    /** App proactively reports phone state (battery, volume, headset). (0x013C) */
    PhoneStateCombined(0x013C),

    /** App replies with current cell signal bars. (0x0133) */
    PhoneCellSignalLevelResponse(0x0133),

    /** App reports the phone's thermal state (overheating level). (0x011A) */
    PhoneThermalState(0x011A),

    /** App sends caller name + number to the CCU display when a call arrives. (0x0118) */
    IncomingCallInformation(0x0118),

    /** App declares which notification categories it will forward to the CCU. (0x0128) */
    NotificationCategory(0x0128),

    /**
     * App notifies the CCU that a phone call started or ended. (0x0134)
     * Distinct from [IncomingCallInformation]/[IncomingCallControl], which cover the ringing
     * alert — this tracks the actual off-hook/on-hook state of the call itself.
     */
    CallChangeNotification(0x0134),

    /** App asks the CCU to start a Virtual Pit Board display sequence. (0x0231) */
    VirtualPitBoardRequest(0x0231),

    /** App tells the CCU the graphic it just requested could not be produced. (0x0234) */
    VirtualPitBoardGraphicResponseNg(0x0234),

    /**
     * App sends one chunk of Virtual Pit Board image data. (0x0235)
     * Sent over the Classic-Bluetooth SPP link, not BLE GATT — see [YmcCommandCode.PhoneThermalState]
     * neighbours for the BLE-only commands. Mirrors the reference SDK's `p`/OTA socket usage.
     */
    SendVirtualPitBoardGraphic(0x0235),

    /** App asks the CCU to clear the Virtual Pit Board display. (0x0237) */
    ClearVirtualPitBoard(0x0237),

    // ── CCU → App ─────────────────────────────────────────────────────────────

    /** CCU requests weather data; may include lat/lng for a specific point. (0x0003) */
    WeatherRequest(0x0003),

    /** CCU requests vehicle settings (triggers a datetime sync response). (0x0009) */
    VehicleSettingRequest(0x0009),

    /** CCU requests the app's preferred language. (0x000B) */
    LanguageRequest(0x000B),

    /** CCU responds to the app's AuthenticationRequest. (0x0102) */
    AuthenticationResponse(0x0102),

    /** CCU requests whether a Bluetooth headset is connected. (0x011B) */
    BluetoothHeadsetStateRequest(0x011B),

    /** CCU sends a media playback control action (play/pause/next/prev). (0x0117) */
    MusicControlRequest(0x0117),

    /** CCU sends a volume control action (up/down/mute). (0x011D) */
    PhoneVolumeControlRequest(0x011D),

    /** CCU asks whether the app can handle volume control commands. (0x0129) */
    VolumeControllableRequest(0x0129),

    /** CCU's keepalive ping. (0x0135) */
    PingRequest(0x0135),

    /** CCU sends an incoming-call action (answer/reject/end). (0x0119) */
    IncomingCallControl(0x0119),

    /** CCU requests the current cell signal level. (0x0132) */
    PhoneCellSignalLevelRequest(0x0132),

    /** CCU rejects the pending VirtualPitBoardRequest. (0x0232) */
    VirtualPitBoardResponseNg(0x0232),

    /** CCU is ready and asks the app to send the queued graphic. (0x0233) */
    VirtualPitBoardGraphicRequest(0x0233),

    /** CCU acknowledges (or asks a retry/abort for) the last graphic chunk. (0x0236) */
    SendVirtualPitBoardGraphicResponse(0x0236),

    /** CCU confirms the Virtual Pit Board display was cleared. (0x0238) */
    ClearVirtualPitBoardResponseOk(0x0238),

    /** CCU reports it could not clear the Virtual Pit Board display. (0x0239) */
    ClearVirtualPitBoardResponseNg(0x0239),
}
