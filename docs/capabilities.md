# CCU Capabilities

What the phone and CCU can exchange over BLE. Organised by who initiates and what the data is.

---

## Phone → CCU (phone pushes data to the bike display)

### Music / Media
Command: `BluetoothMusicMetaData` (0x0116)

| Field | Type | Notes |
|---|---|---|
| Artist name | string | Max 64 UTF-8 bytes |
| Album name | string | Max 64 UTF-8 bytes |
| Song name | string | Max 64 UTF-8 bytes |
| Duration | int (ms) | Up to ~4.6 hours |
| Playback state | enum | Paused / Playing / Rewinding / FastForwarding / Unavailable |
| Playback rate | float | ×10 as short — e.g. 1.0× = 10 |
| Elapsed time | int (ms) | Current position |

The CCU can also send `BluetoothMusicControl` (0x0117) back to the phone — handlebar buttons for play/pause/skip.

---

### Weather
Command: `WeatherInfo` (0x0005)

The CCU requests weather via `WeatherRequest` (0x0003). The phone should respond for the current slot and optionally prefetch forecasts.

| Field | Type | Notes |
|---|---|---|
| Time slot | enum | Current / +1h / +2h / +3h / +1day … +6day |
| Weather type | enum | 28 values — Sunny, Rain, Snow, Fog, etc. |
| Temperature | short | °C × 10 (e.g. 25°C = 250) |
| Max temperature | short | °C × 10 |
| Min temperature | short | °C × 10 |
| Precipitation % | byte | 0–100 |

**Time slots the CCU may ask for:** Current, +1h, +2h, +3h, +1 day, +2 day, +3 day, +4 day, +5 day, +6 day.  
For an MVP, respond to any request with just the current slot. For a richer display, pre-send all 10 slots after auth.

---

### Notifications (phone → bike display)
Commands: `AddNotification` / `UpdateNotification` / `RemoveNotification` / `NotificationData` / `NotificationDataV2`

Push phone notifications (SMS, calls, apps) to the CCU display. `NotificationDataV2` uses protobuf. The CCU sends `NotificationRemovedNotification` when the rider dismisses one.

---

### Incoming Call
Command: `IncomingCallInformation` (0x0118)

| Field | Type | Notes |
|---|---|---|
| Caller name | string | Max 64 bytes |
| Phone number | string | Max 12 bytes |
| Is missed call | bool | |

CCU sends `IncomingCallControl` (0x0119) back — accept/reject from handlebar.

---

### Phone State (batch update)
Command: `PhoneStateCombined` (0x013C)

Single command encoding: battery %, charging flag, phone volume, media volume, signal level, thermal state, headset connected, on-hook flag.

Individual commands also available: `PhoneBattery`, `PhoneVolumeLevel`, `PhoneThermalState`, `PhoneCellSignalLevelResponse`.

---

### Date/Time
Command: `VehicleSettingDatetime` / `DatetimeChangeNotification`

Push current time to the CCU so it can display the correct clock.

---

### Icon Arrangement
Commands: `IconArrangementListRequest` (CCU asks) → `IconArrangementListResponse` (phone replies)

Phone sends the ordered list of icon IDs to display on the CCU home screen. CCU initiates the request.

---

### Language
Commands: `LanguageTypeRequest` (CCU asks, cmd 0x000B) → `LanguageTypeResponse` (phone replies, cmd 0x000C)

Send a language byte. See `data-models.md` for byte values. English = 0x00.

---

## CCU → Phone (bike sends data to the phone)

### Vehicle Information (basic, periodic)
Command: `VehicleInformation` (0x0007)

Subscribe with `VehicleInformationSubscribeInterval` (0x0006).

| Byte offset | Field | Scaling |
|---|---|---|
| 2–3 | Speed | short km/h or mph |
| 4–5 | Revs-based speed | short |
| 6–7 | Gear-based speed | short |
| 8 | Ignition state | 0=Off, 1=On |
| 9 | Speed unit | 0=km/h, 1=mph |

Simple and low-overhead. Good for a dashboard speed display.

---

### CAN Bus Signals (rich vehicle data)
Commands: `CanRequest` (0x0113) → `CanResponse` (0x0114), `CanIntervalRequest` (0x013B)

Request specific signal IDs. Response is id/data pairs. See `can-signals.md` for the full signal catalogue.

Supports one-shot requests and periodic polling with a configurable interval (ms).

---

### Malfunction / DTC Codes
Commands: `MalfunctionRequest` (0x0105) → `MalfunctionResponse` (0x0106)

Response is a list of `(server, code, status)` tuples. Status values: Unknown / Triangle (warning) / Full (error) / TemporaryMalfunction.

Also: `MalfunctionIntervalRequest` (0x0109) for periodic polling.

---

### Freeze Frame Data (FFD)
Commands: `FfdRequest` (0x0111) → `FfdResponse` (0x0112)

Snapshot of sensor readings captured at the time of a fault. Request takes a `server` ID (short). Response contains raw byte arrays keyed by server ID. Decode using the same scaling as CAN signals.

---

### Vehicle Identification
Commands: `VehicleIdentificationRequest` (0x010A) → `VehicleIdentificationResponse` (0x010B)

VIN, model, firmware version, etc.

---

### Ignition State
Commands: `IgnitionStateRequest` (0x0137) → `IgnitionStateResponse` (0x0138)

Simple on/off. The OEM app polls this periodically (configurable timer). Not pushed by the CCU — must be polled.

---

### WiFi / Server Info
Command: `WifiHost` (0x0016)

CCU pushes its WiFi credentials to the phone. Used for OTA update path.

---

### OTA Firmware Update
Multiple commands (0x0121–0x012E). Requires a Bluetooth Classic SPP connection in addition to BLE. Out of scope for a minimal client implementation.

---

## YRC — Yamaha Ride Control Settings
Commands: 0x0201–0x0209

Read/write the bike's ride control settings (traction control level, power mode, etc.) via a DDT v3 session. The payload is protobuf-encoded. Requires knowing the specific YRC parameters for the bike model.

---

## Display / Photo Frame / Push Notifications
Commands: 0x0211–0x0252

Push custom images to the CCU dashboard. Requires SPP + GATT simultaneously. Out of scope for a minimal client implementation.

---

## Capability Summary

| Capability | Direction | Complexity | MVP? |
|---|---|---|---|
| Music metadata | Phone → CCU | Medium (DDT) | ✅ Yes |
| Music control (play/pause) | CCU → Phone | Low | ✅ Yes |
| Weather (current) | Phone → CCU | Low | ✅ Yes |
| Weather (forecast) | Phone → CCU | Low | ✅ Yes |
| Basic vehicle speed | CCU → Phone | Low | ✅ Yes |
| Ignition state | CCU → Phone | Low | ✅ Yes |
| Notifications | Phone → CCU | Medium | Later |
| Incoming calls | Phone → CCU | Medium | Later |
| Phone state | Phone → CCU | Low | Later |
| CAN signals (rich data) | CCU → Phone | Medium | Later |
| DTCs / fault codes | CCU → Phone | Medium | Later |
| Freeze frame data | CCU → Phone | Medium | Later |
| Vehicle ID / VIN | CCU → Phone | Low | Later |
| YRC settings | Both | High (DDT v3) | Later |
| OTA update | Both | High (SPP+BLE) | No |
| Display images | Both | High (SPP+BLE) | No |
