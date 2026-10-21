# Navigation

The vehicle's navigation system is split across two apps:

- **The OEM companion app** — orchestrates route state, sends structured navigation data to the vehicle via BLE (packet `0x60`), and handles the turn-by-turn data pipeline from Garmin StreetCross.
- **Garmin StreetCross** — a third-party turn-by-turn app that renders the live map, runs the Garmin navigation engine, and streams rendered map frames plus navigation metadata to connected devices over Bluetooth using the **NaviLite binary protocol**.

The MPN display on the vehicle receives both the structured BLE navigation data (from the OEM app) and rendered JPEG map frames (from Garmin StreetCross via NaviLite).

---

## Architecture Overview

```
[Garmin StreetCross navigation engine]
        │
        │  GPS, route calculation, TBT step data
        ▼
[Garmin StreetCross map UI]
        │
        ├──► screen capture → JPEG 80% → NaviLite framing
        │
        └──► NaviLite protocol messages (ETA, speed, turn info, alerts)
                  │
                  │  Bluetooth socket (UUID 00007220-0000-1000-8000-00805F9B34FB)
                  ▼
           [Vehicle / MPN display]

---

[OEM app: turn-by-turn ingestion]  ←  receives TBT data from the Garmin navigation SDK
        │
        │  per-step navigation state
        ▼
[OEM app: route control, audio/lighting modes]
        │
        ▼
[OEM app: navigation state snapshot]
        │
        ▼
[OEM app: BLE encoder]  ←  encodes the snapshot into BLE packet 0x60
        │
   BLE (0x60)
        │
        ▼
   [Vehicle / MPN display]
```

---

## Garmin StreetCross — NaviLite Protocol

### Bluetooth Transport

- **Bluetooth UUID:** `00007220-0000-1000-8000-00805F9B34FB`
- The app communicates over a bound Bluetooth transport that exposes:
  - `write(bytes)` — send a NaviLite frame to the device
  - `connect(deviceAddress)` — open the connection
  - callbacks for incoming data, outgoing data tracking, and connection-state changes

### NaviLite Frame Format

Every message is wrapped in a NaviLite frame:

```
[Magic: "nAl@" (4 bytes)]
[Version: 1.0 (1 byte)]
[Checksum (variable)]
[Payload size (2 bytes)]
[Frame type (1 byte)]
[Service type (1 byte)]        ← message category — see table below
[Payload data type (1 byte)]
[Payload data ...]
```

### NaviLite Service Types (Outgoing — App → Device)

| Value | Constant | Description |
|-------|----------|-------------|
| `0x00` | `IMAGE_FRAME_UPDATE` | Live map JPEG frame |
| `0x01` | `ETA_UPDATE` | Estimated time of arrival |
| `0x02` | `NAVIGATION_STATUS_UPDATE` | Nav on/off |
| `0x04` | `NEXT_TURN_DIST_UPDATE` | Next turn distance + instruction |
| `0x05` | `TBT_LIST_UPDATE` | Full turn-by-turn list |
| `0x13` | `NAVIGATION_INFO_UPDATE` | Speed, current road, speed limit |
| `0x14` | `IMAGE_STOPPED_UPDATE` | Stop image stream |
| `0x21` | `DAY_NIGHT_MODE_UPDATE` | Map day/night mode change |
| `0x41` | `AUTH_REQUEST` | Authentication handshake |
| `0x50` | `IMAGE_FRAME_UPDATE_ACK` | ACK for received image frame |
| *(40+ more)* | — | Safety cameras, weather, route progress, alerts, dialogs, POI |

### NaviLite Service Types (Incoming — Device → App)

These arrive as callbacks the app must handle:

| Callback | Description |
|----------|-------------|
| `handleStartRouteRequest(listIndex, itemIndex, routeOption)` | User selected a route on device |
| `handleStopRouteRequest()` | User cancelled navigation |
| `handleSkipNextWaypointRequest()` | Skip current waypoint |
| `handleMapZoomInRequest()` | Zoom in |
| `handleMapZoomOutRequest()` | Zoom out |
| `handleGoHomeRequest()` | Route to saved home |
| `handleGoOfficeRequest()` | Route to saved office |
| `handleDialogActionSelected(selection)` | User confirmed a dialog (e.g. toll road) |
| `handleStartContentUpdateRequest()` | Request POI/traffic/weather refresh |
| `handleStopContentUpdateRequest()` | Stop content updates |
| `handleReceiveImageAck(seqNumber)` | Device ACK for received map frame |
| `handleReceiveAuthRequestAck(isValid)` | Auth response |
| `handleVehicleSpeedUpdate()` | Vehicle speed from MCU |
| `handleMcuEsnUpdate()` | MCU serial number |
| `handleMcuSysInfoUpdate()` | MCU system info |

