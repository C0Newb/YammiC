# Navigation Implementation — MVP

**Goal:** Display a static image on the bike's navigation screen from your app, with zoom in/out via the bike's controls. No OEM navigation app required.

---

## Correction: MPN Photo Frame is NOT the navigation display

The bike has two completely independent display systems:

| | **MPN (BLE GATT)** | **NaviLite (Bluetooth Classic)** |
|---|---|---|
| Transport | BLE GATT | RFCOMM socket |
| Used for | Photo Frame, Push Notification, Pit Board | **Navigation map display** |
| Who uses it | the OEM app | the OEM navigation app |
| Map images | ✗ Not supported | ✓ This is the map screen |

**The navigation map screen on the bike only accepts images via the NaviLite protocol over Bluetooth Classic RFCOMM.** MPN Photo Frame is a separate panel (photo slideshow / notifications), not the navigation display.

To show a map image on the navigation screen without the OEM navigation app, you must **implement the NaviLite protocol** in your app.

---

## NaviLite Transport

- **Protocol:** Bluetooth Classic RFCOMM (not BLE)
- **UUID:** `00007220-0000-1000-8000-00805F9B34FB`
- **Who connects:** The **bike initiates** the connection to this UUID. Your app must open an RFCOMM server socket and wait.
- **Who speaks first:** The **bike sends the first message** (ESN identification) immediately after connecting.

```java
// Open RFCOMM server, bike will connect to us
BluetoothServerSocket server = bluetoothAdapter.listenUsingRfcommWithServiceRecord(
    "NaviLite", UUID.fromString("00007220-0000-1000-8000-00805F9B34FB")
);
BluetoothSocket socket = server.accept(); // bike connects here
```

---

## NaviLite Frame Format

Every message (in both directions) is wrapped in a NaviLite frame.

```
Offset  Size  Field
──────  ────  ─────────────────────────────────────────────────
0–3     4     Magic code: "nAl@"  (0x6E 0x41 0x6C 0x40)
4       1     Version: 0x01
5       1     Frame type (see below)
6       1     Service type (message ID)
7       1     Reserved / flags
8–11    4     Payload size (Big-Endian int)
12      1     Payload data type: 0=VALUE, 1=POINTER
──────  ────  ─────────────────────────────────────────────────
13+     N     Payload (N = payload size)
13+N    4     Checksum (CRC, see below)
```

**Total header size: 13 bytes** (12-byte fixed header + 1-byte data type field)

### Frame Types

| Value | Name | Direction |
|-------|------|-----------|
| 1 | MOBILE_REQUEST | App → Bike |
| 2 | MOBILE_RESPONSE | App → Bike |
| 3 | MOBILE_UPDATE | App → Bike (streaming data) |
| 4 | MCU_REQUEST | Bike → App |
| 5 | MCU_RESPONSE | Bike → App |
| 6 | MCU_UPDATE | Bike → App (streaming data) |

### Service Types — Complete List

**App → Bike:**

