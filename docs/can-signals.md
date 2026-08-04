# CAN Signal Reference

The CCU exposes raw CAN bus signals via `CanRequest` (cmd 0x0113) / `CanResponse` (cmd 0x0114). You request signals by integer ID; the response delivers id/data byte pairs that you decode manually.

**Important:** Signals are split into EV (electric) and ICE (internal combustion engine) variants. Many IDs carry different fields depending on bike model. Check the vehicle type column.

---

## How to Request CAN Data

### One-shot request (cmd 0x0113)

The request is protobuf-encoded. Build it as a `CanRequest` with a list of signal IDs:

```
// Logical protobuf message (field 3 = id_list, repeated int32)
id_list: [0x0220, 0x0450, 0x0451]
```

Wire encoding: standard protobuf binary, then sent via the normal GATT write path (not DDT).

### Periodic polling (cmd 0x013B)

`CanIntervalRequest` adds an `interval` field (milliseconds):

```
interval: 1000        // poll every 1000 ms
id_list: [0x0220, 0x0450]
```

### Response (cmd 0x0114)

`CanResponse` returns a list of `IdData` pairs:
```
data_list:
  - id: 0x0220, data: [byte, byte, ...]
  - id: 0x0450, data: [byte, byte, ...]
```

Decode each `data` byte array using the scaling tables below.

---

## Signal Groups

Signals are grouped by their hex ID prefix. The same ID can have different byte layouts for ICE vs EV models.

---

## Group 0x02xx — Vehicle Status & Controls

### 0x0220 — Main vehicle flags

| Byte | Bits | Field | Scaling | Vehicle |
|------|------|-------|---------|---------|
| DATA01 | 0 | SideStandSW | 0/1 | Both |
| DATA01 | 1 | BrakeSW | 0/1 | Both |
| DATA01 | 2 | ParkingSW | 0/1 | EV |
| DATA01 | 3 | FallingDownFlag | 0/1 | EV |
| DATA01 | 4 | MCUCANErrFlag | 0/1 | EV |
| DATA01 | 6 | StartFlag | 0/1 | Both |
| DATA01 | 7 | AntiTheftFlag | 0/1 | ICE |
| DATA02 | 1–4 | VehicleSystemStatus | (raw >> 1) & 0xF | Both |
| DATA02 | 5–7 | SubMode | (raw >> 5) & 0x7 | EV |
| DATA03 | 2 | ReverseFlag | 0/1 | Both |
| DATA03 | 4 | ThreePhaseShortComm | 0/1 | Both |
| DATA04 | — | APSVoltage | raw × 0.02 V | Both |
| DATA08 | — | APSOpenRate (throttle %) | raw (0–255) | Both |

### 0x0230 — Odometer & status

| Byte | Field | Scaling | Vehicle |
|------|-------|---------|---------|
| DATA01 | Odometer | raw × 100 km | Both |
| DATA04 | DCDCStatus | raw | EV |
| DATA05 | MainSWStatus | raw | Both |
| DATA06 | ModelNumber | raw | Both |
| DATA07 | RelayStatus | raw | Both |

### 0x023C — Torque / Current

| Field | Scaling | Vehicle |
|-------|---------|---------|
| OutputTorque (EV) | (raw − 32768) / 100.0 N·m | EV |
| OutputIq (ICE) | (raw × 0.1) − 3276.8 A | ICE |

### 0x0243 — MCU state

| Bits | Field | Scaling | Vehicle |
|------|-------|---------|---------|
| DATA03 bits 4–7 | MCUState (ICE) | (raw >> 4) & 0xF | ICE |
| DATA05 bits 4–7 | MCUState (EV) | (raw >> 4) & 0xF | EV |
| DATA05 bit 1 | MCUErrorFlag | 0/1 | EV |
| DATA05 bit 2 | MCUPowerSupplyPermit | 0/1 | EV |
| DATA05 bit 0 | OutputTorqueDirection | 0/1 | EV |
| DATA03 (EV) | MCUVoltage | raw / 128.0 V | EV |

### 0x024E — MCU relay states (ICE)

| Byte | Field |
|------|-------|
| DATA01 | PreChargeState |
| DATA02 | WeldingState |
| DATA03 | BatterySwitchPermitted (0/1) |
| DATA04 | MainSWPermit (0/1) |

### 0x0252 — MCU error codes

| Bytes | Field | Scaling |
|-------|-------|---------|
| DATA01/03/05/07 | MCUErrorList[0–3] | formatted as 4-char hex |

---

## Group 0x03xx — EV Mode & Indicators

### 0x0322 — EV mode & charging