### Map Image Streaming

**Flow:**
1. The map view renders the live navigation map (Garmin navigation engine)
2. The rendered frame is captured as a bitmap
3. Compressed to **JPEG at 80% quality**
4. Wrapped in a NaviLite `IMAGE_FRAME_UPDATE` (service type `0x00`) frame
5. Transmitted over the Bluetooth socket
6. Device sends back `IMAGE_FRAME_UPDATE_ACK` (`0x50`) with sequence number

**Image parameters:**
- Format: JPEG, 80% quality
- Dimensions: device-specific — must match the connected display (see model detection table below)
- DPI: fixed at 160
- Sequence numbers: 1–30000, wraps around
- Frame rate: ~1 fps normal; reduced automatically in low-throughput mode (ACK timeout > 4000 ms)

**Image type byte flags:**
| Value | Meaning |
|-------|---------|
| `0x00` | Normal navigation map |
| `0x01` | JCV (Garmin vector format) |
| `0x02` | Throughput test data |
| `0x03` | Expanded map view |
| `0x04` | Other |

**ACK-based flow control:** The app monitors elapsed time between ACKs. If a device is slow (> 4000 ms per frame), it enters a low-throughput mode and reduces frame rate.

### Intents / Broadcasts

Beyond the Bluetooth wire protocol, the app responds to a small set of external triggers: a close-app signal, a startup-completed signal, a GPS-service-ready signal, and standard share/open intents for location files (e.g. GPX). These are internal to the host app and are not part of the Bluetooth wire protocol.

---

## OEM App — BLE Navigation Packet (0x60)

The OEM app sends structured navigation data directly to the vehicle via its own BLE connection, independent of the Garmin image stream.

**Message ID:** `0x60` (96 decimal)
**Group:** `0x05`
**Total size:** ~101 bytes

### LocalId 0x0592 — Navigation State & Primary Guidance (8 bytes)

| Byte | Bits | Field | Range | Notes |
|------|------|-------|-------|-------|
| 0 | 7–4 | `naviState` | 0–15 | See state machine below |
| 0 | 3–0 | `roundaboutExitNumber` | 0–15 | Exit number at roundabout |
| 1 | 7–0 | `naviIndex` | 0–255 | Current turn instruction index |
| 2–3 | 15–0 | `nextDistance` | 0–65535 | Distance to next turn |
| 4 | 7–0 | `naviIndex_2nd` | 0–255 | Secondary/upcoming turn index |
| 5–6 | 15–0 | `nextDistance_2nd` | 0–65535 | Distance to secondary turn |
| 7 | 7–6 | `nextDistanceUnit` | 0–3 | Unit encoding for next turn distance |
| 7 | 5–4 | `nextDistance_2ndUnit` | 0–3 | Unit encoding for secondary distance |

### LocalId 0x0593 — Destination & Transit Info (8 bytes)

| Byte | Bits | Field | Range | Notes |
|------|------|-------|-------|-------|
| 0–1 | 11–0 | `destinationTime` | 0–4094 | Seconds to destination |
| 1 | 1–0 | `destinationDistanceUnit` | 0–3 | Distance unit |
| 2–3 | 15–0 | `destinationDistance` | 0–65535 | Distance to destination |
| 4–5 | 11–0 | `transitTime` | 0–4094 | Seconds to next waypoint |
| 5 | 1–0 | `transitDistanceUnit` | 0–3 | Distance unit |
| 6–7 | 15–0 | `transitDistance` | 0–65535 | Distance to next waypoint |

### LocalId 0x0594 — Weather & Music Status (8 bytes)

