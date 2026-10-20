# CCU Emulator — Design

Date: 2026-10-10
Status: Draft for review

## Goal

A C# / Avalonia emulator of a Yamaha CCU, so the C# client app (the planned replacement for the Kotlin YammiC app) can be developed and tested without the bike. The MVP should also be able to talk to the official Y-Connect app.

The CCU side is written fresh from the wire formats in `docs/` and from captured traffic (`.local/YammiC Device Logs/btsnoop_hci.log`). YammiC's client classes are not ported. No official Yamaha code is used or copied (see `AGENTS.md`).

## Scope

### In the MVP
- BLE GATT server that advertises as `YCCU_<ccuId>`.
- Auth, identify and ping/pong.
- CCU-initiated requests after auth: language, weather, device time, Bluetooth headset state, volume-controllable, phone cell signal level.
- Phone-to-CCU data, shown on an emulated dash: music metadata, notifications, call state, phone state, phone thermal state.
- Virtual Pit Board (VPB): accept the request over BLE, receive the image chunks over the Classic SPP link, ack each chunk over BLE, reassemble and render the JPEG, handle clear. This needs a Classic RFCOMM client (see Wire behavior).
- Action-specific control buttons that send CCU-to-phone commands.
- Packet log with decoded fields.

### Not in the MVP
- GPS / navigation display (NaviLite over Classic RFCOMM). Planned as a second milestone, with its own transport backend.
- Pairing. The emulator reuses the identity of the user's real bike (CCUID, passkey, MAC). Fake identities, a QR code and the cloud pairing API are later experiments.
- Android, iOS and Browser heads of the Avalonia template. They are dropped for now.
- A D-Pad / OK pad. The wire protocol has no generic D-Pad command, so buttons are action-specific. A D-Pad can be layered on later (likely for navigation).

## Structure

One project, `CCU_Emulator`, plus the existing `CCU_Emulator.Desktop` head.

```
CCU_Emulator/
  Protocol/    Command codes, packet encode/decode, DDT reassembly. No Avalonia, no Bluetooth.
  Emulation/   CcuSession: emulated CCU state, request scheduling, packet handlers.
  Transport/   ICcuTransport + implementations.
    LoopbackTcpTransport.cs        compiled on every platform
    Platforms/Windows/             WINDOWS_BLUETOOTH only
  ViewModels/  CommunityToolkit.Mvvm view models
  Views/       Avalonia views
```

- The project multi-targets `net10.0` plus `net10.0-windows10.0.19041.0` when built on Windows. The Windows target defines `WINDOWS_BLUETOOTH`. Windows-only code sits under `Transport/Platforms/Windows/` and is compiled only for that target.
- `CCU_Emulator.Desktop` multi-targets the same way, and disables AOT on the Windows target if the WinRT interop needs it.
- The project is self-contained. It has no dependency on CNCO.Unify or any other external library of the author's.
- `Protocol/` has no UI or Bluetooth dependencies, so it can be unit-tested in isolation.

