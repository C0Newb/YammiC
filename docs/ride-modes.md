# Ride modes (YRC settings) and lap timing

Status: implemented from the vendor SDK's data layouts; **not yet tested against a bike**. Check the log (sections "Ride modes", "Lap timer") on first use.

## Reading ride modes

Two steps, as the official app does:

1. `RequestYrcSettingsInfo` (0x0201) with data kind COMMON only. The answer lists the modes (and flags, current mode).
2. Same request with kinds NAMES, MIN_MAX, CURRENT and the `details` list set to those modes. Responses are merged by (mode, saved number).

Request (simple write): `[0x0201][len 3B][requestId 1B][protobuf]`

- 1 data_kind (packed enum: 1 common, 2 names, 3 min/max, 4 current values)
- 2 details (packed enum: ride mode codes)

Response `ResponseYrcSettingsInfo` (0x0202) arrives as a bike-to-phone DDT session (v2/v3 framing, see `can-signals.md`): `[0x0202][len 3B][requestId][protobuf]`.

Info protobuf: 1 data_kind, 2 result (0 = ok), 3 kw_status_flag, 4 yccs_model_flag, 5 yccs_at_flag, 6 track_flag, 7 sus_info, 8 current_yrc_mode, 9 current_sus_mode, 10 start_trigger (1 LC, 2 app), 11 settings (repeated).
Setting: 12 mode, 13 app_saved_no, 14 yrc_mode_type, 15 name (max 10 chars), 16 values (repeated).
Value: 17 kind, 18 current, 19 min, 20 max.

Mode codes: MT1-6 = 16-21, AT1-6 = 22-27, TR1-4 = 28-31 (Track 1-4), SUS01-15 = 32-46, ALL = 255.
Mode types: 1 MT without YCCS, 2 MT with YCCS, 3 AT, 4 track, 5 suspension.
Setting kinds: 1 PWR, 2 TCS, 3 SCS, 4 LIF, 5 BSR, 6 LCS, 7 QSS, 8 DSS, 9 BC, 10 EBM, 11 SUS, 49-72 suspension values.

## Sending ride modes

`LoadYrcSettingsData` (0x0208) over a phone-to-bike DDT session: `[0x0208][len 3B][requestId][protobuf]` with data kinds [NAMES, CURRENT], start_trigger = 2 (app) and the settings list (mode, saved number, type, name, kind + current for each value).
The bike answers `NotifyLoadYrcSettingsDataResult` (0x0209): `[0x0209][requestId][result 4B big-endian]`; 0 is OK. Other results: 3 parameter out of range, 9 another command is running, 17 could not parse, 18 data could not be applied.

The app checks every value against the min/max the bike reported before sending.

The bike can also push its own saves (`SaveYrcSettingsData` 0x0203, to be answered with 0x0204). Not handled yet.

## Lap timing

`SendLapSectorTime` (0x0251, simple write): `[0x0251][requestId][lapNo][lapTime 3B][referenceTime 3B][sectorNo][gap1 2B][gap2 2B][gap3 2B][gap4 2B][correction]`.
Times are hundredths of a second (max 359999). Lap number 255 / time 1048575 mean "none". Gaps are sent as value + 5999; 12287 means "sector not measured", 16383 "invalid". Sector number 0-3. Correction 255 = none.
Answer `SendLapSectorTimeResponse` (0x0252): `[0x0252][requestId][1 = OK, 0 = NG]`.

## In the app

Settings -> Ride modes (read from bike, library on the phone, JSON export/import, send with a two-tap confirmation) and Settings -> Lap timer (debug).
The library file format is `RideModeFile` (see `YccuSdk/Yrc/ProfileLibrary.cs`).
