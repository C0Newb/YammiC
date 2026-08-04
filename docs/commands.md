# Command Reference

Command codes are `short` values. All values are decimal; hex equivalents shown for commands likely to be needed.

**Direction key:** `P→C` = phone sends, `C→P` = CCU sends, `both` = either side

---

## Core Commands (0x0001–0x0016)

| Code | Hex | Name | Dir | Notes |
|------|-----|------|-----|-------|
| 1 | 0x0001 | LibVersion | C→P | CCU SDK version |
| 2 | 0x0002 | ConnectAppId | P→C | App identifies itself to CCU |
| 3 | 0x0003 | WeatherRequest | C→P | CCU requests weather data |
| 4 | 0x0004 | WeatherLocation | P→C | Send location to CCU |
| 5 | 0x0005 | WeatherInfo | P→C | Send weather data to CCU |
| 6 | 0x0006 | VehicleInformationSubscribeInterval | P→C | Set telemetry rate |
| 7 | 0x0007 | VehicleInformation | C→P | Speed, ignition state, etc. |
| 8 | 0x0008 | DatetimeChangeNotification | P→C | Push time to CCU |
| 9 | 0x0009 | VehicleSettingRequest | C→P | CCU requests settings |
| 10 | 0x000A | VehicleSettingDatetime | P→C | Set CCU clock |
| 11 | 0x000B | LanguageTypeRequest | C→P | CCU asks for language |
| 12 | 0x000C | LanguageTypeResponse | P→C | Reply with language |
| 13 | 0x000D | IconArrangementListRequest | C→P | CCU requests icon config |
| 14 | 0x000E | IconArrangementListResponse | P→C | Send icon layout |
| 15 | 0x000F | DeviceName | P→C | Set CCU display name |
| 16 | 0x0010 | AddNotification | P→C | Push phone notification |
| 17 | 0x0011 | UpdateNotification | P→C | Update existing notification |
| 18 | 0x0012 | RemoveNotification | P→C | Remove notification |
| 19 | 0x0013 | NotificationRemovedNotification | C→P | CCU confirms removal |
| 20 | 0x0014 | NotificationData | P→C | Full notification payload |
| 21 | 0x0015 | Authentication | C→P | **Legacy** — use V2 instead |
| 22 | 0x0016 | WifiHost | C→P | CCU WiFi credentials |

---

## Extended Commands (0x0100+)

