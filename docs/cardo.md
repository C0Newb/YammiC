# Cardo (Packtalk / Freecom) integration notes

Research notes, written so the work can be picked up later. Nothing here has been tested against a Cardo unit; everything comes from reading a decompile of the Cardo Connect app (kept locally in `.local/Cardo-Source`, which is git-ignored, with `sources/` and `resources/`) and a couple of web searches.

Use the decompile the same way as the Yamaha one: for packet layouts and opcodes only, not for its Android code.

## Goal

Show which group the rider is in on the NavUI screen (and maybe list the riders), and change group from the POI list (or similar). Ideally without YammiC connecting to the Cardo unit itself.

## Is there an API?

No public one.

- Web searches found no developer program, SDK, third-party integration docs, or community reverse engineering of the Packtalk/Freecom protocol. (Not proof that a partner program doesn't exist; Cardo support is the only way to be sure.)
- The Cardo app has no real inter-app interface for mesh state. What it does expose (from `AndroidManifest.xml`):
  - Deep link `smartset://intercom-invitation?joinGroup=<id>` into `HomeActivity`: joins one of Cardo's internet ("Intercom"/VoIP) groups by remote ID. These are not mesh channels.
  - `HomeActivity` also reads intent extras `TYPE_ARG_HOME` = `accept` / `decline` (with `GROUP_ID_ARG`) to accept or decline an incoming internet group call.
  - Exported activities `IntercomNewActivity` and `ViewGroupsActivity`: launching only opens the screen, nothing comes back.
  - `MusicPlayerService` (exported media browser service) and broadcasts `com.cardo.smartsetapp.play|pause|songchanged`: for Cardo's own music player.
  - No content providers or bindable services exposing state. Its FileProvider and VoIP provider are not exported.
- During an internet group call `CallService` posts an ongoing notification "Active call in <group name>" built from custom views (mute and leave buttons are `RemoteViews` click intents, not notification actions). A notification listener (YammiC already asks for that access) may be able to read the group name, but could not press the buttons, and the text may not be in the notification extras. Not checked.

Conclusion: routing through the Cardo app only covers the internet groups, not the mesh. Mesh control means talking to the unit directly.

## How obfuscated is it?

Barely. Package, class and opcode names survived (for example `DMCJoinGroup`, `DMCMuteGroup`, `GroupHeader`, `Rider`). The protocol layer is about 500 files under `com/cardo/bluetooth` and `com/cardo/caip64_bluetooth`. Porting the packet layouts looks small.

## Two protocols (CAIP)

| | Older, "CAIP9" | Newer, "CAIP64" |
| -- | -- | -- |
| Package | `com.cardo.bluetooth` | `com.cardo.caip64_bluetooth` |
| Link | classic Bluetooth serial (RFCOMM) | BLE (`BLEService`, RxBle) |
| Which units | unknown, check by model | unknown, check by model |

UUIDs seen in the code (not yet matched to units):
- `fa87c0d0-afac-11de-8a39-0800200c9a66` (secure), `0000fd01-0000-1000-8000-00805f9b34fb`, `7548331e-7925-11e7-b5a5-be2e44b06b34` (`bluetooth/BluetoothService.java`)
- `CD007F85-8B0B-11E6-AE22-56B6B6499611` ("PRO1", `caip64_bluetooth/BluetoothService.java`)
- BLE read/write characteristic UUIDs come from `caip64_bluetooth/ble/UUIDService.java` (not yet read).

Which one the owner's unit uses depends on its model. Open question: Cardo model.

## Framing (CAIP9, from `BluetoothPacket.java`)

`[version = 9][payload length 1B][opcode 1B][payload...]`. No checksum or encryption was visible in the packet class. Many commands are tiny: `DMCJoinGroup` is opcode 32 with payload `00 D0 00`. CAIP64 packet classes live under `caip64_bluetooth/packet/` and were not read yet.

Opcode and event constants are in `bluetooth/packet/messeges/CAIPEvents.java`, for example: `DMC_COMMAND_OPCODE` 37, `CONNECTIVITY_OPCODE` 36, `DEVICE_NAME_GET` 38, `DEVICE_NAME_SET` 35, `FM_SHARING_OPCODE` 40, `CONTROL` 32, `GROUPING` 4, `GROUPING_V2` 5, `GET_RIDER` 4, `ADD_RIDER` 34.

## What could be read from the unit

From the data classes in `bluetooth/packet/messeges/services/`:

- Group state: `GroupHeader` (rider ID, number of units, maximum, membership mask, rider mask, unicast target) and `GroupRole` (creator or member).
- Riders: `Rider` / `GetRiderResponse` / `GroupingRecord`: a short name (7 bytes), Bluetooth address, capability bytes, group ID, flags for "in range" and "private chat".
- Battery: `BatteryStatusService`, steps from 0 (critical) to 9 (full).
- Connectivity and headset state: `ConnectivityService`, `HeadsetStateRecord` (not decoded).
- Unit name, firmware version, settings: `GetDeviceName`, `GetVersion`, `GetSettings`.

Not found in the data classes: a mesh channel number or group name (the named groups in the Cardo app look like the app's own storage/cloud, which are the internet groups), who is talking, signal quality or distance, locations of other riders.

## What could be sent

- Mesh (`messeges/dmc/`): `DMCJoinGroup`, `DMCEndGroup`, `DMCDeleteGroup`, `DMCMuteGroup`, `DMCBridge`, `DMCSetGroupHeader`, `DMCSetRider`, `DMCStartMaster`, `DMCTalkToggle`, `DMCUnicast`, `DMCtoICSwitch`.
- Bluetooth intercom (`messeges/intercom/`): pair/reset a rider, start channel call or conference, end call.
- Music and FM: skip, play/pause, music sharing, FM seek/scan/stations.
- Phone: redial, voice dial, reject, end call, speed dial, add rider to call.

## Open questions

1. Which Cardo model (decides CAIP9 vs CAIP64).
2. Does the unit accept a second control connection while the Cardo app is connected? If not, YammiC and the Cardo app cannot run together.
3. Pairing/bonding handshake and any authentication (not read).
4. What a "group" means on the wire: the meaning of the join payload, rider slots, masks. Needs testing on a unit.
5. Whether switching to a named group is possible at all, or only join/end.

## Suggested path

1. Read-only first: connect, log what the unit sends, decode group header, riders and battery. Send nothing.
2. Show the current group and rider list in a NavUI screen variable.
3. Only then try group commands (mute, then join/end), one at a time, with the log open.

Same approach as the CAN and ride-mode work: port layouts, log raw bytes, adjust from what the hardware returns.
