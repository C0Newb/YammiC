# Wire Protocol

## Standard GATT Message (phone → CCU)

All outgoing commands are written to the **Write characteristic** (`B606C7F9-...`) using **Write With Response**. Wait for the write to complete (the peripheral's write acknowledgment) before sending the next write.

### Single-packet format

```
┌──────────┬──────────┬──────────────────────────────────┐
│  2 bytes │  1 byte  │  N bytes                         │
│ cmd code │ req ID   │  command-specific payload        │
└──────────┴──────────┴──────────────────────────────────┘
  big-endian  0–255,
              cycles
```

- **Command code:** 2-byte big-endian short.
- **Request ID:** 1-byte counter (0–255, wraps). Embedded in every command payload immediately after the command code. Used to match write acknowledgments to commands.
- **No length prefix. No checksum.** The BLE link layer handles integrity.
- Max bytes per write: 180 (normal mode); 24 (v1 compatibility mode).

**Example — ConnectAppId (cmd 0x0002), app ID 4660:**
```
00 02  01  00 00 12 34
^cmd^  ^id ^^^^^id^^^^^
```

**Example — AuthenticationV2Request (cmd 0x0101) — sent via DDT, not a bare write:**
```
Logical message: 01 01  [ccuId 14 bytes]  [passKey 6 bytes]
DDT fragment:    [req_id]  [offset 3 bytes]  [data chunk]
```

### Message encoding

To build an outgoing message: write the 2-byte command code first, then append each payload segment in order (the request ID is included as the first byte of the payload by the caller). There is no additional framing beyond this concatenation.

---

## Standard GATT Message (CCU → phone)

Received as BLE notifications on the main RX characteristic (`9C810D26-...`).

```
┌──────────┬──────────────────────────────────┐
│  2 bytes │  N bytes                         │
│ cmd code │  response payload                │
└──────────┴──────────────────────────────────┘
  big-endian
```

The first 2 bytes of every notification are the command code; decode the remaining bytes as that command's payload.

---

## DDT Session (large data, fragmented)

DDT has **two separate fragment formats** depending on direction. Do not confuse them.

---

### DDT phone → CCU (e.g. music metadata)

A handshake protocol triggered by CCU status notifications.

**Step 1 — Announce:** Write the total data length to the DDT request characteristic (`75AB2788-...`).
**Step 2 — Fragment:** For each fragment, write to the Write characteristic (`B606C7F9-...`):

```
┌──────────┬──────────────────────────┬──────────────────┐
│  1 byte  │  3 bytes                 │  N bytes         │
│ req ID   │  byte offset (24-bit BE) │  data chunk      │
└──────────┴──────────────────────────┴──────────────────┘
```

- Offset starts at 0, increments by chunk length after each acknowledgment.
- Wait for a CCU status notification before sending the next fragment.
- Transfer is complete when `offset >= total length`.
- Max chunk size: `MTU − 7` bytes.

---

### DDT CCU → phone (e.g. large responses)

Received as BLE notifications. The CCU pushes fragments without any phone-side trigger:

```
┌────────────┬───────────────┬──────────────────┬──────────────────┐
│  2 bytes   │  1 byte       │  1 byte          │  N bytes         │
│ seq number │ fragment idx  │ total fragments  │ payload chunk    │
└────────────┴───────────────┴──────────────────┴──────────────────┘
  big-endian   0-based        total count
```

- **Sequence number:** identifies which logical message this fragment belongs to.
- **Fragment index:** 0-based index.
- **Total fragments:** how many are expected for this message.
- Reassembly completes when all fragments for a sequence number have arrived.
- Timeout: 3000 ms. If a transfer stalls past this window, treat it as expired and discard the partial reassembly buffer.

### DDT v3 uses indication (not notification)

The DDT v3 characteristic (`56DC2D58-...`) uses GATT **indications** (CCCD = `0x0200`) rather than notifications. Your app must acknowledge each indication before the next one arrives. This provides flow control for large CCU→phone transfers.

---

## Request ID

Every outgoing command is assigned a request ID — a single byte (0–255) that increments and wraps. It is used to correlate write acknowledgments with the command that triggered them. Track the request ID you assigned to each in-flight command so you can match it against the corresponding write-complete callback.

---

## SPP Framing (OTA only)

Bluetooth Classic SPP uses a different framing:

```
┌────────┬──────────┬──────────────────┬──────────┐
│ 1 byte │  3 bytes │  N bytes         │  1 byte  │
│  0x01  │  length  │  payload         │  0x04    │
└────────┴──────────┴──────────────────┴──────────┘
  frame   big-endian                   end marker
  type
```

Frame type `0x01` = data. The final `0x04` is a frame terminator, not a CRC.

---

## Byte Order

All multi-byte integers use **big-endian** (network byte order) throughout. This applies to command codes, short values, sequence numbers, and lengths.