| Byte | Bits | Field | Scaling | Vehicle |
|------|------|-------|---------|---------|
| DATA03 | 4–7 | EVMode (ICE) | (raw >> 4) & 0xF | ICE |
| DATA05 | 4–7 | EVMode (EV) | (raw >> 4) & 0xF | EV |
| DATA05 | 2–3 | RemainingCapacityWarning | (raw >> 2) & 0x3 | EV |
| DATA05 | 1 | OutputLimit | 0/1 | EV |
| DATA06 | 2–3 | ChargerPlugIndicator | (raw >> 2) & 0x3 | EV |
| DATA06 | 4–5 | TCSIndicator | (raw >> 4) & 0x3 | EV |
| DATA06 | 0 | RunIndicator | 0/1 | EV |
| DATA07 | — | SOC1Monitor | raw (0–255) | EV |
| DATA01 | — | EstimatedChargeTime hours | raw | EV |
| DATA02 | — | EstimatedChargeTime minutes | raw | EV |

### 0x0323 — Vehicle error codes

| Bytes | Field | Scaling |
|-------|-------|---------|
| DATA01/03/05/07 | VehicleErrorList[0–3] | 4-char hex |

### 0x0329 — Speed pulse & display

| Byte | Bits | Field |
|------|------|-------|
| DATA01 | — | EVSpeedPulse |
| DATA02 | 0–3 | MultiFunctionDisplay |
| DATA02 | 4–7 | MeterDisplayMode |

### 0x0340 — Battery status & energy

| Byte | Field | Scaling | Vehicle |
|------|-------|---------|---------|
| DATA01 (EV) | ElectricConsumption | raw / 10.0 kWh/100km | EV |
| DATA01/02 (ICE) | Battery1/2Status | bitfield | ICE |

---

## Group 0x04xx — Battery Pack Data

### 0x0450 — Battery 1 main data

| Byte | Field | Scaling | Vehicle |
|------|-------|---------|---------|
| DATA01 | Segment | raw | Both |
| DATA03 (ICE) | TempMax | (raw − 40) × 0.5 °C | ICE |
| DATA03 (EV) | TempMax | (raw − 32768) / 10.0 °C | EV |
| DATA04 (ICE) | TempMin | (raw − 40) × 0.5 °C | ICE |
| DATA05 (ICE) | TotalVoltage | raw × 0.5 V | ICE |
| DATA07 (EV) | TotalVoltage | raw / 10.0 V | EV |
| DATA06 (ICE) | Current | raw × 10 A | ICE |
| DATA08 (ICE) | RSOC1 | raw & 0x7F % | ICE |

### 0x0451 — Battery 2 / EV current & SOC

| Byte | Field | Scaling | Vehicle |
|------|-------|---------|---------|
| DATA01 (EV) | Current | (raw − 32768) / 10.0 A | EV |
| DATA02 (ICE) | Battery2 Segment | raw | ICE |
| DATA03 (EV) | RSOC | raw / 10.0 % | EV |
| DATA05 (EV) | PrechargeState | raw | EV |
| DATA06 (EV) | WeldingState | raw | EV |
| DATA07 (EV) | SOH | raw / 10.0 % | EV |

### 0x0459 — Cell voltages & current limits

| Byte | Field | Scaling | Vehicle |
|------|-------|---------|---------|
| DATA01 (EV) | CellVoltageMax | raw mV (16-bit) | EV |
| DATA03 (EV) | CellVoltageMin | raw mV (16-bit) | EV |
| DATA05 (EV) | CurrentLimitDischarge | raw / 10.0 A | EV |
| DATA07 (EV) | CurrentLimitCharge | raw / 10.0 A | EV |
| DATA06 (ICE) | CurrentLimitCharge1 | raw × 0.5 A | ICE |

### 0x045A — Battery 2 cell data (ICE)

Same layout as 0x0459 but for the second battery pack.

### 0x0470 — Remaining capacity & FCC

| Byte | Field | Scaling | Vehicle |
|------|-------|---------|---------|
| DATA01 (EV) | RemainingCapacity | raw / 100.0 Ah | EV |
| DATA01 (ICE) | RemainingCapacity1 | raw × 0.1 Ah | ICE |
| DATA02 (ICE) | CurrentFCC1 | raw × 0.1 Ah | ICE |
| DATA03 (EV) | CurrentFCC | raw / 100.0 Ah | EV |
| DATA03 (ICE) | TotalChargeCapacity1 | raw × 1000 Ah | ICE |
| DATA04 (ICE) | TotalPeriodUse1 | raw days | ICE |
| DATA05 (EV) | ChargeableCapacity | raw / 100.0 Ah | EV |
| DATA07 (EV) | TotalPeriodUse | raw days | EV |

### 0x0480 / 0x0481 — BMU mode & warnings (ICE)

| Byte | Field |
|------|-------|
| DATA01 | BMUModeInfo |
| DATA02 | WarningFlag |
| DATA03 | AbnormalCode |