| Byte | Bits | Field | Range | Notes |
|------|------|-------|-------|-------|
| 0 | 7–2 | `weatherForecastInfo` | 0–63 | Weather condition code |
| 0 | 1–0 | `musicPlayingStatus` | 0–3 | 0=stopped, 1–3=playing variants |
| 1 | 7–4 | `precipitationForecast` | 0–15 | Rain/snow intensity |
| 1 | 3–0 | `windSpeedForecast` | 0–15 | Wind speed level |
| 2 | 7–4 | `weatherForecastPoint` | 0–15 | Location/distance of forecast |
| 3 | 7–1 | `tempForecast` | 0–127 | Temperature in °C |
| 4–7 | — | Reserved | 0x55 | Fixed bytes |

### LocalId 0x0595 — Turn Instruction Text (61 bytes)

| Byte | Field | Notes |
|------|-------|-------|
| 0 | Reserved | `0x00` |
| 1–60 | `instructionMessage` | UTF-16 Big-Endian, max 30 characters ("Turn left on Main St") |

### LocalId 0x0598 — Reserved (8 bytes)

All bytes `0x55`. Reserved for future expansion.

### Distance Unit Encoding

| Value | Unit |
|-------|------|
| 0 | Meters |
| 1 | Kilometers |
| 2 | Feet |
| 3 | Miles |

### Navigation State Snapshot Fields

The full structure assembled before encoding into packet `0x60`:

```
{
    // State
    naviState               // 0–15
    roundaboutExitNumber    // 0–15

    // Primary turn
    naviIndex                // 0–255
    nextDistance              // 0–65535
    nextDistanceUnit           // 0–3

    // Secondary turn
    naviIndex_2nd             // 0–255
    nextDistance_2nd           // 0–65535
    nextDistance_2ndUnit        // 0–3

    // Destination
    destinationTime           // 0–4094 seconds
    destinationDistance        // 0–65535
    destinationDistanceUnit     // 0–3

    // Transit (current segment)
    transitTime               // 0–4094 seconds
    transitDistance             // 0–65535
    transitDistanceUnit          // 0–3

    // Weather
    weatherForecastInfo         // 0–63
    musicPlayingStatus          // 0–3
    precipitationForecast        // 0–15
    windSpeedForecast            // 0–15
    weatherForecastPoint          // 0–15
    tempForecast                 // 0–127 °C

    // Fixed reserved
    byte4Fixed                  // 0x55
    byte5Fixed                  // 0x55
    byte6Fixed                  // 0x55
    byte7Fixed                  // 0x55

    // Turn text
    instructionMessage           // UTF-16, max 30 chars
}
```

---

## OEM App — Turn-by-Turn Data Ingestion

The OEM app receives navigation updates from the Garmin navigation SDK over an internal IPC channel and converts them into the navigation state snapshot described above.

**Message code:** `7200` — Navigation information update

```
Garmin Navigation SDK
    │
    │  internal IPC (message code 7200)
    ▼
OEM app: TBT ingestion
    │
    │  parses per-step navigation state
    ▼
OEM app: current navigation state
    │
    │  observed by route control
    ▼
OEM app: navigation state snapshot → BLE packet 0x60
```

---

## OEM App — Turn-by-Turn Data Fields

### Current Navigation State

```
{
    navState
    currentStep                          // step info, see below
    distanceToCurrentStepMeters
    distanceToDestinationMeters
    distanceToFinalDestinationMeters
    distanceToNextDestinationMeters
    timeToCurrentStepSeconds
    timeToFinalDestinationSeconds
    remainingSteps                       // list of step info
    routeChanged
}
```

### Single Turn Step

```
{
    stepNumber
    maneuver                    // turn direction enum
    maneuverBitmap               // turn icon
    lanesBitmap                  // lane guidance diagram
    fullInstructionText          // "Turn left on Main Street"
    simpleRoadName                // "Main St"
    fullRoadName                  // "Main Street"
    distanceFromPrevStepMeters
    timeFromPrevStepSeconds
    roundaboutTurnNumber
    exitNumber
    drivingSide                  // left-hand / right-hand traffic
}
```

---

## Navigation State Machine

