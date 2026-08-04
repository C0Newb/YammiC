# MVP: Music and Weather

**Yes — everything needed is documented.** This file gives you the exact wire formats for both features so you can implement without referencing the SDK source.

---

## Prerequisites (before music/weather work)

Complete the full connection + auth flow first — see `implementation-guide.md`. Music and weather only work once the connection is in the `Initialized` state and auth succeeds.

After auth, send:
1. `ConnectAppId` (cmd 0x0002)
2. Reply to `LanguageTypeRequest` (cmd 0x000B) when it arrives

---

## Music Metadata

**Command:** `BluetoothMusicMetaData` = 0x0116 (278)  
**Direction:** Phone → CCU  
**When to send:** Any time track info changes (new song, play/pause, seek)  
**Transport:** DDT-to-Vehicle session (fragmented if needed — see note below)

### Payload format

```
[2 bytes]  command code:  0x01 0x16
[3 bytes]  payload length (big-endian, lower 24 bits of a 32-bit int)
─────────────────────────────────────
[1 byte]   artist name length (N)
[N bytes]  artist name (UTF-8, max 64 bytes)
[1 byte]   album name length (M)
[M bytes]  album name (UTF-8, max 64 bytes)
[1 byte]   song name length (K)
[K bytes]  song name (UTF-8, max 64 bytes)
[3 bytes]  duration in ms (big-endian, lower 24 bits — supports up to ~4.6 hours)
[1 byte]   playback state (see table below)
[2 bytes]  playback rate × 10 (short, big-endian — e.g. 1.0× = 10)
[3 bytes]  elapsed time in ms (big-endian, lower 24 bits)
```

**Playback state values:**

| Byte | Meaning |
|------|---------|
| 0x01 | Paused |
| 0x02 | Playing |
| 0x03 | Rewinding |
| 0x04 | FastForwarding |
| 0xFF | MediaDeviceUnavailable |
| 0x00 | Unknown / other |

### Example — "Now Playing" packet

Track: "Test Song" by "Test Artist" on "Test Album", playing at 1×, 3:30 duration, 0:15 elapsed.

```
duration_ms  = 210000  (3:30 = 3×60×1000 + 30×1000)
elapsed_ms   = 15000   (0:15)
artist_bytes = "Test Artist" → [0x54 0x65 0x73 0x74 0x20 0x41 0x72 0x74 0x69 0x73 0x74]  (11 bytes)
album_bytes  = "Test Album"  → (10 bytes)
song_bytes   = "Test Song"   → (9 bytes)

payload_length = 1+11 + 1+10 + 1+9 + 3 + 1 + 2 + 3 = 42

Full packet:
01 16           ← command code
00 00 2A        ← payload length = 42
0B              ← artist length = 11
54 65 73 74 20 41 72 74 69 73 74  ← "Test Artist"
0A              ← album length = 10
54 65 73 74 20 41 6C 62 75 6D      ← "Test Album"
09              ← song length = 9
54 65 73 74 20 53 6F 6E 67         ← "Test Song"
03 34 50        ← duration = 210000 = 0x033450
02              ← playing
00 0A           ← playback rate = 10 (1.0×)
00 3A 98        ← elapsed = 15000 = 0x003A98
```

> **Note:** The payload is wrapped by the DDT-to-Vehicle session. If total bytes fit in one MTU packet (≤180 bytes minus DDT header), it sends in one write. For short track names this will always be a single packet. You don't need to implement DDT fragmentation manually if you implement the session-layer framing correctly.

### DDT-to-Vehicle framing (required for music)

Music metadata is sent via the DDT-to-Vehicle session (a large-message transfer channel), **not** a bare GATT write. The protocol is a handshake driven by CCU status notifications.

> **Note:** The CCU→phone DDT format (sequence + index + total) is **different** from the phone→CCU DDT format described here. Do not confuse them.

#### Step 1 — Announce total size

Before sending data, write the total logical message length to the **DDT request characteristic** (`75AB2788-87F6-4F61-BA9C-6F85A12600D1`). Wait for a status notification back from the CCU before proceeding.

#### Step 2 — Send fragments

Each fragment is written to the **Write characteristic** (`B606C7F9-...`):

```
[1 byte]   Request ID  (assigned when the send was initiated)
[3 bytes]  Byte offset into the full logical message (big-endian, 24-bit)
           byte[1] = (offset >> 16) & 0xFF
           byte[2] = (offset >>  8) & 0xFF
           byte[3] =  offset        & 0xFF
[N bytes]  Data chunk (up to MTU − 7 bytes per fragment)
```

