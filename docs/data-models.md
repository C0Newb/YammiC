# Data Models

## Authentication

### `AuthenticationV2Request`
```
ccuId    byte[]   14-byte CCUID from device name suffix
passKey  byte[]   shared secret passkey
```

### `AuthenticationV2Response`
```
success  boolean  true = authenticated, false = rejected
```

### `Ccuid`
```
ccuId  byte[]   raw CCUID bytes returned from device
```

---

## Vehicle State

### `VehicleInformation` (cmd 7)
Received continuously when subscribed.
```
speed          Speed   current speed
revsSpeed      Speed   engine speed (rev-based)
gearSpeed      Speed   gear-based speed
ignitionState  IgnitionState  ON / OFF / UNKNOWN
speedUnit      SpeedUnit  KMH / MPH
```

Wire format is 10 bytes.

### `IgnitionState` (enum)
```
ON / OFF / UNKNOWN
```

### `Speed`
```
value  short   speed value
```

### `SpeedUnit` (enum)
```
KMH / MPH
```

---

## Scan

### `ScanResult`
```
device  BluetoothDevice  raw Android BLE device handle
ccuid   String           14-char CCUID extracted from name
```

---

## Identity

### `AppId`
Sent in `ConnectAppId` to identify the phone app to the CCU.
```
(string identifier)
```

### `LibVersion`
```
(version string from CCU)
```

### `DeviceName`
```
(device name string)
```

---

## Language

Used in `LanguageTypeResponse` (cmd 0x000C). The byte value sent over BLE:

| Byte | Language |
|------|----------|
| 0x00 | English |
| 0x01 | French |
| 0x02 | German |
| 0x03 | Italian |
| 0x04 | Japanese |
| 0x05 | Spanish |
| 0x06 | Chinese (Traditional) |
| 0x07 | Simplified Chinese |
| 0x08 | Greek |
| 0x09 | Finnish |
| 0x0A | Hindi |
| 0x0B | Indonesian |
| 0x0C | Polish |
| 0x0D | Portuguese |
| 0x0E | Thai |
| 0x0F | Vietnamese |
| 0x10 | Korean |
| 0x11 | Malay |
| 0x1F | Others |

---

## Connectivity

### `BleConnectInformation`
```
(BLE connection parameters returned from CCU)
```

---

## Diagnostics

### `MalfunctionResponse` (protobuf)
```
(DTC fault codes)
```

### `FfdResponse` (protobuf)
```
(freeze frame data — sensor readings at fault time)
```

### `CanResponse` (protobuf)
```
(raw CAN bus signal data)
```

---

## Phone State (phone → CCU)

### `PhoneBattery`
```
level  int  battery percentage 0–100
```

### `PhoneThermalState` (enum)
```
Low / Nominal / Fair / Serious / Critical
```

### `PhoneVolumeLevel`
```
type   Type   Phone or Media
level  int    volume level
```
`Type` enum: `Phone`, `Media`

### `PhoneStateCombined`
Aggregates multiple phone state values into one command.

### `PhoneCellSignalLevelResponse` (enum)
```
NoConnection / NoSignal / Lv1 / Lv2 / Lv3 / Lv4
```

---

## Notifications (phone → CCU display)

### `NotificationId`
```
id  int  notification identifier
```

### `NotificationCategory`
Categorizes the notification type for CCU display routing.

### `IncomingCallInformation`
```
(caller name, number)
```

### `IncomingCallControl` (received from CCU)
```
(accept / reject decision from handlebar button)
```

---

## Music

### `BluetoothMusicMetaData`
```
artistName  String  max 64 bytes
albumName   String  max 64 bytes
songName    String  max 64 bytes
```

### `BluetoothMusicControl` (cmd 0x0117, received from CCU)

Handlebar media button press. Wire byte at `bytes[2]`:

| Byte | Action |
|------|--------|
| 0x01 | NextTrack |
| 0x02 | PreviousTrack |
| 0x03 | Play |
| 0x04 | Pause |

---

## Volume Control

### `PhoneVolumeControllableRequest` (cmd 0x0129, received from CCU)
CCU asks whether the phone supports remote volume control. Reply immediately with `PhoneVolumeControllableResponse(true)`.

### `PhoneVolumeControllableResponse` (cmd 0x012A, phone → CCU)
```
bytes[2] = requestId
bytes[3] = 0x01 (can control) | 0x00 (cannot)
```