```
naviState values (0–15):

0  = Idle / no route
1  = Enroute (active guidance)
2  = Arriving (within ~100 m of destination)
3+ = Reserved / extended states

Transitions:

  IDLE ──setRoute()──► Searching
                           │
              ┌────────────┴──────────────┐
            OK                     NO_ROUTE_FOUND / NETWORK_ERROR
              │
          startNavigate()
              │
           ENROUTE ──proximity check──► ARRIVING
              │                              │
         user stops                    OnArriveAtDestination
              │
           IDLE
```

**Route status enum:**

| Status | Meaning |
|--------|---------|
| `OK` | Route valid, ready to navigate |
| `NO_ROUTE_FOUND` | Destination unreachable |
| `ROUTE_CANCELED` | User or system cancelled |
| `NETWORK_ERROR` | No connectivity |
| `WAYPOINT_ERROR` | Invalid waypoint |
| `LOCATION_DISABLED` | GPS/location off |
| `LOCATION_UNKNOWN` | Cannot determine position |
| `QUOTA_CHECK_FAILED` | API quota exceeded |

---

## OEM App — Navigation Controller API

### Core Navigation Engine

```
setRoute(coordinate)                     // Set destination by coordinate
setRouteByPlaceId(placeId)               // Set destination by place ID

startNavigate()                          // Begin turn-by-turn guidance
stopNavigate()                           // End guidance, clear route

startTBTService(options, ...)            // Register for TBT updates (msg 7200)
stopTBTService()
startTurnByTurnObserver()                // Observe navigation state
stopNaviObserver()

setDisplayUnit(unit)                     // METRIC / IMPERIAL
setAudioGuidance(mode)
setNightMode(lighting)                   // AUTO / DAY / NIGHT

startSimulator(speedMultiplier)          // 1.0–5.0×
stopSimulator()
```

### Public Controller API Layer

```
setRoute(coordinate)
startNavigate()
stopNavigate()
setUnit(unit)
setAudioGuidance(mode)
setNightMode(lighting)
setViewMode(viewMode, boolean, boolean)
getCurrentLocation()
getCurrentCameraPosition()
isGuidanceRunning()

showNearbyGasStationMode()              // 10 km radius POI search, 500 ms debounce
hideNearbyGasStationMode()
```

---

## Enums

```
AudioGuidanceMode {
    SILENT                     (0)
    VOICE_ALERTS_ONLY          (2)
    VOICE_ALERTS_AND_GUIDANCE  (4)
}

Lighting {
    AUTO    // Automatic day/night
    DAY
    NIGHT
}

ViewMode {
    NONE
    TRACKING  // Vehicle-centred, rotates with heading
    OVERVIEW  // Full-route bird's-eye view
}

MapType {
    MAP_TYPE_NONE
    MAP_TYPE_NORMAL
    MAP_TYPE_SATELLITE
    MAP_TYPE_HYBRID
    MAP_TYPE_TERRAIN
}

DisplayUnit {
    METRIC    // km / m
    IMPERIAL  // miles / feet
}
```

---

## Dispatcher Actions (OEM App)

| Action Class | Inner Class | Description |
|---|---|---|
| `NaviSetRouteAction` | `OnSearchRoute` | Route calculation complete |
| `NaviSetRouteAction` | `OnChangeRoute` | Route modified mid-guidance |
| `NaviSetRouteAction` | `OnSetRouteStatus` | Status update (OK, errors) |
| `NaviNavInformationAction` | `OnNavigationNavInformation` | Per-step TBT update |
| `NaviNavInformationAction` | `OnArriveAtDestination` | Arrived at destination |
| `NaviNavInformationAction` | `OnSetAudioGuidance` | Audio mode changed |
| `NaviMapEventAction` | *(various)* | Map pan/zoom/marker events |
| `NaviNavigationAction` | *(various)* | Navigator setup/teardown |

---

## MPN Image Transmission (OEM App)

The vehicle's MPN display can also receive image data from the OEM app directly (separate from the Garmin NaviLite image stream).

### MPN Image Message

```
{
    type              // LANE_GUIDE, MANEUVER, ROAD_NAME, SPEED_INFO, …
    requestType
    contentId
    nextPageAndFlags  // Bit flags: more pages, continuation
    data              // Raw image bytes (JPEG or PNG)
}
```

### MPN Image Display Request Message

