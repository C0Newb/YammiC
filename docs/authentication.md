# Authentication

Authentication happens **after** the GATT connection reaches `Initialized` state (all notifications enabled). It is a custom application-layer handshake — separate from Bluetooth pairing.

## Sequence

```
Phone                              CCU
  │                                 │
  │── AuthenticationV2Request ────► │  cmd 0x0101
  │   [ccuId bytes][passKey bytes]  │
  │                                 │
  │◄── AuthenticationV2Response ─── │  cmd 0x0102
  │    [success: 0x01 or 0x00]      │
  │                                 │
```

## AuthenticationV2Request (cmd 0x0101)

Sent via the **DDT-to-Vehicle session**, not a bare GATT write. See `protocol.md` for the DDT phone→CCU framing.

**Logical message format (before DDT wrapping):**

```
Bytes 0–1   : Command code  →  0x01 0x01  (short, big-endian)
Bytes 2–3   : Payload length (lower 24 bits of int, 3 bytes big-endian)
Bytes next  : ccuId bytes   (14 ASCII bytes, e.g. "ABCDEF12345678")
Bytes next  : passKey bytes (6 ASCII bytes, e.g. "ABC123")
```

No delimiter between ccuId and passKey — the CCU expects exactly 14 + 6 = 20 bytes of credential data.

## AuthenticationV2Response (cmd 0x0102)

Received as a notification on the main RX characteristic.

**Wire format:**

```
Byte 0 : 0x01  (high byte of command code 0x0102)
Byte 1 : 0x02  (low byte of command code 0x0102)
Byte 2 : 0x01 = success,  0x00 = failure
```

## CCUID

The CCUID is the **14-character suffix** of the device's BLE advertisement name:

```
Device name:  "YCCU_ABCDEF12345678"
                      ^^^^^^^^^^^^^^
                      CCUID (14 chars)
```

It is converted to bytes before being sent. The exact encoding (ASCII vs. hex-decoded vs. raw) is unconfirmed; the bytes sent are believed to be the UTF-8/ASCII bytes of the CCUID string.

## Passkey

The passkey is a **6-character ASCII string** (e.g. `"ABC123"`), sent as its 6 raw ASCII bytes. It is factory-programmed into the CCU hardware and never changes. It is sent in plaintext over the BLE connection — there is no encryption layer in this handshake.

See `pairing.md` for how to obtain the passkey.

## Legacy Authentication (cmd 0x0015)

Command 21 (`Authentication`) is an older auth path that returns a response on the RX characteristic and is handled separately from `AuthenticationV2`. Avoid this path; `AuthenticationV2` is the current standard.

## CcuidRequest / CcuidResponse (cmd 0x0130 / 0x0131)

Commands 304 and 305 allow the phone to **request the CCUID from the CCU** rather than extracting it from the BLE advertisement name. Useful for double-checking identity after connect. The response payload is a raw byte array containing the CCUID.

## Recommended Post-Auth Steps

After a successful auth response, a client typically:

1. Sends `ConnectAppId` (cmd 0x0002) — identifies the app to the CCU
2. Sends `LanguageTypeResponse` (cmd 0x000C) — sets display language
3. Sends `VehicleInformationSubscribeInterval` (cmd 0x0006) — starts telemetry
4. Begins polling/listening for `VehicleInformation` (cmd 0x0007) notifications
