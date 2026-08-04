# Discovery and Connection

## Device Scanning

Discovery is done with a standard low-power BLE scan. There is **no service UUID filter** — your app should receive all nearby BLE advertisements and filter candidates by device name in software.

**Device name format:**

```
"YCCU_XXXXXXXXXXXXXX"
 ^^^^  ^^^^^^^^^^^^^^
 5-char prefix  14-char CCUID suffix
 (total = 19 chars)
```

The 14-character suffix **is the CCUID**. Extract it from the advertised name and store it as a string — it is needed for authentication later.

There is also a `YSCCU_` prefix variant (seen on a newer/different device type). Your name-parsing logic should accept both prefixes and extract the CCUID suffix the same way in each case.

**Scan result:** for each matching advertisement, keep the underlying BLE device handle together with the extracted CCUID string — you'll need both to open the connection and to authenticate.

---

## GATT Connection Sequence

Once a BLE device has been identified as a CCU, open a direct (non-background) GATT connection to it. The connection then progresses through the following steps.

### Step 1 — GATT Connect

Open a direct GATT connection over the LE transport (not a background "auto-connect" connection).

> **Why a direct (non-background) connection matters:** A background/auto-connect mode tells the OS to keep retrying in the background even while the device isn't currently advertising. A direct connection attempt, by contrast, fails immediately (after a short timeout, typically around 5 s) if the target isn't advertising at that moment. The CCU appears to stop advertising once it already has an active BLE connection — this is the reason your app cannot connect while the OEM app currently owns the connection. See "Single-Connection Limitation" below.

### Step 2 — Service Discovery

Once the connection reports as established, perform GATT service discovery.

### Step 3 — MTU Negotiation

Once service discovery completes, request an MTU of 512 bytes. The CCU may negotiate down from this; use whatever MTU is actually granted for framing calculations elsewhere in the protocol (see `protocol.md`).

### Step 4 — Enable Notifications

Once the MTU exchange completes, enable notifications (and one indication) on the RX characteristics by writing their Client Characteristic Configuration Descriptor (CCCD). Serialize these descriptor writes — issue them one at a time, waiting for each write to complete before starting the next; concurrent descriptor writes are not reliable on most BLE stacks.

Characteristics requiring **notification** (CCCD = `0x0100`):
- RX Main (most command responses)
- DDT session-request RX
- DDT high-bandwidth RX
- DDT large-response RX
- DDT v2 session RX

Characteristic requiring **indication** (CCCD = `0x0200`):
- DDT v3 session RX (requires an application-level ACK — see `protocol.md`)

### Step 5 — Ready

Once all descriptor writes complete, the connection is considered initialized.

**Your app is now ready to authenticate.**

---

## GATT UUIDs

### Primary Service

```
AFA2CDF4-ECCF-46A7-A5EA-9DA428C0157A
```

### Characteristics

| Role | UUID |
|---|---|
| **Write** (phone → CCU) | `B606C7F9-E5A1-4E75-B313-2A920054A8EB` |
| RX Main (most responses) | `9C810D26-B605-4306-8C1C-755A1BA3066C` |
| RX YRC settings | `AB8D233A-CC58-4932-B397-A3751911E021` |
| RX DDT request | `75AB2788-87F6-4F61-BA9C-6F85A12600D1` |
| TX DDT (large send) | `56851826-A090-4F6A-A5DF-D1776520F74A` |
| RX DDT response | `E998AEAB-EA8F-4063-83AA-2017C1A44484` |
| TX DDT alt | `1A1A7A79-3D72-42A1-B084-843058453068` |
| RX DDT high-bandwidth | `77808451-E42F-4F6B-83ED-3AB44AA3D16A` |
| RX DDT large | `347456EE-A588-4DD5-BD98-62BE3672E27A` |
| RX DDT v2 | `E974B9A1-FA31-43C6-91AE-09931F3F6EF1` |
| RX DDT v3 (indication) | `56DC2D58-CBE1-4BA9-A104-E9B8EB4DA95C` |
| RX YRC response | `7690EEF4-F3A1-11EC-B939-0242AC120002` |

### CCCD Descriptor (standard)

```
00002902-0000-1000-8000-00805f9b34fb
```

---

## SPP (Bluetooth Classic)

Used **only for OTA firmware updates**. Not needed for normal operation. SPP uses:
- Service inquiry UUID: `00008911-0000-1000-8000-00805f9b34fb`
- Serial Port Profile UUID: `00001101-0000-1000-8000-00805f9b34fb`

---

## Single-Connection Limitation

The CCU appears to stop BLE advertising once it has an active GATT connection, and a direct (non-background) connection attempt requires the target to be actively advertising at the time of the attempt. In practice this means the CCU accepts only **one BLE central connection at a time** — it cannot be connected to your app and the vehicle's official companion app (the OEM app) simultaneously.

To connect your app independently:

1. The CCU must not have an existing connection, **or**
2. The existing connection must be dropped first.

Multiple simultaneous connections are unlikely to be supported at all, given this is an embedded device. If the OEM app (or any other client) already holds the connection, your app must wait for that connection to end before it can connect.
