namespace CNCO.YccuSdk.Packets;

/// <summary>
/// Command codes. Cast to <see cref="int"/> for the wire value (some are negative: 0xFF01 as a signed short).
/// </summary>
public enum YmcCommandCode
{
    Unknown = -1,

    /// <summary>
    /// App to CCU: app sends this to report an error processing a CCU request
    /// (e.g. unsupported service, timeout). Wire: [cmd 2B][requestId 2B][errorCode 1B][message UTF-8].
    /// CCU to App: CCU sends this to notify the app of a CCU-side error.
    /// </summary>
    CommandError = -255,

    /// <summary>
    /// App to CCU: requests the last command error that occurred on the CCU side. (0xFF02)
    /// </summary>
    LastOccurredCommandErrorRequest = -254,

    // -- App to CCU -----------------------------------------------------------

    /// <summary>
    /// App sends its identifier to the CCU. (0x0002)
    /// </summary>
    IdentifyAppRequest = 0x0002,

    /// <summary>
    /// App registers a new notification ID on the CCU. (0x0010)
    /// </summary>
    AddNotification = 0x0010,

    /// <summary>
    /// App signals that an existing notification's content has changed. (0x0011)
    /// </summary>
    UpdateNotification = 0x0011,

    /// <summary>
    /// App clears a notification from the CCU. (0x0012)
    /// </summary>
    RemoveNotification = 0x0012,

    /// <summary>
    /// CCU notifies the phone that the rider dismissed a notification from the meter. (0x0013)
    /// </summary>
    NotificationRemovedNotification = 0x0013,

    /// <summary>
    /// App sends the weather location name string before WeatherInfo packets. (0x0004)
    /// </summary>
    WeatherLocation = 0x0004,

    /// <summary>
    /// App sends one weather info slot in response to a WeatherRequest. (0x0005)
    /// </summary>
    WeatherInfo = 0x0005,

    /// <summary>
    /// App syncs the CCU clock in response to a VehicleSettingRequest. (0x000A)
    /// </summary>
    GetDatetimeResponse = 0x000A,

    /// <summary>
    /// App replies with the selected language (0x00 = English). (0x000C)
    /// </summary>
    GetDeviceLanguageResponse = 0x000C,

    /// <summary>
    /// App proactively reports battery level and charging state. (0x0115)
    /// </summary>
    PhoneBattery = 0x0115,

    /// <summary>
    /// App sends music metadata to the CCU via DDT session. (0x0116)
    /// </summary>
    BluetoothMusicMetaData = 0x0116,

    /// <summary>
    /// App replies to an AuthenticationRequest from the CCU. (0x0101)
    /// </summary>
    AuthenticationRequest = 0x0101,

    /// <summary>
    /// App replies to a BluetoothHeadsetStateRequest. (0x011C)
    /// </summary>
    BluetoothHeadsetStateResponse = 0x011C,

    /// <summary>
    /// App reports current volume level to the CCU. (0x011E)
    /// </summary>
    PhoneVolumeLevel = 0x011E,

    /// <summary>
    /// App replies to a VolumeControllableRequest. (0x012A)
    /// </summary>
    VolumeControllableResponse = 0x012A,

    /// <summary>
    /// App sends a notification (message or call alert) to the CCU display. (0x012F)
    /// </summary>
    NotificationDataV2 = 0x012F,

    /// <summary>
    /// App asks the CCU for the list of ECU servers it can read data from. (0x0103)
    /// </summary>
    ConnectServerListRequest = 0x0103,

    /// <summary>
    /// CCU to App: the server list (protobuf, field 3 = servers). (0x0104)
    /// </summary>
    ConnectServerListResponse = 0x0104,

    /// <summary>
    /// App asks which data IDs one server supports; payload is the server number (2 bytes). (0x0126)
    /// </summary>
    VehicleInformationSupportIdListRequest = 0x0126,

    /// <summary>
    /// CCU to App: the IDs one server supports (protobuf, field 3 = server, field 4 = ids). (0x0127)
    /// </summary>
    VehicleInformationSupportIdListResponse = 0x0127,

    /// <summary>
    /// CCU to App: a batch of CAN signals (protobuf), sent as a bike-to-phone DDT v3 session. (0x0114)
    /// </summary>
    CanResponse = 0x0114,

    /// <summary>
    /// App asks the CCU to report the listed CAN signals every N milliseconds (protobuf, DDT). (0x013B)
    /// </summary>
    CanIntervalRequest = 0x013B,

    /// <summary>
    /// App replies to an AliveCheckRequest (keepalive pong). (0x0136)
    /// </summary>
    PongResponse = 0x0136,

    /// <summary>
    /// App proactively reports phone state (battery, volume, headset). (0x013C)
    /// </summary>
    PhoneStateCombined = 0x013C,

    /// <summary>
    /// App replies with current cell signal bars. (0x0133)
    /// </summary>
    PhoneCellSignalLevelResponse = 0x0133,

    /// <summary>
    /// App reports the phone's thermal state (overheating level). (0x011A)
    /// </summary>
    PhoneThermalState = 0x011A,

    /// <summary>
    /// App sends caller name + number to the CCU display when a call arrives. (0x0118)
    /// </summary>
    IncomingCallInformation = 0x0118,

    /// <summary>
    /// App declares which notification categories it will forward to the CCU. (0x0128)
    /// </summary>
    NotificationCategory = 0x0128,

    /// <summary>
    /// App notifies the CCU that a phone call started or ended. (0x0134)
    /// Distinct from <see cref="IncomingCallInformation"/>/<see cref="IncomingCallControl"/>, which cover the ringing
    /// alert - this tracks the actual off-hook/on-hook state of the call itself.
    /// </summary>
    CallChangeNotification = 0x0134,

