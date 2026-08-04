# Notifications

## Overview

The CCU display can show incoming message and call alerts. The phone sends notification data to the CCU over BLE using the `NotificationDataV2` command. The CCU also maintains a per-notification state via add/update/remove lifecycle commands. The actual content (title + body) is carried only in `NotificationDataV2`.

---

## Protocol Buffer Payload

`NotificationDataV2` carries a Protocol Buffers message. No proto library or codegen is required to implement this — the fields can be encoded manually using standard protobuf wire format.

| Field | Number | Wire type | Max length |
|-------|--------|-----------|------------|
| `notificationId` | 3 | varint (0) | — |
| `category` | 4 | varint (0) | — |
| `year` | 5 | varint (0) | — |
| `month` | 6 | varint (0) | — |
| `day` | 7 | varint (0) | — |
| `hour` | 8 | varint (0) | — |
| `minutes` | 9 | varint (0) | — |
| `second` | 10 | varint (0) | — |
| `title` | 11 | len-delimited (2) | 64 bytes UTF-8 |
| `subTitle` | 12 | len-delimited (2) | 64 bytes UTF-8 |
| `detail` | 13 | len-delimited (2) | 512 bytes UTF-8 |

**Tag byte** = `(fieldNumber << 3) | wireType`

| Field | Tag byte |
|-------|----------|
| notificationId | `0x18` |
| category | `0x20` |
| year | `0x28` |
| month | `0x30` |
| day | `0x38` |
| hour | `0x40` |
| minutes | `0x48` |
| second | `0x50` |
| title | `0x5A` |
| subTitle | `0x62` |
| detail | `0x6A` |

**Category values:**

| Value | Meaning |
|-------|---------|
| `0` | MESSAGE |
| `1` | CALL |

**Protobuf varint encoding** — for values 0–127 a single byte suffices. For larger values (e.g., the year) each group of 7 bits is written LSB-first with the MSB set as a continuation flag:

```
2025 → 0b11111101001
groups (7-bit, LSB first): 1101001 (0x69) | 0001111 (0x0F)
encoded: [0xE9, 0x0F]
```

**String fields** — tag byte, then varint length, then raw UTF-8 bytes (no null terminator).

**Recommended:** omit empty string fields (e.g. an unused `subTitle`) from the payload entirely rather than encoding an empty length-delimited field. This keeps the payload smaller and matches typical protobuf encoder behavior.

---

## NotificationDataV2 (cmd 0x012F, phone → CCU)

Proactively sent by the phone when a new notification arrives.

**Wire format:**
```
bytes[0..1]          = 0x01 0x2F          (command code, big-endian)
bytes[2..4]          = payloadLen          (3-byte big-endian, byte count of proto payload)
bytes[5..]           = proto payload       (serialized notification message)
```

Sent via DDT session (same framing as `BluetoothMusicMetaData`).

**Example — test notification:**
```
Proto fields:
  notificationId = 1   → [0x18, 0x01]
  category       = 0   → [0x20, 0x00]
  year           = 2025 → [0x28, 0xE9, 0x0F]
  month          = 6   → [0x30, 0x06]
  day            = 29  → [0x38, 0x1D]
  hour           = 0   → [0x40, 0x00]
  minutes        = 0   → [0x48, 0x00]
  second         = 0   → [0x50, 0x00]
  title          = "Test" → [0x5A, 0x04, 0x54, 0x65, 0x73, 0x74]
  subTitle       = ""  → (omitted when empty)
  detail         = "It works!" → [0x6A, 0x09, 0x49, 0x74, ...]
```

(The `title` bytes above are illustrative — substitute the UTF-8 bytes of your own string, prefixed with tag `0x5A` and the varint length.)

---

## Notification Lifecycle Commands

These manage a per-notification record on the CCU (needed for some CCU firmware versions). Content is not re-sent — only IDs. All three outgoing commands use **Simple transport** (written directly to `simpleWriteUuid`, no DDT handshake).

### AddNotification (cmd 0x0010, phone → CCU)

Registers a new notification ID on the CCU. Send this before sending `NotificationDataV2` for a new notification.