| Hex | Dec | Name | Notes |
|-----|-----|------|-------|
| 0x00 | 0 | IMAGE_FRAME_UPDATE | Live map JPEG frame |
| 0x01 | 1 | ETA_UPDATE | Estimated time to destination |
| 0x02 | 2 | NAVIGATION_STATUS_UPDATE | Nav on/off flag |
| 0x03 | 3 | CUR_ROAD_NAME_UPDATE | Current road name string |
| 0x04 | 4 | NEXT_TURN_DIST_UPDATE | Distance + instruction to next turn |
| 0x05 | 5 | TBT_LIST_UPDATE | Full turn-by-turn list |
| 0x06 | 6 | ACTIVE_TURN_LIST_UPDATE | Active turn list |
| 0x07 | 7 | FAV_POI_LIST_UPDATE | Favourite POIs |
| 0x08 | 8 | GAS_POI_LIST_UPDATE | Nearby petrol stations |
| 0x09 | 9 | NAVI_EVENT_TEXT_UPDATE | Alert text (speed camera etc.) |
| 0x0A | 10 | HOME_SETTING_UPDATE | Saved home address |
| 0x0B | 11 | OFFICE_SETTING_UPDATE | Saved office address |
| 0x0C | 12 | APP_SETTING_UPDATE | App settings |
| 0x0D | 13 | GPS_STATUS_UPDATE | GPS fix quality |
| 0x0E | 14 | MAP_ZOOM_LEVEL_UPDATE | Current zoom level |
| 0x0F | 15 | ROUTE_CALC_PROGRESS_UPDATE | Route calculation progress (0–100%) |
| 0x10 | 16 | BT_THROUGHPUT_TIMEOUT_UPDATE | Throughput mode change notification |
| 0x11 | 17 | SPEED_LIMIT_UPDATE | Speed limit on current road |
| 0x12 | 18 | VIA_POINT_COUNT_UPDATE | Waypoint count |
| 0x13 | 19 | NAVIGATION_INFO_UPDATE | Speed, road, heading |
| 0x14 | 20 | IMAGE_STOPPED_UPDATE | Stop image stream |
| 0x1F | 31 | DAY_NIGHT_MODE_UPDATE | Day/night map mode |
| 0x20 | 32 | DIALOG_PROMPT_UPDATE | Show dialog on bike display |
| 0x21 | 33 | AUTH_REQUEST | Auth handshake step 1 |
| 0x43 | 67 | AUTH_REQUEST_SEC_DATA_ACK | Auth handshake step 4 |
| 0x51 | 81 | MCU_ESN_UPDATE_ACK | ACK for bike's ESN |

**Bike → App:**

| Hex | Dec | Name | Notes |
|-----|-----|------|-------|
| 0x30 | 48 | APP_START_ROUTE_REQUEST | User selected a route |
| 0x31 | 49 | APP_STOP_ROUTE_REQUEST | User cancelled navigation |
| 0x32 | 50 | APP_SKIP_NEXT_WAYPOINT_REQUEST | Skip waypoint |
| 0x33 | 51 | APP_MAP_ZOOM_IN_REQUEST | **Zoom in** |
| 0x34 | 52 | APP_MAP_ZOOM_OUT_REQUEST | **Zoom out** |
| 0x35 | 53 | APP_GO_HOME_REQUEST | Route to home |
| 0x36 | 54 | APP_GO_OFFICE_REQUEST | Route to office |
| 0x37 | 55 | APP_START_CONTENT_UPDATE_REQUEST | Bike ready for images |
| 0x38 | 56 | APP_STOP_CONTENT_UPDATE_REQUEST | Bike exiting nav mode |
| 0x41 | 65 | AUTH_REQUEST_ACK | Auth handshake step 2 |
| 0x42 | 66 | AUTH_REQUEST_SEC_DATA / MCU_ESN_UPDATE | Auth step 3 / ESN |
| 0x45 | 69 | MCU_DIALOG_USER_SELECT_UPDATE | User pressed dialog button |
| 0x46 | 70 | MCU_SYSINFO_UPDATE | Bike system info |
| 0x50 | 80 | IMAGE_FRAME_UPDATE_ACK | ACK for received image frame |

---

## Full Connection & Auth Sequence

The bike is always the master. It initiates and controls the session.