### ICcuTransport
A small interface for "bytes in, bytes out" on the BLE characteristic roles (simple write, main RX, DDT control/status/data) plus one raw Classic SPP byte stream: receive a write, notify a characteristic, send and receive SPP bytes, connection state events for BLE and SPP, start and stop. The Windows implementation maps BLE onto `GattServiceProvider` and SPP onto an RFCOMM client socket (`StreamSocket` to the phone's SPP service). The loopback TCP implementation tags each frame with a characteristic role, so a client on another machine or in a test can drive the emulator with no radio. Frame format: `[characteristic role 1B][length 2B big-endian][bytes]`, one TCP connection per client, default port configurable in settings.

## Wire behavior

### BLE server
- Advertises `YCCU_<ccuId>` (19 characters).
- Primary service `AFA2CDF4-ECCF-46A7-A5EA-9DA428C0157A` with the characteristics listed in `AGENTS.md`: simple write, main RX (notify), DDT control, DDT status (notify), DDT data, YRC settings RX, DDT hi-bandwidth, DDT large, DDT v2, DDT v3 (indicate), YRC response RX. The notify characteristics have CCCD descriptors so the client's subscription writes succeed.
- Negotiated MTU is whatever the client requests (YammiC requests 183). DDT chunk size is `MTU - 7`.

### Phone to CCU
- **Auth, cmd `0x0101`.** 22 bytes: `[cmd 2B][ccuId 14B][passKey 6B]`, no length prefix. The emulator compares against the configured identity and replies `0x0102` with `01` (success) or `00` (failure).
- **Identify, cmd `0x0002`.** Records the app ID.
- **DDT sessions.** The phone writes `[requestId][size 3B]` to the control characteristic. The emulator notifies the status characteristic with `[1][requestId]`, receives chunks `[requestId][offset 3B][data...]` on the data characteristic, and on completion notifies `[0][0]`. The reassembled message is `[cmd 2B][payload]`, exactly what YammiC sends and what works against a real CCU. There is no extra length prefix, except that `NotificationDataV2` (`0x012F`) carries its own `[payloadLength 3B]` before the protobuf. (`docs/protocol.md` describes the DDT characteristics differently; `AGENTS.md` and the working YammiC code win.) The DDT status characteristic must be readable and report idle (`value[0] == 0`) between sessions.
- **Simple writes.** `[cmd 2B][requestId 1B][payload]` on the simple write characteristic (weather info, language response, pong).
- **Decoded and shown:** music metadata (`0x0116`), notifications and call info, phone state, thermal state, cell-signal response, headset-state response, volume-controllable response.
- **VPB.**
  - `0x0231` request (BLE simple write) → `0x0233` accept (or `0x0232` reject, if the rejection toggle is on), notified over BLE
  - `0x0235` chunk arrives on the **SPP socket**, framed `[0x01][len 4B][payload][0x04]`; layout `[cmd][contentId 4B][index 1B][chunkSize 4B][totalIndex 1B][totalSize 4B][crc32 4B][data]`; the CRC32 is the nonstandard variant in `docs/vpb.md` (init 0). Each chunk is answered with `0x0236` `[contentId 4B][index 1B][1=ok|2=retry|3=abort]`, notified over BLE
  - reassemble and render the JPEG
  - `0x0237` clear (BLE) → `0x0238` ok
- **SPP link.** The phone listens on SPP service UUID `00008911-0000-1000-8000-00805f9b34fb`; the CCU connects to it as the client. After BLE connect the emulator connects, then sends one `0x013A` frame carrying `[ccuId 14B][passKey 6B]`. The link is optional: YammiC falls back to stored credentials after 2 seconds. The Windows backend needs the phone's Classic Bluetooth address and a prior Windows pairing with the phone.

### CCU to phone
- **After auth,** each of these is individually toggleable. Order and bytes follow the real CCU as seen in `.local/YammiC Device Logs/yammic-*.log.txt`: `0x0001` LibVersion (`00 01 03 01 01`), language request (`00 0B`), device-time request (`00 09`, repeated every ~5 s until answered), volume-controllable (`01 29`). Also toggleable but not seen in that capture: weather request (`0x0003`), Bluetooth headset state (`0x011B`), cell signal level (`0x0132`). Periodic ping is `01 35 <seq>`, a counter byte starting at 0, every 10 s by default (configurable).
- **Order on the real CCU:** the SPP connection is made first (before the BLE link is connectable), and the BLE side notifies auth success as `01 02 01`.
- **Buttons:**
  - Next, Previous, Play, Pause → `0x0117` with byte `01`, `02`, `03`, `04`
  - Call volume up/down, mute mic, mute speaker, media volume up/down → `0x011D` with byte `01`, `02`, `03`, `04`, `05`, `06`
  - Call reject / accept / hang up → `0x0119` with byte `00`, `01`, `02`
  - Call buttons are disabled when no call is active.
- **Fault injection:** force an auth failure, drop the connection.

### Ground truth and unknowns
- `AGENTS.md` flags the DDT status notification format as unverified. The btsnoop capture is the source of truth for what a real CCU sends and when.
- The official Y-Connect app may exercise paths YammiC never does. Anything unrecognized is logged as hex and otherwise ignored.

## UI

Main window: an emulated dash on the left, a control column on the right.

### Dash panel
A fixed-aspect-ratio TFT-style view. The view shown, in priority order:
1. **Incoming call:** caller name or number.
2. **VPB image:** the last decoded JPEG, or a "cleared" state.
3. **Idle dash:** now-playing artist, album and song, weather, time, and a status strip for cell signal and Bluetooth headset.

Notifications show as a transient banner over any view, with a short history list.

### Control column (tabs)
- **Connection:** start/stop server, advertising and connected state, auth result.
- **Buttons:** the action-specific button panel.
- **Notifications:** the received-notification history, each with a "dismiss on dash" button (sends `0x0013` to the phone).
- **CCU behavior:** the request toggles, ping interval, fault injection.
- **Log:** live packet log with direction, command name, hex and decoded fields; filter; copy.

## Settings

- `emulator.settings.json` in a gitignored local path, created with placeholder values on first run.
- Fields: CCUID, passkey, MAC, advertised name (derived), transport (`WindowsBle` or `LoopbackTcp`, plus the TCP port), the CCU-behavior toggles.
- The repo contains only an example file with fake values. The user's real bike identity (see `.local/AGENTS.md`) is never committed.

## State, threading, errors

- `Emulation/CcuSession` owns the music, weather, notification, call, phone and VPB state and raises change events. View models bind to it via CommunityToolkit.Mvvm.
- Transport callbacks are marshalled to the UI thread at the view-model boundary. `Protocol/` and `Emulation/` never reference Avalonia.
- Malformed or unknown packets are logged in hex and ignored, never fatal.
- Each connection step logs explicitly (advertise, subscribe, auth, identify) so failures trace to the right layer, as in `AGENTS.md`.

## Testing

- Manual: drive the emulator over the loopback TCP transport (scripted test client now, the C# client app later), then from the real Y-Connect app over Windows BLE.
- Automated: a test project for `Protocol/` packet codecs and DDT reassembly, using bytes from the btsnoop capture.

## Open risks

1. **Windows as a CCU for the official app.** The adapter supports the peripheral role (checked: `IsPeripheralRoleSupported = True`). It is not yet known whether the official app accepts a Windows GATT server: it may require specific bonding or security properties on the characteristics, or a matching MAC.
2. **Bonding.** The real CCU bonds with the phone. How the emulator should present bonding (Just Works vs. numeric) is unknown. Reusing the real bike's MAC is likely impossible on Windows, so the official app may need a different bonding path.
3. **DDT status format** is unverified against a live session (see above).
4. **Advertised name on Windows (probed 2026-10-10, no phone needed).** `GattServiceProvider` cannot set the advertised local name, and `BluetoothLEAdvertisementPublisher` rejects both `LocalName` and raw name data sections, so Windows cannot advertise `YCCU_<ccuId>` at all (computer names are also limited to 15 characters). With the 128-bit service UUID the connectable, discoverable advertisement aborts on this PC, probably because the PC name overflows the 31-byte advertisement. Details in `docs/emulator.md`. Fallbacks: a client that scans by service UUID for emulator use, other advertising hardware, or a Linux/BlueZ host. Decision needed from the user.
5. **SPP direction.** The CCU is the Classic *client*. It needs the phone's Classic address and a Windows pairing with it.
6. **Real bike conflict.** With the real bike's identity cloned, the real bike must be off or out of range during emulator tests.

## Milestones

1. Protocol + loopback TCP transport + dash/log UI, exercised by a small scripted test client (the C# client app does not exist yet) and by `Protocol/` unit tests.
2. Windows transports: advertising spike, then BLE GATT server and the SPP/RFCOMM client; connect with the official Y-Connect app (or YammiC).
3. GPS display: NaviLite handshake/auth over a second RFCOMM role, JPEG frame viewer.
