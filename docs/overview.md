# CCU Bluetooth Protocol — Overview

## Transport

- **Transport:** Bluetooth Low Energy (BLE / GATT), with Bluetooth Classic SPP used only for over-the-air (OTA) firmware updates.

## Functional Components

A client implementation of this protocol is typically organized around the following logical components:

```
Client (public API surface)
  ├── BLE scanner            — discovers CCU devices
  ├── GATT handler           — connection, reads, writes, callbacks
  ├── SPP handler            — Bluetooth Classic, OTA updates only
  ├── Message codec          — encodes outgoing, decodes incoming messages
  ├── DDT-to-Vehicle session      — large outgoing messages (fragmented)
  ├── DDT-from-Phone sessions     — large incoming messages (fragmented)
  ├── DDT v3 session          — YRC settings transfer
  ├── YRC settings manager    — ride-control settings
  ├── Display/photo-frame push manager
  └── Lap time measurement manager
```

DDT ("Data-Divided Transfer," name inferred from protocol behavior) refers to the fragmentation scheme used to move payloads too large for a single BLE write across multiple packets. See below and `receiving-data.md` for details.

## Connection State Machine

```
Disconnect
    │  connect(device)
    ▼
  Connect  ← GATT link established
    │  all GATT descriptors written (notifications enabled)
    ▼
Initialized  ← ready to send/receive commands
    │  disconnect() or any error
    ▼
Disconnect
```

**Key timing constants:**

| Constant | Value |
|---|---|
| GATT connect timeout | 5000 ms |
| Descriptor write error retry count | 3 |
| Descriptor write error retry interval | 100 ms |
| DDT v2 session timeout | 3000 ms |
| DDT v3 session timeout | 3000 ms |
| Max packet size (normal) | 180 bytes |
| Max packet size (v1 compatibility mode) | 24 bytes |
| MTU requested | 512 bytes |

## Data Flow Summary

```
Phone → CCU  (commands/requests)
  Write to the write characteristic

CCU → Phone  (responses/notifications)
  Notifications on the main RX characteristic  ← most commands
  Indications on the DDT v3 characteristic     ← large data, ack required
  Notifications on other characteristics       ← other DDT sessions for large data
```