```
1.  Bike connects via RFCOMM to UUID 00007220-0000-1000-8000-00805F9B34FB
    └─ Your app must be listening (server socket accept)

2.  BIKE → APP:  MCU_ESN_UPDATE (0x42)
    └─ Payload: ESN string (bike's serial number)

3.  APP → BIKE:  MCU_ESN_UPDATE_ACK (0x51)
    └─ Payload: bool — 1 if ESN accepted, 0 if rejected

4.  APP → BIKE:  AUTH_REQUEST (0x21)
    └─ Payload: appVersion (2-byte short) + extraData (4-byte int)
    └─ A known-working value is appVersion = 1861

5.  BIKE → APP:  AUTH_REQUEST_ACK (0x41)
    └─ Payload: 4-byte int — 1 = auth OK, 0 = rejected

6.  BIKE → APP:  AUTH_REQUEST_SEC_DATA (0x42)
    └─ Payload: lcSwPartNumber (string) + seederKeyData (byte[])
    └─ ⚠️  Security challenge — see note below

7.  APP → BIKE:  AUTH_REQUEST_SEC_DATA_ACK (0x43)
    └─ Payload: seederKey (byte[]) — response to challenge

    ── AUTH COMPLETE ──

8.  BIKE → APP:  APP_START_CONTENT_UPDATE_REQUEST (0x37)
    └─ Bike user activated navigation mode
    └─ Payload: content type int

9.  APP → BIKE:  NAVIGATION_STATUS_UPDATE (0x02)
    └─ Payload: 1 byte — 0x01 = navigation active

10. APP → BIKE:  IMAGE_FRAME_UPDATE (0x00)  [repeat]
    └─ Image frame (see format below)

11. BIKE → APP:  IMAGE_FRAME_UPDATE_ACK (0x50)
    └─ Payload: 4-byte sequence number ACK

12. Repeat steps 10–11 for each frame

13. On zoom:
    BIKE → APP:  APP_MAP_ZOOM_IN_REQUEST (0x33)  or  APP_MAP_ZOOM_OUT_REQUEST (0x34)
    └─ Payload: empty
    └─ Your app re-renders image at new zoom, sends new IMAGE_FRAME_UPDATE

14. BIKE → APP:  APP_STOP_CONTENT_UPDATE_REQUEST (0x38)
    └─ User exited navigation mode on bike

15. APP → BIKE:  IMAGE_STOPPED_UPDATE (0x14)
    └─ Confirm stream stopped
```

---

## Auth Challenge

The security challenge in steps 6–7 uses **a simple XOR 0x0A transformation**, not real cryptography.

**Example reference implementation:**
```kotlin
// De-obfuscate the part number and nonce received from bike
fun deobfuscate(data: ByteArray): ByteArray = data.map { (it.toInt() xor 0x0A).toByte() }.toByteArray()
```

**Step 6 — Bike sends `AUTH_REQUEST_SEC_DATA` (0x42):**
```
payload:
  lcSwPartNumber   String   (obfuscated)
  seederKeyData    byte[]   (obfuscated nonce)
```

**Step 7 — App sends `AUTH_REQUEST_SEC_DATA_ACK` (0x43):**
```
payload:
  seederKey   byte[]   = XOR each byte of seederKeyData with 0x0A
```

That's it. No real crypto. The auth handshake is fully implementable.

---

## Image Frame Packet (0x00)

Sent as a MOBILE_UPDATE (frame type 3).

```
[NaviLite header: 13 bytes]
  magic:       "nAl@"
  version:     0x01
  frame_type:  0x03  (MOBILE_UPDATE)
  service:     0x00  (IMAGE_FRAME_UPDATE)
  reserved:    0x00
  payload_size: N+3  (image bytes + 3 bytes of prefix)
  data_type:   0x00  (VALUE)

[Payload:]
  offset 0:    Image type   1 byte
                 0x00 = IMAGE_NAVIGATION  ← use this
                 0x01 = IMAGE_JCV
                 0x02 = IMAGE_THROUGHPUT_TEST
                 0x03 = IMAGE_NAVIGATION_EXPANDED
                 0x04 = IMAGE_OTHER
  offset 1–2:  Sequence #  2 bytes (unsigned short, Big-Endian), 1–30000 then wrap
  offset 3+:   JPEG data   variable

[Checksum: 4 bytes]
  Custom CRC (see checksum section below)
  ⚠️  Must match or bike rejects the frame (STATUS_ERROR_CHECKSUM_FAIL = 7)
```

