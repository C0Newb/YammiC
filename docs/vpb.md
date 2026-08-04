# Virtual Pit Board (VPB)

VPB lets the phone push a small custom image (JPEG) onto the CCU's meter display. This document
covers the VPB wire protocol as understood from black-box protocol analysis and testing against a
real CCU. Two aspects of the protocol are easy to get wrong from a naive reading of the wire
format alone — see [Gotchas](#gotchas) below.

---

## Command Codes

| Code | Hex | Name | Dir | Notes |
|------|-----|------|-----|-------|
| 561 | 0x0231 | VirtualPitBoardRequest | P→C | Start a VPB display sequence |
| 562 | 0x0232 | VirtualPitBoardResponseNg | C→P | CCU rejects the request |
| 563 | 0x0233 | VirtualPitBoardGraphicRequest | C→P | CCU accepts, ready for image data |
| 565 | 0x0235 | SendVirtualPitBoardGraphic | P→C | One chunk of image data |
| 566 | 0x0236 | SendVirtualPitBoardGraphicResponse | C→P | Per-chunk ack/retry/reject |
| 567 | 0x0237 | ClearVirtualPitBoard | P→C | Clear the displayed message |
| 568 | 0x0238 | ClearVirtualPitBoardResponseOk | C→P | Clear succeeded |
| 569 | 0x0239 | ClearVirtualPitBoardResponseNg | C→P | Clear rejected |

`VirtualPitBoardGraphicResponseNg` (0x0234) also exists — sent by the app if it decides not to
proceed after being asked for the graphic (e.g. image too large / too many chunks). Not normally
needed.

---

## Transport split

VPB uses **two different transports** for outbound app→CCU traffic, which is easy to miss since
both commands "feel" similar:

| Command | Transport |
|---|---|
| `VirtualPitBoardRequest` (0x0231) | BLE GATT write (`simpleWriteUuid`) |
| `SendVirtualPitBoardGraphic` (0x0235) | Classic Bluetooth SPP socket |
| `ClearVirtualPitBoard` (0x0237) | BLE GATT write |

All **incoming** responses/requests (`0x0232`, `0x0233`, `0x0236`, `0x0238`, `0x0239`) arrive as
BLE GATT notifications, even though the graphic chunks themselves went out over SPP. This
asymmetry is a fixed property of the protocol: `VirtualPitBoardRequest` and `ClearVirtualPitBoard`
go over the BLE GATT write characteristic, while `SendVirtualPitBoardGraphic` goes over the
SPP-backed transport.

Your app must act as the **SPP server** — it opens a listening socket during connect and the CCU
connects to it as a client. The SPP link is also reused for the initial BLE-connect-info handoff
and OTA traffic; nothing else about VPB flows over it except the graphic chunks.

### SPP frame format

Every SPP write is wrapped in a simple STX/length/ETX frame:

```
[0x01][payload length, 4B big-endian][payload][0x04]
```

The payload is the normal `[cmd 2B][...]` command body. No checksum at this layer (the CRC32
described below is inside the VPB-specific payload, not the SPP frame).

---

## Handshake sequence

```
App                                   CCU
 │──VirtualPitBoardRequest (0x0231)──►│   BLE, 2 bytes, empty payload
 │                                    │
 │◄──VirtualPitBoardResponseNg (0x0232)   BLE — rejected, e.g. CCU busy/not ready
 │        (or)
 │◄──VirtualPitBoardGraphicRequest (0x0233)│  BLE — proceed, CCU wants the image
 │                                    │
 │──SendVirtualPitBoardGraphic (0x0235)──►│  SPP, chunk 0
 │◄──SendVirtualPitBoardGraphicResponse──│  BLE, ack/retry/reject for chunk 0
 │──SendVirtualPitBoardGraphic (0x0235)──►│  SPP, chunk 1
 │◄──SendVirtualPitBoardGraphicResponse──│  BLE, ack/retry/reject for chunk 1
 │                 ...                  (one chunk at a time, wait for ack before next)
```

A `VirtualPitBoardRequest` can be rejected (`0x0232`) if the CCU isn't ready — observed taking up
to ~90 seconds after BLE connect before the CCU starts accepting VPB requests. Just retry; there's
no separate "enable VPB" handshake needed first.

---

## Wire formats

### `VirtualPitBoardRequest` (0x0231) — app → CCU

```
[cmd 2B]
```

**No payload.** This is the first gotcha: it's tempting to include a content ID and a format byte
here, since those fields exist and are meaningful for VPB as a whole. They aren't part of this
particular command, though — the content ID and format byte are tracked locally by your app and
are only sent later, as part of the graphic chunks. `VirtualPitBoardRequest` itself carries no
payload at all. Sending anything else here gets every request rejected with `0x0232`.

### `VirtualPitBoardGraphicRequest` (0x0233) — CCU → app

```
[cmd 2B]
```

No payload — just tells the app to proceed with sending chunks.

### `VirtualPitBoardResponseNg` (0x0232) — CCU → app

```
[cmd 2B]
```

No payload — request rejected.

### `SendVirtualPitBoardGraphic` (0x0235) — app → CCU (SPP)

```
[cmd 2B][contentId 4B][index 1B][chunkSize 4B][totalIndex 1B][totalSize 4B][crc32 4B][chunkData]
```

| Field | Size | Notes |
|---|---|---|
| `contentId` | 4B | App-chosen, tracks this VPB session; echoed back in acks |
| `index` | 1B | 0-based chunk index |
| `chunkSize` | 4B | Length of `chunkData` in this packet |
| `totalIndex` | 1B | Index of the *last* chunk (`chunkCount - 1`) |
| `totalSize` | 4B | Total image byte length across all chunks |
| `crc32` | 4B | CRC32 of `chunkData` — see [CRC32 variant](#crc32-variant) below |
| `chunkData` | variable | Raw chunk bytes, up to `CHUNK_SIZE` |

`CHUNK_SIZE` is **6144 bytes**.

This is the **second gotcha**: the field right before the chunk data is not a repeat of the chunk
length — it's a checksum. The CRC32 field is easy to mistake for a repeated length field, since a
length field already appears two fields earlier in the same packet — sending the chunk length
there instead of a checksum causes the CCU to respond `FailureRetry` indefinitely, regardless of
chunk content.

#### CRC32 variant

The checksum is **not** standard `java.util.zip.CRC32`. It uses the same IEEE-802.3 polynomial
table, but:

- Initialized at **0** (not `0xFFFFFFFF`)
- Final value is a bitwise complement (`~crc`), same as the standard algorithm's XOR-with-all-1s
- Only the low 32 bits of the result are used

```kotlin
private fun crc32(data: ByteArray): Int {
    var crc = 0
    for (b in data) {
        val index = (crc xor b.toInt()) and 0xFF
        crc = (crc ushr 8) xor CRC32_TABLE[index]
    }
    return crc.inv()
}
```

### `SendVirtualPitBoardGraphicResponse` (0x0236) — CCU → app (BLE)

```
[cmd 2B][contentId 4B][index 1B][response 1B]
```

| `response` value | Meaning |
|---|---|
| `0x01` | Success — send the next chunk (or you're done, if this was the last one) |
| `0x02` | FailureRetry — resend the *same* chunk (observed: give up after 3 attempts) |
| `0x03` | FailureNotRetry — abort, don't retry |

### `ClearVirtualPitBoard` (0x0237) / responses — no payload

```
[cmd 2B]
```

Same for `ClearVirtualPitBoardResponseOk` (0x0238) and `ClearVirtualPitBoardResponseNg` (0x0239).

---

## Image format

The image is a composite of two things onto one JPEG frame before sending:

- A full **600×306** black "vehicle image" canvas
- A **526×66** message strip (background color + centered text) pasted at `left=0, top=163`

Both are built with plain `Bitmap.createBitmap(..., ARGB_8888)` + `Canvas`, then
`Bitmap.compress(JPEG, quality)` — standard Android APIs, nothing custom. A bare 526×66 image
without the black letterboxing gets rejected/retried by the CCU; match the exact 600×306 canvas
dimensions.

Content ID values are arbitrary app-chosen integers used purely for correlation. Any incrementing
scheme works, as long as content IDs don't repeat within an active session; e.g. starting from a
fixed base value and wrapping before overflow.

---

## Gotchas

1. **`VirtualPitBoardRequest` has no payload.** Sending a content ID + format byte here (a
   reasonable-looking guess, since those fields do exist elsewhere in the VPB flow) gets every
   request rejected with `VirtualPitBoardResponseNg` (0x0232).
2. **The chunk's trailing 4-byte field before the data is a CRC32, not a length repeat.** Sending
   the chunk's byte length there (which coincidentally also fits in 4 bytes and is easy to
   confuse with the earlier `chunkSize` field) causes the CCU to `FailureRetry` every chunk
   forever. The CRC32 is a nonstandard variant (init=0, not init=0xFFFFFFFF) — don't reach for
   a standard library CRC32 implementation directly.
3. **The CCU can take up to ~90 seconds after BLE connect before accepting VPB requests.** This
   looks like a timing bug at first but isn't — it's just the CCU not being ready yet. Retry
   `VirtualPitBoardRequest`; don't add an artificial delay thinking it's required — it isn't the
   actual fix for the two bugs above and doesn't shorten the CCU's own readiness window in any
   principled way.
4. **VPB and the navigation-screen link can visually interfere** since both push imagery to the
   CCU's display. Consider exposing independent enable/disable toggles for VPB and the
   navigation-screen link so they can be isolated during testing; disabling VPB should also clear
   any currently-displayed message.