```
{
    type
    contentId
    top
    pushedUp
    pushedDown
    pushedLeft
    pushedRight
    pushedEnter
}
```

---

## Custom Map Images — What's Possible

Since Garmin StreetCross streams **plain JPEG images** over a documented NaviLite frame format, it is possible to inject or replace these frames:

1. **Intercept the Bluetooth stream** — NaviLite frames have a known magic header (`"nAl@"`), so frames can be parsed and replaced.
2. **Generate custom map tiles** — Any bitmap can be JPEG-compressed and wrapped in a NaviLite `IMAGE_FRAME_UPDATE` (`0x00`) frame with a valid sequence number.
3. **Implement flow control** — The device sends `IMAGE_FRAME_UPDATE_ACK` (`0x50`) with sequence numbers; the sender must honour these to avoid flooding.
4. **Alternatively, via the MPN image message** — the OEM app's own MPN layer can send custom bitmaps directly without touching the Garmin stream.

The most straightforward path is the MPN image route, since the OEM app already has the plumbing for it. Replacing the Garmin stream would require either a man-in-the-middle on the Bluetooth socket or modifying Garmin StreetCross directly.

---

## Your App's Own NaviLite Implementation (No Garmin StreetCross Required)

It's possible to build a NaviLite client that connects to the bike's navigation-screen RFCOMM link directly, without Garmin StreetCross installed. This section documents what was actually confirmed while building and testing an independent RFCOMM-based implementation, including corrections to earlier assumptions elsewhere in this document. Where something below contradicts an earlier assumption in this file, trust this section — it has been validated against a real CCU (part number `006-B4160-03`), not merely inferred.

### Corrections to earlier assumptions

- **You are the RFCOMM client, not a server.** An earlier assumption was that a third-party implementation should listen for an incoming RFCOMM connection and wait for the bike to connect. In practice the bike is already listening on UUID `00007220-…`, and your app should connect to it as a client using a standard insecure RFCOMM socket. Confirmed working end-to-end.
- **CRC location in the frame.** An earlier assumption placed the CRC after the payload, at the very end of the frame. The real layout is:
  ```
  magic "nAl@"(4) | version(1) | frameType(1) | serviceType(1) | payloadSize(4 LE) |
  payloadDataType(1) | CRC32/MPEG-2(4 LE) | payload[payloadSize]
  ```
  CRC comes right after the 12-byte header (before the payload), computed over header+payload excluding the CRC field itself. Confirmed against a real captured `SEC_DATA_ACK` frame (`header+payload → CRC 24 f0 73 5b`, byte-exact match).
- **`ESN_ACK`/`AUTH_REQUEST` are not empty-payload messages.** Sending them empty (as originally assumed) means the CCU never proceeds to `SEC_DATA` — this produced a real on-device failure (`Expected SEC_DATA (83), got 56`). See confirmed payloads below.

### Confirmed handshake (byte-exact, tested against real hardware)

1. Wait for `ESN_UPDATE`(66) — **skip any unrelated frames** the CCU sends first rather than treating them as an error (the CCU interleaves other traffic even before auth completes).
2. Send `ESN_ACK`(81), payload `01 00` (VALUE). An empty payload is silently ignored.
3. Send `AUTH_REQUEST`(33), payload `1c 07 00 01 00 00 00 00` (POINTER) — encodes app version 1820 (`0x071c`). An empty/malformed payload here means the CCU never sends `SEC_DATA`.
4. Wait for `SEC_DATA`(83) — again skip unrelated frames. Payload is `("<part-number>" + 4-byte nonce) XOR 0x0A`. The part number also reveals the CCU's display resolution — see "Model detection" below.
5. Send `SEC_DATA_ACK`(84), payload = the de-obfuscated 4-byte nonce (POINTER).
6. Send the setup burst (11 frames, real observed values):

   | Service | Payload | Type |
   |---|---|---|
   | `NAV_STATUS`(2) | `00 00` | VALUE |
   | `DAY_NIGHT`(31) | `01 00` | VALUE |
   | `HOME`(10) | `01` (1 byte!) | VALUE |
   | `OFFICE`(11) | `01` (1 byte!) | VALUE |
   | `GPS`(13) | `01 00` | VALUE |
   | `APP_SETTING`(12) | `00 00` | VALUE |
   | `ZOOM`(14) | `07 19 06 00 30 2e 32 20 6d 69` | POINTER |
   | `ROAD`(3) | *(empty)* | POINTER |
   | `SPEED_LIMIT`(17) | `00 00 00 00 03 6d 70 68` | POINTER |
   | `GPS`(13) again | `01 00` | VALUE |
   | `APP_SETTING`(12) again | `01 00` | VALUE |

