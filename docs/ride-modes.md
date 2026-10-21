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

`LoadYrcSettingsData` (0x0208): `[0x0208][len 3B][requestId][protobuf]` with data kinds [NAMES, CURRENT], start_trigger = 2 (app) and the settings list (mode, saved number, type, name, kind + current for each value).

**Transport: DDT v3, not the control/data DDT session.** Sent through the ordinary DDT session (control `56851826` / data `E998AEAB`) the bike completes the transfer at the Bluetooth level but never answers with 0x0209 and does not apply anything (seen in a device log, 2026-10-21). The reference SDK sends the ride-mode messages (`LoadYrcSettingsData`, and the answers to the bike's saved-settings requests) as v3 packets written to characteristic `7690EEF4-F3A1-11EC-B939-0242AC120002`: the message is split into packets of at most `MTU - 7` data bytes, each `[sequence u16 BE][index from 1][count][data]` with the same sequence for all packets of one message (the sequence counts up from 1 per message and skips 0), written one at a time after the previous write completed. In YammiC this is `Transport.DdtV3` / `YmcBikeProtocol.DdtV3WritePackets`. Untested against a bike at the time of writing.

**Built-in modes cannot be changed.** The bike lists MT1, MT2, MT3, MT5 and TR1-TR4 on the test bike; the Rain, Sport and Street modes (MT1-MT3) are fixed. Only the Track modes and the rider's Custom mode (named "CUSTOM" by the bike) take changes, so the app refuses to send to the others (`YrcNames.IsEditable`). Each mode read from the bike is logged with its type, saved number, name and values so this rule can be checked against what the bike reports.

The library entries can be edited on the phone (name on the bike, value per setting within the bike's min/max) before sending.
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

## The bike's own export and import (menu on the bike)

The bike has its own menu for moving a mode to and from the phone. This is probably the way sending to the bike is meant to work: a load the phone starts on its own (above) was never answered in the device logs.

**The ride-mode characteristic `AB8D233A` carries short, direct messages**, not DDT fragments: `[cmd 2B][length 3B][request id 1B][protobuf]`. Earlier versions fed it to the DDT assembler, which is why the bike's import requests and the load result (0x0209) never showed in the log. The bike sends three things on it: 0x0205 (list what is saved), 0x0207 (load one saved entry) and 0x0209 (result of a load).

**Export** (the rider saves a mode to the phone, 0x0203): arrives as a bike-to-phone DDT message with the mode's name and current values (the ranges are 0 in it). The phone keeps it in the library (the ranges are filled in from what was read from the bike) and answers with a simple write `[0x0204][bike request id][result 4B big-endian][name]`; result 0 is OK.

**Import** (the rider loads a mode from the phone):

1. The bike sends 0x0205. Request protobuf: 1 data kinds, 2 modes, 3 mode type, 4 flag, 5 repeated `{6 saved number}`. Seen: kinds [2] (names), mode MT5, type 1, flag 2.
2. The phone answers 0x0206 as DDT v3 packets: `[0x0206][length 3B][the bike's request id][info protobuf]` with one setting per saved entry that fits (`mode`, `app_saved_no`, `yrc_mode_type`, `name`; values only if the bike asked for them). Nothing to offer: result 515.
3. The rider picks one: 0x0207 with the saved number(s) in field 5.
4. The phone answers with 0x0208 as DDT v3, with the bike's request id and start trigger 1 (the bike), the chosen entry as the requested mode. Not found: result 513 only. The bike should then confirm with 0x0209.

The saved number is the entry's place in the library, counted from 1 (so it changes if entries are deleted); an entry fits a request when its type matches and its mode is the requested mode (the Track modes count as one family).

Everything in this section except the request shapes seen in the logs is from the layout of the reference messages and is **untested against a bike**; the log prints each request in hex ("0x0205 from the bike: ...") and what the phone answered.

### What the reference app sends for a load (checked against its decompile, 2026-10-21)

- Answering the bike's import (0x0207): data kinds **[current, names]** in that order, start trigger 1 (bike), one setting with the mode **the bike asked for**, the mode type, the name **without trailing spaces**, and the values as `kind` + `current` only. **No saved number.** Our first attempt sent kinds [names, current] (then echoed the bike's [current]), the name padded to ten characters and the saved number; the bike rejected it without a reply.
- A load the app starts itself: data kinds **[current]** only, start trigger 2 (app), the same setting layout.
- Nothing found: only a result (the reference app uses 0x201 / 513 for "not found" and 0x207 for "could not fetch").
