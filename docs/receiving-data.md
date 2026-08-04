# Receiving Data

## Main RX Path (most commands)

Notifications arrive on the main RX characteristic. The raw bytes are routed by command code:

```
onCharacteristicChanged(characteristic, bytes)
  → read bytes[0..1] as short (command code)
  → switch(commandCode) → call matching decoder
  → fire listener callback
```

All listeners follow the same general pattern:
```
ReceiveListener<T>.onReceive(client, result)
```

Register listeners **before** connecting, e.g.:
```
client.addAuthenticationV2ResponseListener(...)
client.addVehicleInformationListener(...)  // receive listener for cmd 7
```

## Key Incoming Commands

| Command | Trigger | Payload |
|---|---|---|
| `AuthenticationV2Response` (258) | After sending auth request | Authentication result |
| `VehicleInformation` (7) | Continuous when subscribed | Vehicle telemetry |
| `LibVersion` (1) | On connect | Library/protocol version |
| `IgnitionStateResponse` (312) | After request | Ignition state |
| `CcuidResponse` (305) | After CcuidRequest | CCUID |
| `AliveCheckResponse` (310) | After ping | (empty) |
| `WeatherRequest` (3) | CCU needs weather | Weather request |
| `LanguageTypeRequest` (11) | CCU asks language | (empty) |
| `IconArrangementListRequest` (13) | CCU asks icons | (empty) |
| `BluetoothMusicControl` (279) | Handlebar controls | Music control event |
| `IncomingCallControl` (281) | Handlebar button | Call control event |
| `PhoneVolumeControl` (285) | CCU sets volume | Volume level |

## Write Acknowledgment

Every outgoing write fires a write-completion callback once the GATT write completes, carrying the request ID that was returned from the original send call.

```
// Send returns a request ID
result = client.writeAuthenticationV2Request(req);
requestId = result.value.id;

// Later, in the write-completion callback:
onWrite(client, writeResult) {
    // writeResult.value.id == requestId
}
```

## Large Data (DDT Sessions)

Some commands use fragmented DDT transfer instead of a single GATT write:

| Direction | Session | Commands |
|---|---|---|
| Phone → CCU | DDT-to-Vehicle | AuthV2Request, IconArrangement, NotificationData, MusicMetaData |
| CCU → Phone | DDT-from-Phone v1 | ConnectServerListResponse, MalfunctionResponse, MalfunctionInterval |
| CCU → Phone | DDT-from-Phone v2 | VehicleIdentificationResponse, MarketDataResponse, etc. |
| CCU → Phone | DDT v3 | VehicleInformationResponse, FfdResponse, CanResponse (proto) |

For DDT sessions, the completion callbacks are:
```
onComplete(sequenceNo: Short, completeData: byte[])  // all fragments received
onExpired(sequenceNoList: List<Short>)               // timeout (3000 ms)
```

## Ignition Polling

Ignition state is obtained by periodically sending `IgnitionStateRequest` (cmd 311). This is a simple poll pattern — there is no CCU-initiated ignition state push. Set the poll interval to something reasonable (e.g., every 5–10 seconds).

## Connection State Changes

```
ConnectionListener.onConnectionStateChange(client, state)
// state: Disconnect, Connect, Initialized

ConnectionListener.onConnectionError(client, result)
// Called on any GATT error; triggers auto-disconnect
```

On `Disconnect`, call `disconnect()` to clean up resources.