**Image format:** JPEG, target 80% quality. A resolution of 320×240 px is known to work; the display accepts similar resolutions as well.

---

## Image Frame ACK Packet (0x50)

Sent by bike after receiving each frame.

```
[NaviLite header: 13 bytes]
  frame_type:  0x05  (MCU_RESPONSE)
  service:     0x50  (IMAGE_FRAME_UPDATE_ACK)
  payload_size: 4

[Payload:]
  offset 0–3:  Sequence number  4-byte int (matches the 2-byte seq# sent in the frame)
```

Wait for this ACK before sending the next frame. A target of 1000–2000 ms per frame is reasonable; if the ACK takes longer than 4000 ms, the bike enters low-throughput mode and reduces frame rate.

---

## Zoom Command Packets (0x33 / 0x34)

These arrive from the bike with no payload — just the header.

```
[NaviLite header: 13 bytes]
  frame_type:  0x04  (MCU_REQUEST)
  service:     0x33  (ZOOM_IN)  or  0x34  (ZOOM_OUT)
  payload_size: 0
```

On receipt, re-render the image at the new zoom level and send a new IMAGE_FRAME_UPDATE.

---

## NaviLite Checksum

The checksum algorithm is **CRC-32/MPEG-2**.

```
Polynomial:  0x04C11DB7
Initial value: 0xFFFFFFFF
Input reflection:  false
Output reflection: false
Final XOR:   0x00000000
```

This is **not** Java's standard `java.util.zip.CRC32` (which uses 0xEDB88320 reflected). You need to implement CRC-32/MPEG-2 explicitly:

```kotlin
fun crc32mpeg2(data: ByteArray): Int {
    var crc = 0xFFFFFFFF.toInt()
    for (byte in data) {
        crc = crc xor (byte.toInt() shl 24)
        repeat(8) {
            crc = if (crc and 0x80000000.toInt() != 0) (crc shl 1) xor 0x04C11DB7.toInt()
                  else crc shl 1
        }
    }
    return crc
}
```

The checksum covers the **entire frame** (header + payload). Write the checksum as the last 4 bytes.

---

## Error Status Codes (bike rejects our frame)

| Code | Meaning |
|------|---------|
| 0 | OK |
| 1 | Frame too short (less than header size) |
| 2 | Magic code mismatch (not "nAl@") |
| 3 | Payload length field doesn't match actual data |
| 4 | Service type doesn't match frame type |
| 5 | Service type doesn't match payload |
| 6 | Payload content error |
| 7 | Checksum fail |

---

## Implementation Plan

Everything needed to implement the protocol is documented above. Your implementation will need:

- An RFCOMM socket layer that listens for the bike's incoming connection on the NaviLite UUID.
- A full auth state machine covering ESN → ACK → AUTH → SEC_DATA → ACK.
- The XOR de-obfuscation step for the security challenge.
- A frame parser with magic-byte detection and resync (so it can recover if the byte stream gets out of alignment).
- Image-frame reception and flow-control logic (waiting for ACKs, handling the low-throughput mode).

### Phase 1 — NaviLite connection + auth

```kotlin
// 1. Open RFCOMM server
val server = bluetoothAdapter.listenUsingInsecureRfcommWithServiceRecord(
    "NaviLite", UUID.fromString("00007220-0000-1000-8000-00805F9B34FB")
)
val socket = server.accept()

// 2. Receive ESN from bike (service type 0x42)
val esn = readFrame(socket.inputStream)

// 3. Send ESN ACK (0x51)
sendFrame(socket.outputStream, serviceType = 0x51, payload = byteArrayOf(1))

// 4. Send AUTH_REQUEST (0x21) with app version
sendFrame(socket.outputStream, serviceType = 0x21,
    payload = buildAuthRequest(appVersion = 1861, extraData = 0))

// 5. Receive AUTH_ACK (0x41)
val authAck = readFrame(socket.inputStream)

// 6. Receive SEC_DATA (0x42) — security challenge
val secData = readFrame(socket.inputStream)

// 7. Send SEC_DATA_ACK (0x43) — XOR 0x0A response
val seederKey = secData.payload.map { (it.toInt() xor 0x0A).toByte() }.toByteArray()
sendFrame(socket.outputStream, serviceType = 0x43, payload = seederKey)

// Auth complete. Bike will now send APP_START_CONTENT_UPDATE_REQUEST (0x37)
```

