# Implementation Guide — Connecting to the CCU

This is a step-by-step guide for implementing CCU communication from scratch in your own client app.

---

## Prerequisites

- Known CCUID (14-char string, e.g., from device name or stored from prior pairing)
- Known passkey (byte array, shared secret)
- Platform BLE API (CoreBluetooth on iOS, BluetoothGatt on Android)
- Location/Bluetooth permissions granted

---

## Step 1 — Scan for the CCU

Start a BLE scan. Filter results in software:

```
advertisement.localName starts with "YCCU_"  
  AND  length == 19 characters
```

The CCUID is the last 14 characters of the device name.

**Important:** Scan uses no service UUID filter. All BLE advertisements are inspected.

Stop scanning once the target device is found (match on CCUID).

---

## Step 2 — Connect GATT

Connect to the discovered peripheral with **direct connection** (not background/cached reconnect). Use BLE transport only.

If the connection doesn't succeed within 5 seconds, treat it as a timeout failure and retry or alert the user.

**If the CCU is busy with the OEM app (or any other host):** The connection will fail because the CCU stops advertising while paired to another host. There is no way around this from the phone side — the CCU must not have an active connection.

---

## Step 3 — Discover Services

After the GATT connection is established, trigger service/characteristic discovery. Wait for the discovery callback before proceeding.

Confirm that the primary service UUID is present:
```
AFA2CDF4-ECCF-46A7-A5EA-9DA428C0157A
```

If the service is not found, disconnect and report an incompatible device.

---

## Step 4 — Negotiate MTU

Request MTU of **512 bytes**. The actual negotiated MTU may be lower depending on the CCU and OS. The effective payload per write packet is `MTU - 3` bytes.

Wait for the MTU callback before proceeding.

---

## Step 5 — Enable Notifications / Indications

For each characteristic listed below, write the CCCD descriptor to enable it. Serialize these writes (do not issue multiple descriptor writes concurrently — GATT does not support it).

**Enable Notifications** (write `0x0100` to CCCD):
```
9C810D26-B605-4306-8C1C-755A1BA3066C  (main RX)
75AB2788-87F6-4F61-BA9C-6F85A12600D1  (DDT request)
77808451-E42F-4F6B-83ED-3AB44AA3D16A  (DDT hi-bandwidth)
347456EE-A588-4DD5-BD98-62BE3672E27A  (DDT large)
E974B9A1-FA31-43C6-91AE-09931F3F6EF1  (DDT v2)
```

**Enable Indications** (write `0x0200` to CCCD):
```
56DC2D58-CBE1-4BA9-A104-E9B8EB4DA95C  (DDT v3)
```

CCCD UUID (standard): `00002902-0000-1000-8000-00805f9b34fb`

After all descriptors are written successfully → the connection is **ready**.

---

## Step 6 — Authenticate

Auth uses the **DDT-to-Vehicle session** (same path as music metadata). Build the logical message:

```
[0x01][0x01]          ← command code 257 (AuthenticationV2Request), big-endian
[length: 3 bytes]     ← big-endian lower-24-bits of payload byte count (= 20)
[ccuId: 14 bytes]     ← ASCII bytes of CCUID string
[passKey: 6 bytes]    ← ASCII bytes of passkey string
```

Total logical message: 2 + 3 + 14 + 6 = 25 bytes.

Send this via the DDT-to-Vehicle protocol (announce length to `75AB2788-...`, then send fragment(s) to `B606C7F9-...`). At 25 bytes this will always be a single fragment.

Wait for a notification on the main RX characteristic. Parse the incoming bytes:
```
bytes[0..1] == 0x0102 (AuthenticationV2Response)
bytes[2]    == 0x01   → success
bytes[2]    == 0x00   → failure
```

If auth fails, disconnect. Do not retry automatically.

---

## Step 7 — Session Setup

After successful auth, send these in order:

All standard (non-DDT) commands share this structure:
```
[2 bytes]  command code (big-endian)
[1 byte]   request ID (increment from 0, wraps at 255)
[N bytes]  command-specific payload
```