The CCU sends a status notification after each fragment. The next fragment is only sent after receiving that notification. Offset starts at 0 and increments by the chunk length each time.

#### Step 3 — Completion

When `offset >= total message length`, the transfer is complete.

#### Fragment size

`MTU − 7` bytes of payload per fragment (4-byte DDT header + up to 3 bytes of BLE ATT overhead). At MTU 180: max 173 bytes of payload per fragment. For most song names the entire music packet fits in one fragment.

---

## Weather

**When:** The CCU sends a `WeatherRequest` (cmd 0x0003). The phone must respond.  
**Response command:** `WeatherInfo` = 0x0005 (5)  
**Direction:** Phone → CCU  
**Transport:** Standard single-packet GATT write (not DDT)

### Handling the request

Listen for notifications on the main RX characteristic where bytes[0..1] == 0x0003.

```
WeatherRequest payload:
  bytes[2] = 0x01, length=3  → Current location weather
  bytes[2] = 0x02, length=11 → Point weather (includes lat/lng at bytes[3..10])
```

For the MVP, ignore the request type and always reply with static current weather.

### WeatherInfo packet format (cmd 0x0005)

```
[2 bytes]  command code:  0x00 0x05
[1 byte]   request ID    (copy from somewhere; use 0x00 for MVP)
[1 byte]   time slot     (see table below)
[1 byte]   weather type  (see table below)
[2 bytes]  temperature × 10 (short, big-endian — e.g. 25°C = 250 = 0x00FA)
[2 bytes]  max temperature × 10
[2 bytes]  min temperature × 10
[1 byte]   precipitation % ÷ 5 (0–20; clamp input to 0–100 before dividing)
```

**Total: 12 bytes**

**Time slot values (WeatherTime enum ordinal):**

| Byte | Meaning |
|------|---------|
| 0x00 | Current |
| 0x01 | After 1 hour |
| 0x02 | After 2 hours |
| 0x03 | After 3 hours |
| 0x04 | After 1 day |
| 0x05 | After 2 days |
| 0x06 | After 3 days |
| 0x07 | After 4 days |
| 0x08 | After 5 days |
| 0x09 | After 6 days |

**Weather type values (Weather enum ordinal):**

| Byte | Weather |
|------|---------|
| 0x00 | Sunny |
| 0x01 | PartlyCloudyDay |
| 0x02 | CloudyDay |
| 0x03 | Rain |
| 0x04 | Snow |
| 0x05 | Wind |
| 0x06 | RainStormMix |
| 0x07 | RainSnowMix |
| 0x08 | Fog |
| 0x09 | Smog |
| 0x0A | Hail |
| 0x0B | ScatteredShowersDay |
| 0x0C | ScatteredStormDay |
| 0x0D | NoData |
| 0x0E | RainLight |
| 0x0F | RainHeavy |
| 0x10 | SnowLight |
| 0x11 | SnowHeavy |
| 0x12 | RainSnowMixLight |
| 0x13 | RainSnowMixHeavy |
| 0x14 | Cloudy |
| 0x15 | Dust |
| 0x16 | Tornado |
| 0x17 | Clear |
| 0x18 | PartlyCloudyNight |
| 0x19 | CloudyNight |
| 0x1A | ScatteredShowersNight |
| 0x1B | ScatteredStormNight |

### Example — Static sunny weather response

25°C / high 28°C / low 20°C / 10% rain, current slot:

```
00 05        ← command code (WeatherInfo)
00           ← request ID (0 for MVP)
00           ← time slot: Current
00           ← weather: Sunny
00 FA        ← temperature: 25°C × 10 = 250 = 0x00FA
01 18        ← max temp: 28°C × 10 = 280 = 0x0118
00 C8        ← min temp: 20°C × 10 = 200 = 0x00C8
02           ← precipitation: 10% ÷ 5 = 2
```

Write this to the **Write characteristic** (`B606C7F9-...`) directly — no DDT session needed.

---

## MVP Event Loop

```
On connection Initialized:
  → send ConnectAppId (0x0002)

On receive LanguageTypeRequest (0x000B):
  → send LanguageTypeResponse (0x000C) with your language byte

On receive WeatherRequest (0x0003):
  → send WeatherInfo (0x0005) with static weather data

On music track change:
  → send BluetoothMusicMetaData (0x0116) via DDT session

On play/pause/seek:
  → resend BluetoothMusicMetaData with updated state/elapsed time
```

---

## What Needs the Passkey

The passkey is a **6-character ASCII string** (e.g. `"ABC123"`), sent as its 6 raw ASCII bytes. See `pairing.md` for how to obtain it.