### `PhoneVolumeControl` (cmd 0x011D, received from CCU)

Volume button press. Wire byte at `bytes[2]`:

| Byte | Action |
|------|--------|
| 0x01 | VolumeUp (phone call) |
| 0x02 | VolumeDown (phone call) |
| 0x03 | MuteMicrophone |
| 0x04 | MuteSpeaker |
| 0x05 | MediaVolumeUp |
| 0x06 | MediaVolumeDown |

### `PhoneVolumeLevel` (cmd 0x011E, phone → CCU)

Report current volume after handling a `VolumeControl` command.
```
bytes[2] = requestId
bytes[3] = type: 0x00 = Phone, 0x01 = Media
bytes[4] = volume percentage (0–100)
```

`type` values: `Phone(0x00)`, `Media(0x01)`

---

## Weather

### `WeatherRequest` (cmd 0x0003, received from CCU)
```
bytes[2] = 0x01 → Current location
bytes[2] = 0x02 → Point (bytes[3..10] = lat/lng as two 4-byte big-endian ints × 1e6)
```

### `WeatherInformation` (cmd 0x0005, phone → CCU)

Weather data to push to the CCU display.

```
bytes[2]    = requestId
bytes[3]    = time slot (0x00=Current .. 0x09=+6days) — see time-slot table below
bytes[4]    = weather type (0x00=Sunny .. 0x1B=ScatteredStormNight) — see weather-type table below
bytes[5..6] = temperature × 10 as signed big-endian short (e.g. 22°C = 220 = 0x00DC)
bytes[7..8] = max temperature × 10
bytes[9..10]= min temperature × 10
bytes[11]   = precipitation percentage (0–100)
```

**Time slot wire values:**

| Byte | Slot |
|------|------|
| 0x00 | Current |
| 0x01 | +1 hour |
| 0x02 | +2 hours |
| 0x03 | +3 hours |
| 0x04 | +1 day |
| 0x05 | +2 days |
| 0x06 | +3 days |
| 0x07 | +4 days |
| 0x08 | +5 days |
| 0x09 | +6 days |

**Weather type wire values (selected — full enum spans 0x00–0x1B):**

| Byte | Weather |
|------|---------|
| 0x00 | Sunny |
| 0x03 | Rain |
| 0x04 | Snow |
| 0x08 | Fog |
| 0x0D | NoData |
| 0x14 | Cloudy |
| 0x17 | Clear |

---

## YRC Settings

### `YrcInfoDataMessage`
Transfer container for YRC (Ride Control) settings data.

### `NotifySaveYrcSettingsDataResult`
```
success  boolean
```

### `NotifyLoadYrcSettingsDataResult`
```
success  boolean
```

---

## OTA Update

### `OtaLinkcardInformation` (received from CCU)
CCU signals that a firmware update is available.

### `OtaUpdateDataRequest` (received from CCU)
```
(offset and length for next data chunk)
```

### `OtaUpdateDataResponse`
```
(binary firmware data chunk)
```

### `OtaStopRequest` (received from CCU)

**ErrorCode enum:**
```
InternalRawDataLengthOverflowInPacket
InternalRawDataSerialNumberError
InternalDataCrcError
ErrorOccursWhenProgrammingFlash
ErrorOccursWhenProgrammingControlInfo
WrongUpdateFileSignature
WrongDownloadInformation
WrongDownloadPackageNumber
WrongOtaFileHeader
InternalOtaFileData* (various)
WrongOtaFileTailer
```

---

## Error

### `Error` (enum)
```
NoLocationPermission
ScanFailed
InvalidDataFormat
(others)
```

### `CommandError`
```
(error details for command-level failures)
```

### `Result<T>`
```
isSuccess  boolean
value      T         (null on failure)
error      Error   (null on success)
```

---

## Minimum Model Set

For a basic integration, plan to implement models covering at least: scan/discovery results, authentication request/response, music metadata and music control, weather request/information, phone volume control and volume level reporting, and vehicle information (speed, ignition state, unit).

A request ID (`RequestId`) is a single byte, cycling through the range 1–255, used to correlate a request with its corresponding response.

The protobuf-based models (`MalfunctionResponse`, `CanResponse`, `FfdResponse`) require a protobuf parser and a schema definition matching the CCU's protobuf message layout.