### Phase 2 — Send a static image

```kotlin
// Wait for bike to request content (0x37)
val startRequest = readFrame(socket.inputStream)

// Send NAVIGATION_STATUS_UPDATE (0x02) — nav is active
sendFrame(socket.outputStream, serviceType = 0x02, payload = byteArrayOf(1))

// Build and send image frame
val jpeg = compressBitmapToJpeg(myBitmap, quality = 80)
sendImageFrame(socket.outputStream, jpeg, seqNum = 1)

// Wait for IMAGE_FRAME_UPDATE_ACK (0x50) before sending next
val ack = readFrame(socket.inputStream)  // payload = 4-byte seq num
```

**`sendImageFrame`:**
```kotlin
fun sendImageFrame(out: OutputStream, jpeg: ByteArray, seqNum: Int) {
    val payload = ByteBuffer.allocate(3 + jpeg.size)
        .put(0x00.toByte())                        // image type: IMAGE_NAVIGATION
        .putShort(seqNum.toShort())                // 2-byte seq num, Big-Endian
        .put(jpeg)
        .array()
    sendFrame(out, frameType = 3, serviceType = 0x00, payload = payload)
}
```

**`sendFrame`:**
```kotlin
fun sendFrame(out: OutputStream, frameType: Byte = 3, serviceType: Byte, payload: ByteArray) {
    val header = ByteBuffer.allocate(13)
        .put("nAl@".toByteArray())                // magic
        .put(0x01)                                 // version
        .put(frameType)
        .put(serviceType)
        .put(0x00)                                 // reserved
        .putInt(payload.size)                      // payload size, Big-Endian
        .put(0x00)                                 // data type = VALUE
        .array()
    val body = header + payload
    val checksum = crc32mpeg2(body)
    out.write(body + ByteBuffer.allocate(4).putInt(checksum).array())
}
```

### Phase 3 — Zoom loop

```kotlin
// In your frame-reading loop:
when (frame.serviceType) {
    0x33.toByte() -> {  // ZOOM_IN
        currentZoom = (currentZoom * 1.5f).coerceAtMost(MAX_ZOOM)
        sendImageFrame(out, renderMap(currentZoom), seqNum++)
    }
    0x34.toByte() -> {  // ZOOM_OUT
        currentZoom = (currentZoom / 1.5f).coerceAtLeast(MIN_ZOOM)
        sendImageFrame(out, renderMap(currentZoom), seqNum++)
    }
    0x38.toByte() -> {  // STOP_CONTENT_UPDATE
        sendFrame(out, serviceType = 0x14, payload = byteArrayOf())  // IMAGE_STOPPED
        stopStreaming()
    }
}
```

### Phase 4 — Map integration

1. Use a maps SDK snapshot API or a static maps API to render a tile at the current zoom and GPS position
2. Resize to **480×240 px** (the bike's display resolution)
3. Compress to JPEG 80%, feed into the image frame sender
4. On GPS position change → re-render and re-send
5. On zoom command → adjust zoom level, re-render, re-send

---

## Protocol Summary

| Item | Value |
|---|---|
| Checksum algorithm | CRC-32/MPEG-2 (poly 0x04C11DB7, init 0xFFFFFFFF) |
| Auth challenge response | XOR each byte with 0x0A |
| Image dimensions | 480×240 px |
| RFCOMM socket type | Insecure RFCOMM |
| Frame type for images | 3 (MOBILE_UPDATE) |