### `HOME_SETTING_UPDATE`/`OFFICE_SETTING_UPDATE` — 1 byte, not 2

Confirmed by observing the actual bytes sent on a real connection: the payload is a single `isSet & 1` byte, not 2 bytes as earlier assumed. `0x00` = not configured (the CCU shows "not set" **immediately, without ever asking the phone** — it never sends `GO_HOME_REQUEST`/`GO_OFFICE_REQUEST` in that case), `0x01` = configured.

**Tried and reverted:** by analogy with the HOME/OFFICE fix above, it was hypothesized that `NAV_STATUS`/`GPS`/service-12 might also use a single-byte payload, on the theory that a length mismatch was making the CCU silently ignore the whole setup burst (and thus never register `HOME`/`OFFICE` either). **On real hardware this made things worse** — the CCU went from silently showing "not set" to explicitly rejecting the connection ("settings are wrong"). `NAV_STATUS`/`GPS`/service-12 were reverted back to their original 2-byte form; only the `HOME`/`OFFICE` 1-byte fix (above) is kept, since it wasn't implicated in the regression. Root cause of "home/work still show not set" is still open — there may be a dependency on another field (e.g. real GPS coordinates, or a message not yet identified) that's missing from the setup burst.

### Image channel

- Service `IMAGE`(0) / `IMAGE_ACK`(80). Payload: `[imageType: 1B][seq: 2B LE][JPEG bytes]`.
- **`imageType` must be `3`** (expanded-navigation render mode) — `0` is a different, wrong render mode.
- **Resolution must match the CCU model** — sending an arbitrary-resolution JPEG risks the CCU rejecting/mis-rendering it. See model table below; letterbox any source image onto the correct canvas before compressing.
- Flow control: sliding-window semaphore with a capacity of 2, 1000ms acquire timeout (send anyway + log "window stalled" if it expires), guarded release (only release if permits available are below the cap) so a late/duplicate ACK can't over-release. Confirmed working — `IMAGE_ACK` observed in the on-device log.
- The CCU appears to expect a continuous feed even for static content; re-send the last pushed image at a low idle rate (e.g. 2fps) so a single one-shot push doesn't look like a stalled connection.

### Model detection (from the SEC_DATA part number)

Not queried live — inferred client-side from the part-number string in `SEC_DATA`:

| Part number prefix | Resolution | Model |
|---|---|---|
| `006-B3952` | 480×234 | base LinkCard layout |
| `006-B4160` / `006-B4920` | 480×240 | richer layout |
| anything else | fallback ~480×234 | unknown/invalid model |

### Dash button requests (confirmed against real hardware)

| Service | Payload | Meaning |
|---|---|---|
| `GO_HOME_REQUEST`(53) | — | Rider tapped "Go home" (only sent if `HOME` was reported as configured) |
| `GO_OFFICE_REQUEST`(54) | — | Rider tapped "Go to work" |
| `START_CONTENT_UPDATE_REQUEST`(55) | `[contentType: 1B][00]` | Rider opened a content screen. Captured on real hardware: `01 00`=NAVI_IMAGE, `03 00`=FAVORITE_LIST (Favorites), `04 00`=STATIONS_LIST (gas stations) |
| `STOP_CONTENT_UPDATE_REQUEST`(56) | `[contentType: 1B][00]` | Rider left that content screen |

Favorites/gas-stations **must** be answered or the CCU spins on "loading" forever — there's no timeout.

### `FAV_POI_LIST_UPDATE`(7) / `GAS_POI_LIST_UPDATE`(8) — list-size reply

Payload format (confirmed): `[itemCount: 1B][0x00]`. Send this in reply to `START_CONTENT_UPDATE_REQUEST` before any per-item data.

