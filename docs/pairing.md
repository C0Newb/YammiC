# Pairing — Obtaining the Passkey

## Short answer

An account with the OEM app is **required for the cloud API path**, but **not required if you have the physical QR code** that came with the CCU. A third path (extracting credentials from an already-paired Android device) requires no account and no QR code.

The passkey is a **6-character ASCII string**. It is factory-programmed into the CCU and never changes. Once obtained it can be reused indefinitely.

---

## What the Passkey Is

- **Length:** exactly 6 characters (ASCII alphanumeric)
- **Origin:** factory-programmed into the CCU hardware; the same value is also stored in the vendor's cloud
- **Wire encoding:** the 6 ASCII chars are sent as raw bytes in the BLE auth request
- **CCUID:** 14-character ASCII string (always accompanies the passkey)

Both values need to be persisted locally once pairing completes. A typical local store looks like:

| Key | Value |
|---|---|
| `passkey` | 6-char passkey |
| `ccu_id` | 14-char CCUID |
| `ccu_id_backup` | backup copy of CCUID |
| `bluetooth_mac_address` | BLE MAC address of the CCU |
| `bluetooth_device_name` | BLE device name (e.g. `YCCU_ABCDEF12345678`) |

---

## Path 1 — Extract from an Existing Paired Device (no account, no QR)

If you have an Android device on which the vehicle's official companion app (the OEM app) has already paired with the CCU:

1. This requires root access, an ADB backup, or a debuggable build of the OEM app.
2. The credentials live in that app's private preferences storage, in a preferences XML file scoped to the app's package.
3. Read the `passkey` and `ccu_id` entries from that file.

Since the passkey doesn't change, a value obtained this way is permanent and can be stored directly by your app.

---

## Path 2 — QR Code (no account needed)

Every CCU ships with a QR code (printed on the device or in its documentation). The QR code encodes a 20-character string that contains both the CCUID and passkey.

**Format of the decoded QR string (after unscrambling, if any):**

```
Characters [0..13]  = CCUID  (14 chars)
Characters [14..19] = passkey (6 chars)
```

**Note:** The raw QR payload may go through an unscrambling transformation before these fields can be extracted directly. The exact transformation is unconfirmed. In practice, either:

- The QR code **is** the plain 20-char string directly (no scramble), or
- There is a simple transformation (reversal, Caesar shift, XOR, etc.)

**To determine the scramble:** scan the QR code on your CCU, compare characters [0..13] against the known CCUID and [14..19] against the known passkey (obtained via another path). If they match without transformation, no unscrambling is needed. If they don't match, the transformation can be worked out from the mismatch pattern.

---

## Path 3 — Vendor Cloud API (OEM account required)

If no existing paired device and no QR code is available, the passkey can be fetched from the vendor's server using the vehicle's VIN or serial number.

### Requirements

- A registered account with the OEM app (authenticated through the vendor's identity provider, a Gigya/SAP Customer Data Cloud instance)
- A Gigya JWT token (obtained after login)
- The vehicle VIN (`vinCd`) **or** stamping model + serial number

### Endpoints

**Option A — VIN:**
```
GET https://sccu-eu.yamaha-motorcycle-connect.com/pairinginfo/{vinCd}
```

**Option B — Stamping model + serial number:**
```
GET https://sccu-eu.yamaha-motorcycle-connect.com/pairinginfo/{stampingStyle}/{serialNo}
```

### Required Headers

| Header | Value | Notes |
|---|---|---|
| `jwtKey` | `<gigya_jwt_token>` | Obtained from the OEM app's login flow |
| `x-api-key` | `SHA256(substrings of JWT)` | Derived per-request (see below) |

### API Key Derivation

The `x-api-key` header is computed from the JWT token at runtime — it is **not** a static key:

```
1. Let jwt = the raw JWT string from Gigya
2. Repeat jwt until total length >= 2048 chars  →  jwtPadded
3. Concatenate:
     jwtPadded[1951..1961]  (11 chars)
     + jwtPadded[104..107]   (4 chars)
     + jwtPadded[522..542]   (21 chars)
4. api_key = SHA-256(concatenated string), as hex string
```

This means the `x-api-key` value **changes with each JWT**. You cannot use a static API key — you must derive it from the current session JWT.

### Response

```json
{
  "ccuId":    "ABCDEF12345678",
  "passKey":  "ABC123",
  "vinCd":    "JYARJ18EXKA000001",
  "errorInfo": null
}
```

Fields:
- `ccuId` — 14-char CCUID
- `passKey` — 6-char passkey
- `vinCd` — vehicle VIN (≤25 chars)
- `errorInfo` — null on success, error object on failure

### Vendor Identity Provider Authentication

Getting the JWT requires completing the vendor's login flow through its identity provider (Gigya/SAP CDC). The OEM app uses a hosted Gigya screen set for registration and login. The resulting JWT needs to be persisted locally alongside the account session info after a successful login.

The exact Gigya configuration (API key, data center) used by the OEM app is embedded in that app's client and is not published; obtaining it requires inspecting the OEM app's own login integration.

---

## Pairing Flow Summary

```
User has QR code?
  YES → Scan QR → unscramble (if needed) → extract CCUID[0..13] + passkey[14..19]
  NO  → Need an OEM account + vehicle VIN
         → Log in via the vendor's identity provider → get JWT
         → GET /pairinginfo/{vin} with jwtKey + x-api-key headers
         → Response contains ccuId + passKey

Have passkey?
  → Store passkey + CCUID + BLE MAC in local storage
  → Connect BLE (see implementation-guide.md)
  → Send AuthenticationV2Request with ccuId bytes + passKey bytes
  → Success → connected
```

---

## BLE Auth Encoding

When sending the auth request over BLE, both CCUID and passkey are encoded as **raw ASCII bytes**:

```
ccuId bytes  = "ABCDEF12345678".getBytes(UTF_8)  → 14 bytes
passKey bytes = "ABC123".getBytes(UTF_8)          → 6 bytes
```

These are concatenated after the 2-byte command code `0x01 0x01` in the auth request payload.

---

## Practical Notes for Implementers

- **Simplest path**: if you already have a paired device, extract the passkey from its local storage (Path 1) and reuse it. The passkey is permanent.
- **New pairings for other users**: either require them to provide their passkey (extracted from their own device), or implement the QR code scan path (simpler, no account needed if they have the QR).
- **Cloud API path**: only needed if neither a QR code nor an existing paired device is available. It requires implementing the vendor's identity-provider login flow, which is significant additional scope.
- **The passkey never rotates** — once obtained it remains valid until the CCU is factory reset.