---

## Group 0x01xx — ICE Motor & Drive

### 0x01BA — Current & voltage (DC side)

| Byte | Field | Scaling |
|------|-------|---------|
| DATA01 | IqCurrent | (raw − 32768) / 100.0 A |
| DATA03 | IdCurrent | (raw − 32768) / 100.0 A |
| DATA05 | DCCurrent | (raw − 32768) / 100.0 A |
| DATA07 | DCVoltage | raw / 100.0 V |

### 0x0100 — Vehicle speed (ICE)

| Byte | Field | Scaling |
|------|-------|---------|
| DATA04 | VehRealSpeed | raw / 256.0 km/h |

---

## Group 0x06xx — Odometer & Range

### 0x0690 — ODO & range

| Byte | Field | Scaling |
|------|-------|---------|
| DATA02 | Odometer | raw km (long) |
| DATA06 | EstimatedRange | raw km |

---

## Named Motor Fields (EV)

These appear as named fields in the entity files rather than indexed by DATA byte:

| Field | Scaling | Notes |
|-------|---------|-------|
| MotorSpeed | raw − 32768 RPM | 16-bit, range ≈ ±32768 RPM |
| MotorTemperature | (raw × 0.1) − 20.0 °C | |
| MCUDriveState | raw | state code |
| MCUTemperature (EV) | raw − 55 °C | |
| MCUTemperature (ICE) | (raw × 0.1) − 20.0 °C | |
| PbBatVoltage (EV) | raw / 10.0 V | lead-acid battery |
| PbBatVoltage (ICE) | raw × 0.1 V | |
| SystemWarningIndicator | (raw >> 6) & 0x3 | 0–3 severity |

---

## Group 0x020A — Charging data (ICE/EV)

| Byte | Field | Scaling | Vehicle |
|------|-------|---------|---------|
| DATA01 (EV) | PeakCurrentOfBattery1 | (raw − 32768) / 10.0 A | EV |
| DATA03 (EV) | PeakVoltageOfBattery1 | raw / 10.0 V | EV |
| DATA05 (EV) | SOCOfBattery1 | 0–100 % | EV |
| DATA06 (EV) | SOCHOfBattery1 | varies | EV |
| DATA07 (EV) | ProtectionStatus | bitfield | EV |

---

## Practical Signal Sets

### Minimum useful set — ICE bike dashboard
```
Request IDs: [0x0220, 0x0230, 0x0100]

Gives you:
  - Throttle position (APSOpenRate) from 0x0220.DATA08
  - Side stand, brake, ignition from 0x0220.DATA01
  - Odometer from 0x0230.DATA01
  - Speed from 0x0100.DATA04
```

### Minimum useful set — EV bike dashboard
```
Request IDs: [0x0220, 0x0322, 0x0451, 0x0450, 0x0690]

Gives you:
  - Side stand, brake, parking from 0x0220.DATA01
  - EVMode, run indicator, charger plug from 0x0322
  - Battery SOC (RSOC %) from 0x0451.DATA03
  - Battery voltage from 0x0450.DATA07
  - Estimated range from 0x0690.DATA06
  - Odometer from 0x0690.DATA02
```

### Rich EV set
```
Request IDs: [0x0220, 0x0230, 0x0243, 0x023C, 0x0322, 0x0323,
              0x0340, 0x0450, 0x0451, 0x0459, 0x0470, 0x0690]

Adds:
  - Motor torque, MCU state/temp
  - Energy consumption kWh/100km
  - Cell voltage max/min
  - Remaining & full charge capacity (Ah)
  - Error codes
```

---

## Example: Periodic CAN Request (protobuf)

Protobuf wire encoding for `CanIntervalRequest` with IDs `[0x0220, 0x0451]` at 1000 ms:

```
Field 1 (interval, varint): 1000 → 08 E8 07
Field 3 (id_list[0], varint): 0x0220 = 544 → 18 A0 04
Field 3 (id_list[1], varint): 0x0451 = 1105 → 18 91 09
```

Full bytes: `08 E8 07 18 A0 04 18 91 09`

This is the protobuf payload. It is then wrapped with the command code:
```
[0x01][0x3B]  ← CanIntervalRequest
[req_id]
[protobuf bytes]
```

---

## Notes

- Signal availability depends on the **CCU firmware version** and **bike model**. Request all IDs in your set and ignore any that return no data.
- ICE bikes (gas engines) use a different signal subset than EV bikes, sharing the same ID space.
- The `server` field in FFD requests likely corresponds to ECU module IDs (e.g., fuel injection ECU, ABS ECU). Values are bike-model specific.
- Some 0x6C-prefixed group IDs (0x0B, 0x11, 0x18 sub-groups) have unclear BLE command routing — they may be sub-groups of the CAN response rather than standalone request IDs.
