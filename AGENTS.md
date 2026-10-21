# AGENTS.md

Make use of `./local/AGENTS.md` too for local customization.

## Project Goal

Build a feature-comparable app for Yamaha CCU equipped bikes; talking directly to the motorcycle CCU over BLE.

**DO NOT** use, reference, or otherwise 'borrow' official Yamaha code or other Yamaha copywritten materials.

## Workspace Layout

- `YammiC/` — the Android app under active development (Kotlin, Compose, coroutines).
- `docs/` — protocol documentation derived research sessions (such as BLE snoops).
- `CCU-Emulator/` — C# / Avalonia CCU emulator for developing clients without the bike; see `docs/emulator.md`.

## Current App State

Core files:

- `YammiC/app/src/main/java/co/cnewb/yammic/MainActivity.kt` — Compose debug UI; binds to `BikeForegroundService` to get the controller
- `YammiC/app/src/main/java/co/cnewb/yammic/bike/BikeForegroundService.kt` — foreground service; owns the `YmcConnectionBroker` and `NavigationSdk`, registers all `Ccu*RequestHandler`s, runs on-connect jobs, shows a persistent notification, exposes VPB/navigation enable toggles
- `YammiC/app/src/main/java/co/cnewb/yammic/bike/PhoneCallMonitor.kt`, `MediaStateMonitor.kt`, `MusicMetadataProvider.kt`, `DemoMusicMetadataProvider.kt`, `WeatherProvider.kt`, `YammicNotificationListenerService.kt`, `BikeDebugState.kt`, `DownloadsLogWriter.kt` — Android-integration glue (telephony/media/notification listeners, demo data, rolling log writer to `Downloads/YammiC/`)
- `YammiC/app/src/main/java/co/cnewb/yammic/ymcsdk/YmcConnectionBroker.kt` — connection lifecycle: scan, bond, GATT connect/MTU/discovery, auth, identify, periodic + on-connect job scheduling
- `YmcMessageBroker.kt` / `YmcMessageHandler.kt` — outgoing packet senders / incoming command dispatch
- `YmcBikeProtocol.kt` — UUID constants and pure byte-framing helpers (DDT session-start/chunk packets, SPP frame packet); no Android dependencies
- `YmcBikeSessionStore.kt` — persists credentials and device name to SharedPreferences
- `YmcConnectionContext.kt` / `YmcBluetoothConnectionManager.kt` — GATT/BLE plumbing (scan, connect, notification subscription, writes)
- `YmcVirtualPitBoardController.kt` / `vpb/VpbImageBuilder.kt` — Virtual Pit Board image send/clear
- `handlers/` — one `Ccu*RequestHandler` per incoming command (media control, volume control/controllable, weather, incoming-call control, phone cell signal level, bluetooth headset state, ping, device time, device language)
- `packets/YmcCommandCode.kt` — full command code enum; `packets/YmcPacket.kt`, `YmcBasePacketHandler.kt` — packet framing/DDT session implementation; `packets/app/` and `packets/ccu/` — outgoing/incoming packet types
- `models/` — data models (`YmcMediaMetaData`, `YmcWeatherInfo`, `YmcCellSignalLevel`, `YmcPhoneThermalState`, `BikeVolumeType`, etc.)
- `navigation/` — `NavigationSdk`, `NavigationEngine`, `NavigationHandshake`, `NavigationState`, `navigation/protocol/` (NaviLite codec/auth/CRC32) — YammiC's own NaviLite client for the navigation-screen RFCOMM link; see `docs/navigation.md`
- `providers/YmcMusicMetadataProvider.kt`

The app currently:

- Runs a foreground service (`BikeForegroundService`) so the connection survives when the screen is off.
- Scans for `YCCU_<CCUID>` by BLE name.
- Checks bond state; bonds if needed, skips if already bonded.
- Connects GATT, negotiates MTU (183), discovers services, enables notifications on all RX characteristics.
- Registers a `Ccu*RequestHandler` per supported incoming command via `YmcMessageHandler`.
- Sends auth (`AuthenticationRequest`, cmd 0x0101 — `[cmd 2B][ccuId 14B][passKey 6B]`, 22 bytes, over DDT transport but without the length-prefix DDT framing), then `IdentifyAppRequest` (cmd 0x0002).
- On connect: sends phone state, phone thermal state, notification categories, a test notification, current call state, current media metadata, a VPB "Connected" message, and activates the navigation-screen link (each step isolated so one failure doesn't block the rest).
- Responds to: `WeatherRequest` (0x0003), `LanguageRequest` (0x000B), `PingRequest`/keepalive (0x0135), `VolumeControllableRequest` (0x0129), `GetDeviceTimeRequest`, `BluetoothHeadsetStateRequest`, `IncomingCallControl` (0x0119), `PhoneCellSignalLevelRequest` (0x0132).
- Handles handlebar media buttons (`MusicControlRequest` 0x0117) via the media handler.
- Handles CCU volume commands (`PhoneVolumeControlRequest` 0x011D) and reports the new level back (`PhoneVolumeLevel` 0x011E).
- Sends/receives Virtual Pit Board graphics (see `docs/vpb.md`) and can independently toggle VPB and navigation, since both draw to the CCU display and can interfere with each other.

## CCU Identity

This is only an example.

Observed CCU:

- Advertised name: `YCCU_01234567890123`
- CCUID: `01234567890123`
- MAC: `00:AA:BB:CC:DD:EE`

Credentials from rooted Y-Connect SharedPrefs dump (which gets it from Yamaha's online services):

- `ccu_id = 00100123456789`
- `passkey = 012345`
- `phone_uuid = 0123456789abcdef0123456789abcdef`

## Authentication

### Protocol

Auth uses `AuthenticationRequest` (command code 0x0101), implemented in `AppAuthenticationRequest.kt`.

The vendor SDK's own documentation describes this as a DDT-wrapped message with a 3-byte length prefix around a 20-byte `ccuId + passKey` payload (see `docs/authentication.md`). YammiC found this framing does not work: **YCCU expects exactly 22 bytes, with no length-prefix framing** —

```
[cmd 2 bytes]      = 0x01 0x01
[ccuId 14 bytes]   ASCII bytes of the 14-char CCUID
[passKey 6 bytes]  ASCII bytes of the 6-char passkey
```

sent over the DDT transport (chunked per `docs/protocol.md`), but skipping the usual DDT length-prefix wrapper. No phone UUID is included.

### Response

`AuthenticationResponse` (cmd 0x0102) arrives as a notification on the main RX characteristic:
```
bytes[0..1] = 0x01 0x02
bytes[2]    = 0x01 (success) or 0x00 (failure)
```

## BLE Services and Characteristics

Primary service UUID:
```
AFA2CDF4-ECCF-46A7-A5EA-9DA428C0157A
```

Characteristic roles:

| UUID | Role |
|---|---|
| `B606C7F9-E5A1-4E75-B313-2A920054A8EB` | Simple write (weather, language, alive, ConnectAppId) |
| `9C810D26-B605-4306-8C1C-755A1BA3066C` | Main RX — most CCU responses, notify |
| `56851826-A090-4F6A-A5DF-D1776520F74A` | DDT control — session-start packet write |
| `75AB2788-87F6-4F61-BA9C-6F85A12600D1` | DDT status — active/complete notifications |
| `E998AEAB-EA8F-4063-83AA-2017C1A44484` | DDT data — chunk writes |
| `AB8D233A-CC58-4932-B397-A3751911E021` | YRC settings RX, notify |
| `77808451-E42F-4F6B-83ED-3AB44AA3D16A` | DDT hi-bandwidth, notify |
| `347456EE-A588-4DD5-BD98-62BE3672E27A` | DDT large, notify/write |
| `E974B9A1-FA31-43C6-91AE-09931F3F6EF1` | DDT v2, notify |
| `56DC2D58-CBE1-4BA9-A104-E9B8EB4DA95C` | DDT v3, indicate |
| `7690EEF4-F3A1-11EC-B939-0242AC120002` | YRC response RX |

CCCD descriptor: `00002902-0000-1000-8000-00805f9b34fb`

## CCU Connect Flow

Vendor-SDK-documented flow (see `docs/discovery-and-connection.md`, `docs/implementation-guide.md`); YammiC's actual sequence lives in `YmcConnectionBroker.runConnectAndSend`:

1. Scan BLE; filter by name starting with `YCCU_`, total length 19 chars.
2. Bond if not already bonded.
3. `connectGatt` with `TRANSPORT_LE`, `autoConnect = false`.
4. `discoverServices`.
5. `requestMtu(183)` — YammiC requests 183 directly rather than 512.
6. Enable notifications/indications on all RX characteristics (serialised — one descriptor write at a time).
7. Send `AuthenticationRequest` (cmd 0x0101, 22-byte framing — see [Authentication](#authentication) above).
8. Send `IdentifyAppRequest` (cmd 0x0002) — app ID = 4660 (0x1234).
9. Respond to `LanguageRequest` (cmd 0x000B) with `GetDeviceLanguageResponse` (cmd 0x000C).
10. Respond to `WeatherRequest` (cmd 0x0003) with `WeatherInfo` (cmd 0x0005) for each time slot.
11. Respond to `PingRequest` (cmd 0x0135) with `PongResponse` (cmd 0x0136).

Vendor docs also describe an optional `VehicleInformationSubscribeInterval` (cmd 0x0006) telemetry step and a periodic `BluetoothMusicMetaData` (cmd 0x0116) push every ~2 seconds; YammiC currently pushes music metadata reactively (on connect and on media-session change via `MediaStateMonitor`) rather than on a fixed timer — see `docs/media-metadata.md`.

## DDT Session Protocol (phone → CCU)

Used for: auth, music metadata, icon arrangement, notification data.

1. Write `[requestId][size_hi][size_mid][size_lo]` to the **control characteristic** (`56851826`).
2. Await notification on **status characteristic** (`75AB2788`) where `value[0]==1 && value[1]==requestId`.
3. Write chunks to **data characteristic** (`E998AEAB`): each chunk is `[requestId][offset_hi][offset_mid][offset_lo][data...]`.
4. Await notification on **status characteristic** where `value[0]==0 && value[1]==0`.

Chunk size: `MTU - 7` bytes.

The logical message wrapped by DDT: `[cmd_code 2 bytes][payload_length 3 bytes][payload]`.

## Simple Write Protocol (phone → CCU)

Used for: WeatherInfo, GetDeviceLanguageResponse, PongResponse, IdentifyAppRequest.

Direct write to `B606C7F9`. No handshake. Packet format: `[cmd_code 2 bytes][requestId 1 byte][payload]`.

If `B606C7F9` is not writable, fall back to `E998AEAB`.

## Key Command Codes

Names below are the `YmcCommandCode` enum values used in YammiC. See `docs/commands.md` for the full vendor-SDK-derived reference (which uses slightly different names in places, e.g. `AuthenticationV2Request`/`AliveCheckRequest`).

| Direction | Code | Name |
|---|---|---|
| P→C | 0x0002 | IdentifyAppRequest |
| P→C | 0x0005 | WeatherInfo |
| C→P | 0x0003 | WeatherRequest |
| P→C | 0x000C | GetDeviceLanguageResponse |
| C→P | 0x000B | LanguageRequest |
| P→C | 0x0101 | AuthenticationRequest |
| C→P | 0x0102 | AuthenticationResponse |
| P→C | 0x0116 | BluetoothMusicMetaData |
| C→P | 0x0117 | MusicControlRequest |
| P→C | 0x0136 | PongResponse |
| C→P | 0x0135 | PingRequest |
| C→P | 0x0007 | VehicleInformation (speed, ignition) |

Full command reference: `docs/commands.md`.

## Protocol Documentation

See `docs/` for full reference:

- `docs/implementation-guide.md` — step-by-step connection recipe with exact byte layouts
- `docs/mvp-music-and-weather.md` — exact wire formats for music and weather
- `docs/protocol.md` — DDT framing, simple write, byte order
- `docs/authentication.md` — auth handshake detail
- `docs/pairing.md` — how to obtain CCUID and passkey
- `docs/commands.md` — full command code reference
- `docs/can-signals.md` — CAN signal IDs and scaling for rich vehicle data
- `docs/capabilities.md` — full capability overview

## Known Open Questions

1. **Simple write characteristic** — `B606C7F9` (`YmcBikeProtocol.simpleWriteUuid`) is the documented write char. If weather/language/alive responses are rejected, try `E998AEAB` (`dataUuid`) as the target instead.
2. **DDT status notification format** — `value[0]==1, value[1]==requestId` for active; `value[0]==0, value[1]==0` for completion. Derived from existing YammiC code. Unverified against a live session.

## Working Rules

- Keep BLE / protocol code isolated in `ymcsdk/`.
- `YmcBikeProtocol` is pure functions — no Android dependencies, no I/O.
- `YmcConnectionBroker` owns all coroutine lifecycle and GATT state.
- `models/` owns all data types.
- Keep logs explicit so connection failures can be traced to bond, auth, GATT, or notify setup.

## C# Code Style

Applies to the C#/Avalonia port (`YammiC/`). Formatting is CSharpier (`csharpier format <paths>`; format only the files you changed).

- **No `is { }` / `is not { }` property or null patterns.** Write plain `!= null` / `== null` checks instead, and use a local variable (`var x = Foo(); if (x != null) ...`) when the value is needed afterwards. This includes `is { Length: > 0 }` and `is { Kind: ... }` (compare the members explicitly). For a nullable struct, use `.Value` after the null check.
- **`<summary>` tags go on their own lines**, never `/// <summary>text</summary>` on one line:

  ```csharp
  /// <summary>
  /// What this does.
  /// </summary>
  ```
- Keep it readable, not clever: prefer a few clear statements over a dense one-liner.

## Build

```bash
./gradlew.bat :app:compileDebugKotlin
```

Requires `JAVA_HOME` set to a JDK 17+ installation.
