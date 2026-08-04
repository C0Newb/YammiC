# Music Metadata Deep Dive

## What "Media device unavailable" Means

The CCU displays this when it has no valid music metadata to show. It is the CCU's **default state** before your app sends a `BluetoothMusicMetaData` packet, and is also the state triggered by sending playback state byte `0xFF` (`MediaDeviceUnavailable`). In other words: either your write is failing silently, or the CCU is ignoring/rejecting the packet.

---

## Complete Sequence Required for Music Display

The CCU gates music display behind several steps. All must succeed in order:

```
1. Phone → CCU:  PhoneStateCombined (0x013C)
                   └─ headsetStateConnected bit must be 1
                   └─ sent as simple write to B606C7F9 (simpleWriteUuid)

2. CCU  → Phone: BluetoothHeadsetStateRequest (0x011B)
                   └─ arrives on 9C810D26 (mainRxUuid)
                   └─ CCU polls this periodically after connection

3. Phone → CCU:  BluetoothHeadsetStateResponse (0x011C)
                   └─ byte[2] = 0x01 (connected=true)
                   └─ must arrive BEFORE CCU will accept music metadata

4. Phone → CCU:  BluetoothMusicMetaData (0x0116)
                   └─ via DDT-to-Vehicle session (NOT a simple write)
                   └─ see DDT session protocol below
```

Steps 2–4 repeat. The CCU polls for headset state and expects updated music metadata on a regular interval (~1–5 s).

---

## BluetoothMusicMetaData Packet Format (0x0116)

```
[2 bytes]  command code: 0x01 0x16
[3 bytes]  payload length (big-endian, lower 24 bits of 4-byte int)
────────────────────────────────────
[1 byte]   artist name length (N)   ← max 64 UTF-8 bytes
[N bytes]  artist name (UTF-8)
[1 byte]   album name length (M)
[M bytes]  album name (UTF-8)
[1 byte]   song name length (K)
[K bytes]  song name (UTF-8)
[3 bytes]  duration in ms (big-endian, lower 24 bits)
[1 byte]   playback state (see table)
[2 bytes]  playback rate × 10 (short, big-endian; e.g. 1.0× = 0x000A)
[3 bytes]  elapsed time in ms (big-endian, lower 24 bits)
```

**Playback state byte values:**

| Byte   | Meaning              |
|--------|----------------------|
| `0x01` | Paused               |
| `0x02` | Playing              |
| `0x03` | Rewinding            |
| `0x04` | FastForwarding       |
| `0xFF` | MediaDeviceUnavailable |
| `0x00` | Unknown / other      |

**Note:** Strings are length-prefixed (1-byte length + bytes). Max 64 UTF-8 bytes each, truncated if longer. Duration and elapsed are big-endian 24-bit milliseconds (upper byte of the 32-bit int is dropped).

---

## PhoneStateCombined Packet Format (0x013C)

Simple write, no DDT session required.

```
[2 bytes]  command code: 0x01 0x3C
[4 bytes]  state payload
```

Byte layout of the 4-byte payload:

```
byte0: (battery% & 0x7F) | (charging << 7)
byte1: (phoneVolume% & 0x7F) | (enablePhoneVolume << 7)   ← always set bit7=1
byte2: (mediaVolume% & 0x7F) | (enableMediaVolume << 7)    ← always set bit7=1
byte3: (signalLevel << 4) | (thermalState << 2) | (headsetConnected << 1) | enableOnHook
```

Signal level wire values:

| Level        | Wire byte |
|-------------|-----------|
| NoConnection | 0         |
| NoSignal     | 1         |
| Lv1          | 2         |
| Lv2          | 3         |
| Lv3          | 4         |
| Lv4          | 5         |

Thermal state wire values:

| State  | Wire byte |
|--------|-----------|
| Low    | 0         |
| Middle | 1         |
| High   | 2         |

**Critical:** `headsetConnected` lives in bit 1 of byte3. If this bit is 0, the CCU will not enable its music screen. Your app should set this bit whenever a media/headset session is active — hardcoding it to `true` works but means the CCU will consider a headset "connected" even when it isn't.

**Written to:** `simpleWriteUuid` = `B606C7F9-E5A1-4E75-B313-2A920054A8EB`

---

## BluetoothHeadsetStateResponse Packet Format (0x011C)

Simple write. The CCU only enables music display after receiving this with `connected=true`.

```
[2 bytes]  command code: 0x01 0x1C
[1 byte]   0x01 = connected, 0x00 = not connected
```

---

## DDT-to-Vehicle Session Protocol (for Music Metadata)

This is the transport layer used for the music metadata packet — a chunked, acknowledged transfer session layered on top of three dedicated GATT characteristics, distinct from the simple-write path used by smaller commands.

### Characteristics

| Role     | UUID                                     |
|----------|-------------------------------------------|
| Control  | `56851826-A090-4F6A-A5DF-D1776520F74A`  |
| Status   | `75AB2788-87F6-4F61-BA9C-6F85A12600D1`  |
| Data     | `E998AEAB-EA8F-4063-83AA-2017C1A44484`  |

### Session State Machine

```
None
  │ session start → READ status char
  ▼
CheckInactive
  │ status[0] == 0 (idle) → WRITE control: [reqId, len[1], len[2], len[3]]
  │ status[0] != 0 → Failure (channel busy)
  ▼
WaitActive
  │ NOTIFY status[0]==1 && status[1]==reqId → start Sending
  │ NOTIFY anything else → Failure
  ▼
Sending
  │ WRITE data chunk: [reqId, offset[1], offset[2], offset[3], ...data...]
  │ after each write:
  │   if (offset >= total) → WaitInactive
  │   else → READ status char (per-chunk ack)
  ▼
SendCheckActive (per-chunk only; skipped for single-chunk messages)
  │ READ status[0]==1 && status[1]==reqId → send next chunk
  │ else → Failure
  ▼
WaitInactive
  │ NOTIFY status[0]==0 && status[1]==0 → Success
  ▼
Success
```

Chunk size: `min(MTU − 3, 180) − 4` bytes of data per chunk. At MTU 183: 176 bytes of payload per chunk (total write = 180 bytes).

## Volume / Phone State Requirements

Volume is reported via `PhoneVolumeLevel` (0x011E) after a `PhoneVolumeControl` (0x011D) command. This is separate from music metadata.

The CCU asks `PhoneVolumeControllableRequest` (0x0129); your app should respond with `VolumeControllableResponse(true)` (0x012A). After that, the CCU may send `PhoneVolumeControl` commands; respond with `PhoneVolumeLevel` after executing them.

Volume is NOT required for music metadata display — it's independent. However, if `enableMediaVolume=0` in `PhoneStateCombined`, the CCU might not show the volume UI alongside the music screen.