**Wire format (7 bytes):**
```
bytes[0..1] = 0x00 0x10        (command)
bytes[2]    = requestId        (1-byte rolling counter)
bytes[3..6] = notificationId  (4-byte big-endian int)
```

### UpdateNotification (cmd 0x0011, phone → CCU)

Signals that an existing notification's content changed. Send this before re-sending `NotificationDataV2` with the updated content.

**Wire format (7 bytes):**
```
bytes[0..1] = 0x00 0x11
bytes[2]    = requestId
bytes[3..6] = notificationId
```

### RemoveNotification (cmd 0x0012, phone → CCU)

Clears a notification from the CCU display.

**Wire format (7 bytes):**
```
bytes[0..1] = 0x00 0x12
bytes[2]    = requestId
bytes[3..6] = notificationId
```

### NotificationRemovedNotification (cmd 0x0013, CCU → phone)

The CCU reports that the rider dismissed a notification from the meter display. Total packet length is always 6 bytes — treat any other length as a parse error and discard the packet.

**Wire format (6 bytes):**
```
bytes[0..1] = 0x00 0x13
bytes[2..5] = notificationId  (4-byte big-endian int)
```

No `requestId` — this is an unsolicited push from the CCU. The phone should respond by calling `RemoveNotification` to acknowledge the dismissal.

---

## NotificationData (cmd 0x0014, legacy)

An older, simpler format (command 20 decimal / 0x0014). Wire layout:

```
bytes[0..1]  = 0x00 0x14          (command)
bytes[2..5]  = notificationId     (4-byte big-endian)
bytes[6..7]  = titleLen
bytes[8..]   = title (UTF-8)
bytes[+2]    = subTitleLen
bytes[+2..]  = subTitle (UTF-8)
```

Only used by older CCU firmware. Prefer `NotificationDataV2`.

---

## NotificationCategory (cmd 0x0128, phone → CCU)

Tells the CCU which notification categories the user has enabled. Sent once after connection.

**Wire format:**
```
bytes[0..1] = 0x01 0x28
bytes[2]    = requestId
bytes[3]    = bitfield (one bit per category)
```

Bit positions:
```
bit 0 = other
bit 1 = incomingCall
bit 2 = missedCall
bit 3 = voicemail
bit 4 = social
bit 5 = schedule
bit 6 = email
bit 7 = news
(byte 2) bit 0 = healthAndFitness
         bit 1 = businessAndFinance
         bit 2 = location
         bit 3 = entertainment
```

---

## Full Send Sequence

```
[connection established, phone state sent]

Phone: NotificationDataV2(id=1, category=MESSAGE, title="Test", detail="It works!") (0x012F)

[new notification arrives]
Phone: AddNotification(id=42) (0x0010)
Phone: NotificationDataV2(id=42, category=MESSAGE, title="WhatsApp", detail="Hey!") (0x012F)

[notification updated]
Phone: UpdateNotification(id=42) (0x0011)
Phone: NotificationDataV2(id=42, ..., detail="Hey! (edited)") (0x012F)

[notification dismissed on phone]
Phone: RemoveNotification(id=42) (0x0012)

[rider dismisses on meter]
CCU: NotificationRemovedNotification(id=42) (0x0013)
Phone: RemoveNotification(id=42) (0x0012)
```

---

## Implementation Notes

- Leave `subTitle` empty (`""`) unless you have a specific value for it. Empty string fields should be **omitted** from the proto payload entirely rather than encoded as a zero-length field.
- `detail` is truncated to 512 UTF-8 bytes; `title` and `subTitle` to 64 bytes. Enforce these limits before serialization.
- `NotificationDataV2` uses **DDT transport**: `[cmd 2B][payloadLen 3B][proto payload]`. The three lifecycle commands (add/update/remove) use **Simple transport**: written directly to the simple-write characteristic, no DDT handshake.
- Recommended: skip notifications whose timestamp predates the BLE connection, to avoid flooding the CCU with stale notifications at startup.
- On Samsung devices, WhatsApp VoIP calls arrive as `category=call` notifications and are routed through the call-state path instead of the message notification path.