    /// <summary>
    /// App asks the CCU to start a Virtual Pit Board display sequence. (0x0231)
    /// </summary>
    VirtualPitBoardRequest = 0x0231,

    /// <summary>
    /// App tells the CCU the graphic it just requested could not be produced. (0x0234)
    /// </summary>
    VirtualPitBoardGraphicResponseNg = 0x0234,

    /// <summary>
    /// App sends one chunk of Virtual Pit Board image data. (0x0235)
    /// Sent over the Classic-Bluetooth SPP link, not BLE GATT - see <see cref="PhoneThermalState"/>
    /// neighbours for the BLE-only commands. Mirrors the reference SDK's `p`/OTA socket usage.
    /// </summary>
    SendVirtualPitBoardGraphic = 0x0235,

    /// <summary>
    /// App asks the CCU to clear the Virtual Pit Board display. (0x0237)
    /// </summary>
    ClearVirtualPitBoard = 0x0237,

    // -- CCU to App -----------------------------------------------------------

    /// <summary>
    /// CCU requests weather data; may include lat/lng for a specific point. (0x0003)
    /// </summary>
    WeatherRequest = 0x0003,

    /// <summary>
    /// CCU requests vehicle settings (triggers a datetime sync response). (0x0009)
    /// </summary>
    VehicleSettingRequest = 0x0009,

    /// <summary>
    /// CCU requests the app's preferred language. (0x000B)
    /// </summary>
    LanguageRequest = 0x000B,

    /// <summary>
    /// CCU responds to the app's AuthenticationRequest. (0x0102)
    /// </summary>
    AuthenticationResponse = 0x0102,

    /// <summary>
    /// CCU requests whether a Bluetooth headset is connected. (0x011B)
    /// </summary>
    BluetoothHeadsetStateRequest = 0x011B,

    /// <summary>
    /// CCU sends a media playback control action (play/pause/next/prev). (0x0117)
    /// </summary>
    MusicControlRequest = 0x0117,

    /// <summary>
    /// CCU sends a volume control action (up/down/mute). (0x011D)
    /// </summary>
    PhoneVolumeControlRequest = 0x011D,

    /// <summary>
    /// CCU asks whether the app can handle volume control commands. (0x0129)
    /// </summary>
    VolumeControllableRequest = 0x0129,

    /// <summary>
    /// CCU's keepalive ping. (0x0135)
    /// </summary>
    PingRequest = 0x0135,

    /// <summary>
    /// CCU sends an incoming-call action (answer/reject/end). (0x0119)
    /// </summary>
    IncomingCallControl = 0x0119,

    /// <summary>
    /// CCU requests the current cell signal level. (0x0132)
    /// </summary>
    PhoneCellSignalLevelRequest = 0x0132,

    /// <summary>
    /// CCU rejects the pending VirtualPitBoardRequest. (0x0232)
    /// </summary>
    VirtualPitBoardResponseNg = 0x0232,

    /// <summary>
    /// CCU is ready and asks the app to send the queued graphic. (0x0233)
    /// </summary>
    VirtualPitBoardGraphicRequest = 0x0233,

    /// <summary>
    /// CCU acknowledges (or asks a retry/abort for) the last graphic chunk. (0x0236)
    /// </summary>
    SendVirtualPitBoardGraphicResponse = 0x0236,

    /// <summary>
    /// CCU confirms the Virtual Pit Board display was cleared. (0x0238)
    /// </summary>
    ClearVirtualPitBoardResponseOk = 0x0238,

    /// <summary>
    /// CCU reports it could not clear the Virtual Pit Board display. (0x0239)
    /// </summary>
    ClearVirtualPitBoardResponseNg = 0x0239,

    // -- Ride settings (YRC) and lap timing -----------------------------------

    /// <summary>
    /// App asks the CCU for its ride-mode settings (protobuf). (0x0201)
    /// </summary>
    RequestYrcSettingsInfo = 0x0201,

    /// <summary>
    /// CCU to App: the ride-mode settings, over a DDT session. (0x0202)
    /// </summary>
    ResponseYrcSettingsInfo = 0x0202,

    /// <summary>
    /// CCU to App: the rider saved settings on the bike, over a DDT session. (0x0203)
    /// </summary>
    SaveYrcSettingsData = 0x0203,

    /// <summary>
    /// App to CCU: result of handling <see cref="SaveYrcSettingsData"/>. (0x0204)
    /// </summary>
    NotifySaveYrcSettingsDataResult = 0x0204,

    /// <summary>
    /// CCU asks the app for the settings it has saved. (0x0205)
    /// </summary>
    RequestSavedYrcSettingsData = 0x0205,

    /// <summary>
    /// App to CCU: the saved settings. (0x0206)
    /// </summary>
    ResponseSavedYrcSettingsData = 0x0206,

    /// <summary>
    /// CCU asks the app to load settings. (0x0207)
    /// </summary>
    RequestLoadYrcSettings = 0x0207,

    /// <summary>
    /// App to CCU: settings for the CCU to apply, over a DDT session. (0x0208)
    /// </summary>
    LoadYrcSettingsData = 0x0208,

    /// <summary>
    /// CCU to App: result of <see cref="LoadYrcSettingsData"/>. (0x0209)
    /// </summary>
    NotifyLoadYrcSettingsDataResult = 0x0209,

    /// <summary>
    /// App sends a lap / sector time for the CCU to show. (0x0251)
    /// </summary>
    SendLapSectorTime = 0x0251,

    /// <summary>
    /// CCU acknowledges a lap / sector time. (0x0252)
    /// </summary>
    SendLapSectorTimeResponse = 0x0252,
}