1. **ConnectAppId** (cmd 0x0002): Identify the app to the CCU.
   ```
   00 02        ← command code
   XX           ← request ID (your counter, e.g. 0x01)
   00 00 12 34  ← app ID (big-endian int) — an application identifier value; 0x1234 is an example value used during testing
   ```

2. **VehicleInformationSubscribeInterval** (cmd 0x0006): Start telemetry stream.
   ```
   00 06        ← command code
   XX           ← request ID
   03 E8        ← interval = 1000 ms as big-endian short (use 200 ms = 0x00C8 for faster updates)
   ```

3. Respond to **LanguageTypeRequest** (cmd 0x000B) when received by sending **LanguageTypeResponse** (cmd 0x000C):
   ```
   00 0C        ← command code
   XX           ← request ID
   00           ← language byte: 0x00 = English (see data-models.md for full table)
   ```

---

## Step 8 — Receive Vehicle Data

Listen for notifications on the main RX characteristic. When `bytes[0..1] == 0x0007`:

**VehicleInformation wire format (10 bytes total):**
```
bytes[0..1] : 0x0007 (command code)
bytes[2..3] : speed (short, big-endian)
bytes[4..5] : revs-based speed (short, big-endian)
bytes[6..7] : gear-based speed (short, big-endian)
bytes[8]    : ignition state (0=OFF, 1=ON; other values = parse error)
bytes[9]    : speed unit (0=KMH, 1=MPH)
```

---

## Step 9 — Keepalive

Send `AliveCheckRequest` (cmd 0x0135) periodically. The CCU does not run this timer for you — your app is responsible for scheduling it. A reasonable interval is 30 seconds.

```
01 35        ← command code
XX           ← request ID
             (no additional payload)
```

Expect `AliveCheckResponse` (cmd 0x0136) as a notification on the main RX characteristic:
```
bytes[0..1] == 0x01 0x36
```

If no response within 10 seconds, treat the connection as dropped and reconnect.

---

## Reconnection Strategy

On disconnect:
1. Clean up all GATT resources
2. Wait a brief delay (1–2 seconds)
3. Re-scan for the device (do not assume BLE address is cached correctly)
4. Restart from Step 2

---

## Foreground Service (background survival)

BLE connections are killed by Android when the screen locks unless the app runs a foreground service. A recommended structure: a foreground service holds the BLE connection controller and its coroutine/async scope, and the main activity binds to that service to get a reference to the controller — the activity should not own the connection directly.

```
startService(...)   ← keeps process alive
bindService(...)     ← get controller reference for UI
```

The service should show a persistent notification with a "Disconnect" action. When the activity is destroyed (e.g. rotated), the service and connection survive. The service should stop itself when a disconnect action is received from the notification, or when its owning activity is permanently destroyed.

---

## Handling Incoming Commands

After `Initialized` state, the CCU sends several unsolicited requests. These must all be answered or the connection degrades:

| Command | Code | Action |
|---|---|---|
| `LanguageTypeRequest` | 0x000B | Reply with `LanguageTypeResponse(English=0x00)` |
| `WeatherRequest` | 0x0003 | Reply with `WeatherInfo` for at least Current slot |
| `AliveCheckRequest` | 0x0135 | Reply with `AliveCheckResponse` |
| `VolumeControllableRequest` | 0x0129 | Reply with `VolumeControllableResponse(true)` + send current `PhoneVolumeLevel` |

Your command dispatcher should handle all of these automatically once the connection is established.

---

## GATT Write Type

All writes to the Write characteristic use **Write With Response** (GATT `WRITE_TYPE_DEFAULT`). Wait for the write-confirmation callback (e.g. `onCharacteristicWrite`) before queuing the next write. Do not use Write Without Response — it will break the flow control.

## Encoding Notes

**CCUID:** 14-character ASCII string → 14 raw ASCII bytes  
**Passkey:** 6-character ASCII string → 6 raw ASCII bytes (e.g. `"ABC123"` → `[0x41, 0x42, 0x43, 0x31, 0x32, 0x33]`)  
**Request ID:** 1 byte, 0–255, increment per command, wraps around. Included in every command payload after the command code.