| Code | Hex | Name | Dir | Notes |
|------|-----|------|-----|-------|
| 257 | 0x0101 | **AuthenticationV2Request** | P→C | Send CCUID + passkey |
| 258 | 0x0102 | **AuthenticationV2Response** | C→P | Auth success/failure |
| 259 | 0x0103 | ConnectServerListRequest | P→C | Request server list |
| 260 | 0x0104 | ConnectServerListResponse | C→P | Server list data |
| 261 | 0x0105 | MalfunctionRequest | P→C | Request DTC codes |
| 262 | 0x0106 | MalfunctionResponse | C→P | DTC fault codes |
| 265 | 0x0109 | MalfunctionIntervalRequest | P→C | Set DTC poll rate |
| 266 | 0x010A | VehicleIdentificationRequest | P→C | Request VIN/model info |
| 267 | 0x010B | VehicleIdentificationResponse | C→P | VIN/model info |
| 268 | 0x010C | MarketDataRequest | P→C | Request market data |
| 269 | 0x010D | MarketDataResponse | C→P | Market data |
| 270 | 0x010E | VehicleInformationRequest | P→C | Request vehicle info |
| 271 | 0x010F | VehicleInformationResponse | C→P | Vehicle info (proto) |
| 272 | 0x0110 | VehicleInformationIntervalRequest | P→C | Set vehicle info rate |
| 273 | 0x0111 | FfdRequest | P→C | Request freeze frame data |
| 274 | 0x0112 | FfdResponse | C→P | Freeze frame data (proto) |
| 275 | 0x0113 | CanRequest | P→C | Request CAN bus data |
| 276 | 0x0114 | CanResponse | C→P | CAN bus data (proto) |
| 277 | 0x0115 | PhoneBattery | P→C | Push phone battery % |
| 278 | 0x0116 | BluetoothMusicMetaData | P→C | Now-playing metadata |
| 279 | 0x0117 | BluetoothMusicControl | C→P | Play/pause/skip |
| 280 | 0x0118 | IncomingCallInformation | P→C | Incoming call info |
| 281 | 0x0119 | IncomingCallControl | C→P | Accept/reject |
| 282 | 0x011A | PhoneThermalState | P→C | Phone temperature state |
| 283 | 0x011B | BluetoothHeadsetStateRequest | C→P | Headset status request |
| 284 | 0x011C | BluetoothHeadsetStateResponse | P→C | Headset status |
| 285 | 0x011D | PhoneVolumeControl | C→P | CCU sets phone volume |
| 286 | 0x011E | PhoneVolumeLevel | P→C | Current phone volume |
| 287 | 0x011F | CumulativeInjectionQuantityRequest | P→C | Request fuel injection data |
| 288 | 0x0120 | CumulativeInjectionQuantity | C→P | Fuel injection data |
| 289 | 0x0121 | OtaLinkcardInformation | C→P | OTA update available |
| 290 | 0x0122 | OtaUpdateInformation | P→C | OTA update info |
| 291 | 0x0123 | OtaUpdateDataRequest | C→P | CCU requests data chunk |
| 292 | 0x0124 | OtaUpdateDataResponse | P→C | Send data chunk |
| 293 | 0x0125 | OtaUpdateCompleted | C→P | OTA done |
| 294 | 0x0126 | VehicleInformationSupportIdListRequest | P→C | What data IDs are supported |
| 295 | 0x0127 | VehicleInformationSupportIdListResponse | C→P | Supported ID list |
| 296 | 0x0128 | NotificationCategory | P→C | Notification category info |
| 297 | 0x0129 | PhoneVolumeControllableRequest | C→P | Can CCU control volume? |
| 298 | 0x012A | PhoneVolumeControllableResponse | P→C | Yes/no |
| 299 | 0x012B | OtaAbort | C→P | OTA aborted |
| 300 | 0x012C | CreateOtaConnectionRequest | C→P | Start OTA SPP connection |
| 301 | 0x012D | OtaStopRequest | C→P | Stop OTA |
| 302 | 0x012E | OtaStopResponse | P→C | OTA stop ACK |
| 303 | 0x012F | NotificationDataV2 | P→C | Notification (protobuf) |
| 304 | 0x0130 | **CcuidRequest** | P→C | Request CCUID from CCU |
| 305 | 0x0131 | **CcuidResponse** | C→P | Returns CCUID bytes |
| 306 | 0x0132 | PhoneCellSignalLevelRequest | C→P | Request signal level |
| 307 | 0x0133 | PhoneCellSignalLevelResponse | P→C | Signal level |
| 308 | 0x0134 | CallChangeNotification | P→C | Call state changed |
| 309 | 0x0135 | **AliveCheckRequest** | P→C | Keepalive ping |
| 310 | 0x0136 | **AliveCheckResponse** | C→P | Pong |
| 311 | 0x0137 | **IgnitionStateRequest** | P→C | Request ignition state |
| 312 | 0x0138 | **IgnitionStateResponse** | C→P | Ignition on/off |
| 313 | 0x0139 | BleConnectInformationRequest | P→C | BLE info request |
| 314 | 0x013A | BleConnectInformationResponse | C→P | BLE info |
| 315 | 0x013B | CanIntervalRequest | P→C | Set CAN poll rate |
| 316 | 0x013C | PhoneStateCombined | P→C | Aggregated phone state |
| 317 | 0x013D | EnterFactoryModeRequest | C→P | Factory/test mode |

---

## YRC Settings Commands (0x0200+)

YRC = Yamaha Ride Control (traction control, power modes, etc.)

| Code | Hex | Name |
|------|-----|------|
| 513 | 0x0201 | Request_Yrc_Settings_Info |
| 514 | 0x0202 | Response_Yrc_Settings_Info |
| 515 | 0x0203 | Save_Yrc_Settings_Data |
| 516 | 0x0204 | Notify_Save_Yrc_Settings_Data_Result |
| 517 | 0x0205 | Request_Saved_Yrc_Settings_Data |
| 518 | 0x0206 | Response_Saved_Yrc_Settings_Data |
| 519 | 0x0207 | Request_Load_Yrc_Settings |
| 520 | 0x0208 | Load_Yrc_Settings_Data |
| 521 | 0x0209 | Notify_Load_Yrc_Settings_Data_Result |

---

## Display / Push Commands (0x0210–0x025x)

Photo frame (0x0211–0x0219), push notifications (0x0221–0x0229), virtual pit board (0x0231–0x0239), lap timing (0x0251–0x0252).

---

## Commands for Minimum Viable Connection

For basic vehicle data, only these are needed:

1. **Send:** `ConnectAppId` (2) — identify the app
2. **Send:** `LanguageTypeResponse` (12) — reply when CCU asks
3. **Send:** `VehicleInformationSubscribeInterval` (6) — start telemetry stream
4. **Receive:** `VehicleInformation` (7) — speed, ignition state
5. **Send/Receive:** `AliveCheckRequest/Response` (309/310) — keepalive

Optional for richer data:
- `IgnitionStateRequest` (311) / `IgnitionStateResponse` (312)
- `MalfunctionRequest` (261) / `MalfunctionResponse` (262) — DTCs
- `CanRequest` (275) / `CanResponse` (276) — raw CAN signals
- `FfdRequest` (273) / `FfdResponse` (274) — freeze frame