### `FAV_POI_LIST_DATA_UPDATE`(98) / `GAS_POI_LIST_DATA_UPDATE`(99) — one per item

Every field — including what might look like a floating-point distance value — collapses to a single byte in the actual wire format (distance is quantized, not a real float):

```
[index: 1B][unknown, always 0 so far: 1B][direction: 1B signed][len(name): 1B][len(road): 1B]
[distance: 1B, 0-255][road bytes][name bytes]
```

- **Direction is relative to the vehicle, not compass:** `0`=undefined, `1`=front, `2`=front-right, `3`=right, `4`=back-right, `5`=back, `6`=back-left, `7`=left, `8`=front-left.
- **Confirmed quirk, not a transcription error:** the two length-prefix bytes are written in `(name, road)` order, but the string *data* bytes are appended in `(road, name)` order.
- Implement this as a POI entry data structure in your app; **not yet confirmed against real hardware** at time of writing (the empty-list reply was confirmed to work; the populated 3-fake-entry reply is a best-effort reconstruction pending an on-device test).

### Open questions / not yet confirmed

- The "unknown" byte in the POI entry structure (always `0` so far) — purpose unconfirmed.
- Whether the name/road field-order quirk above actually renders correctly on-screen, or whether the two strings need swapping.
- Distance quantization unit/scale for POI entries (currently sent as whole miles, unconfirmed).
- Whether the Bluetooth Classic RFCOMM link your app opens (UUID `7220`) can coexist indefinitely with the existing VPB SPP link (UUID `8911`) — an earlier suspected connection-contention issue turned out to just be the bike powering off, so this remains unconfirmed either way.

---

## Turn-by-turn guidance (NaviLite services 4 and 19)

The bike's guidance display (turn arrow, distance to the turn, road) is fed over the same NaviLite link as the map image, not over the
ordinary BLE commands (the `0x60` packet above belongs to a different, scooter-class unit). Two messages exist; which one a bike takes
depends on its part number from the handshake:

| Part number | Message |
|---|---|
| `006-B3952` | `NEXT_TURN_DIST_UPDATE`, service 4 |
| `006-B4160`, `006-B4920` | `NAVIGATION_INFO_UPDATE`, service 19 |
| anything else | both are sent; a bike ignores the one it does not use |

Both go in a phone frame (frame type 6) with payload data type "pointer". Numbers are little-endian, floats are IEEE 754 single precision,
strings are UTF-8, at most 144 bytes each.

Service 4: `[icon u8][distance f32][unit length u8][road length u8][unit][road]`.

Service 19: `[icon u8][next distance f32][unit length u8][road length u8][remaining distance f32][remaining unit length u8]
[remaining minutes u32][lane count u8][unit][road][remaining unit][lane bytes]`, at most 5 lane bytes.

The icon is an arrow ordinal (the bike draws the arrow): 0 arriving, 1 arriving left, 2 arriving right, 3-5 via point, 6 keep left,
7 keep right, 8 continue, 9 drive to, 10 exit left, 11 exit right, 12 exit, 13 ferry, 14 roundabout, 32 sharp left, 33 sharp right,
34 left, 35 right, 36 U-turn left, 37 U-turn right, 69 none. Roundabout exits (15-31) and heading arrows (38-68) also exist.

The distance is a number in the unit named by the unit string. The unit strings the bike expects ("ft", "mi", "m", "km") are a guess
and unverified on a bike. Likewise unverified: whether the bike needs the navigation image stream to be running, and how it shows
service 19's remaining trip and lanes. The layout comes from the packer in the reference navigation library; the bytes have not yet
been seen on a bike.

In YammiC: `NaviLiteTurn` (builders), `NavigationSdk.SetTurn` / `ClearTurn`, `BikeService.ShowTurn`, and a test form under
Bike UI -> Navigation status -> Turn-by-turn test.

First device test (006-B4160-03): the service 19 message went out and the bike showed nothing; the bike's turn-by-turn option was greyed out. The bike never sent a "start content update" for content type 2 (the turn list). Untried so far: the navigation status (service 2) says `00 00` (idle) after the setup, and the app now sends `01 00` while a turn is shown and `00 00` when it is cleared. If the bike still does not offer the screen, what makes it enable it is unknown (the authentication request's fixed bytes are the other candidate).
