# Track Timer, GPS logging and app navigation — design

## Intent
Let the rider time laps on a track with the phone, the way the official Y-Connect app does: GPS positions are logged, a start/finish line (and sectors) drawn on a map are crossed automatically, lap and sector times go to the bike, and the whole session is saved so it can be reviewed later (eventually played back on a map with bike telemetry). The app is also reorganised around a sidebar so the growing feature set has a home.

## Goals
- Record GPS (and selected CAN signals) while a track session runs; save sessions as files; export GPX.
- Define tracks on an online map: one start/finish line, up to 4 sectors. Save, load, import, export tracks. Pick the track to use.
- Auto-detect lap and sector crossings and send the times to the bike (existing lap-time message, 0x0251).
- Auto-select the nearest / last-used track when a session starts (optional setting).
- Once the bike reports "timer ready", **nothing the rider could change is changeable**; the rider cannot touch the phone on a track.
- Sidebar navigation (hamburger) grouping the features.
- Record sessions so that later playback on a map with telemetry needs no format change.

## Non-goals (for now)
- Offline map tiles / tile cache (TODO: "Offline map cache").
- Session playback UI (format is designed for it; UI comes later).
- External GPS receivers, cloud upload, rankings.
- Switching track mid-session.

## Anti-goals
- A prompt, dialog, permission request or screen change appearing while armed.
- Lap times that disagree with the bike's own idea of the lap without the rider being able to tell.

## Bike "timer ready" signal (found in the official app's wire handling)
- CAN ID **959 (0x03BF)**, **byte 4, bit 5** (`(b4 >> 5) & 1`) is the timer state: 1 = ready, 0 = not. Byte 4's low nibble is a separate "home info" value.
- Official behaviour on 0→1: vibrate the phone, show the track name on the VPB, start GPS timing, send the initial lap/sector message with the reference time. On 1→0: stop timing, store the result.
- The official app tracks three states: STANDSTILL (1), READY (2), MEASURING (3); MEASURING is derived by the app, not the bike.
- The official code reads this on a specific vehicle-type path; whether the user's bike uses the same ID is **unverified**. The session must log raw 0x03BF bytes so this can be checked, and the manual "Arm" button remains as a fallback.
- Consequence: 0x03BF must be added to the CAN request list whenever Track Timer is enabled (in addition to the user's chosen signals), at a low rate.

## Armed state (the lock)
- States: Idle → Ready (bike says ready, or rider arms manually) → Timing (first line crossing) → Idle (bike clears ready, or rider ends).
- While Ready/Timing: track selection, timer, GPS and recording settings are read-only; no dialogs, snackbars needing action or permission prompts; screen kept awake; leaving the page does not stop the session; rider-initiated disarm needs a deliberate action (long press).
- Permissions (location, notification) and the chosen track must be satisfied **before** arming; if not, arming from the bike is refused with a vibration pattern / VPB message rather than a prompt.
- State survives an app restart (persisted), so a crash mid-session resumes armed.

## Architecture
1. **GpsRecorder** (platform service behind an `ILocationSource` interface in YccuSdk; Android implementation uses `LocationManager` GPS provider, no Play Services). Emits `GpsFix` (UTC time, lat, lon, speed, bearing, accuracy, elapsed-realtime nanos). Runs under the existing foreground service (add the location foreground-service type and permissions).
2. **GPS frequency**: user setting with the rates the phone can deliver. Android exposes no list of supported rates, so a "Test GPS" step requests each candidate (1, 2, 5, 10 Hz) for a few seconds with a sky view, measures the achieved fix rate, and only offers rates achieved within tolerance. Result cached per device; default 5 Hz if it works, else 1 Hz.
3. **TrackLibrary**: `Track { Id, Name, StartFinish (two points), Sectors[0..4] (two points each, ordered), Created/LastUsed }`. JSON files in app storage; import/export as `.json`. Own plain classes with `System.Text.Json` (same approach as screen layouts), versioned.
4. **LapDetector**: for each pair of consecutive fixes, intersect the segment with the active line(s); interpolate the crossing time between the fixes (linear in time along the segment). Guards: minimum lap time, direction check (line is directed), ignore crossings with poor accuracy, sectors only valid in order. Pure function over fixes → unit testable with synthetic data.
5. **TrackSelector** (auto-select): on session start take the first good fix, compute distance to each track's start/finish midpoint, pick nearest within a threshold (default 500 m), tie-break by last used; otherwise leave unselected (rider chose beforehand). Cost is O(tracks) per call and runs once, not per fix. Setting can turn it off. No mid-session re-selection.
6. **TrackSession** recorder: file per session with header (track id/name, start time, GPS rate, signal list) and samples `{t, lat, lon, speed, bearing, acc, can{...}}`, plus lap/sector events. JSON lines or JSON with arrays (builder's choice); GPX export derived. CAN signals recorded are a separate "track recording" selection with their own rate, independent of the screen-builder signals.
7. **TimerController**: owns the state machine above, listens to the CAN timer-state bit, drives vibration, VPB track-name message and sends lap/sector messages through the existing `LapTimer`/`AppSendLapSectorTime` path (manual debug timer stays).
8. **Map editor**: online OpenStreetMap tiles in an Avalonia map control (Mapsui.Avalonia is the proposal; builder to confirm it works on Android with Avalonia 12.1 — fallback: a small custom tile control). Tap two points to draw a line, drag handles, add/remove sectors, name the track, save. Attribution shown as OSM requires; respect tile-usage policy (no bulk download; offline cache is a later TODO).

## Navigation (sidebar)
`SplitView` shell, hamburger toggles the pane. Entries: **Dashboard** (home, top — my call), **Bike Telemetry** (CAN config, data viewer), **Track** (Tracks, Timer, Sessions, track settings incl. GPS frequency and recording), **Y-Trac** (ride profiles), **Bike UI** (VPB, NavUI settings, screen editor/viewer), **Debug log** (its own entry, above Settings), **Settings** (bottom). Existing pages move under these without behaviour change. The debug log leaves the Dashboard and becomes its own page because a long log makes the UI lag: it must be virtualised (a recycling list, not one big text block), capped (ring buffer of the most recent N lines, default a few thousand), and only fed/rendered while the page is open; when closed it stops observing the log. Copy/clear/share stay. The screen-builder redesign (separate spec) lands under Bike UI.

## Settings (Track)
GPS frequency (tested rates only), auto-select track on/off + distance threshold, vibrate on ready on/off, recorded CAN signals + rate, keep screen awake while armed (default on), units.

## Error handling
GPS lost/poor accuracy while timing: keep timing, mark affected laps "uncertain", never block. Location permission denied: Track Timer disabled with an explanation shown outside the armed state. Bike message send failure: existing retry; lap kept locally. Corrupt track file: skipped with a message in the track list.

## Testing
Unit: line intersection and crossing-time interpolation, direction/minimum-lap guards, sector ordering, nearest-track selection, track JSON round-trip, GPX export, state machine (including restart while armed). Manual: GPS rate test, real laps on a track or a recorded drive, bike ready bit via logs, locked UI behaviour.

## Review corrections (SDK-session review, 2026-10-20)
- **Location in the background (decision):** `BikeForegroundService` is started by the Bluetooth ACL receiver with no Activity. On Android 12+ such a service cannot read location without `ACCESS_BACKGROUND_LOCATION`, and on 14+ the location service type needs fine location granted when `StartForeground` runs. Chosen approach: **Option A with Option B as fallback.** Track Timer asks for fine location, then background ("Allow all the time", via the settings screen), both **before** arming and never while armed. If background location is not granted, bike-triggered arming is unavailable and GPS starts only when the rider arms from the UI (Option B). Restart-while-armed likewise needs background location; without it the session ends cleanly on restart and the partial file is kept.
- Service mechanics: `[Service(ForegroundServiceType = TypeConnectedDevice | TypeLocation)]`, pass the combined type to `StartForeground`, add `FOREGROUND_SERVICE_LOCATION` and a `PARTIAL_WAKE_LOCK` held while armed. "Keep screen awake" applies only if the Activity is showing; the real path is screen off, phone pocketed.
- **CAN request:** `BikeService` sends one `CanIntervalRequest` with one interval for all ids, so 0x03BF cannot have its own rate (it rides at the shared 250 ms; a second request with another interval is untested and not used). The request must be sent when Track Timer is enabled even if the user's CAN-data toggle is off. Add a third internal "required ids" set merged into the existing `Distinct()` (`_canIds ∪ _canExtraIds ∪ required`); do not use `CanExtraIds` (user-visible, persisted) and do not extend `CanSignals.KnownIds`. Detect the ready bit by reading `CanTelemetry.Raw()` on its `Updated` event; treat data older than ~3 s as "unknown"; **never auto-disarm on stale data or disconnect**.
- **VPB/Nav collision:** `BikeService` owns the custom screen presenters. While Ready/Timing the Track Timer needs a "board override" hook in `BikeService` that pauses the custom VPB presenter so the track-name message is not overwritten; define it jointly with the SDK session. Nav images go through `NavImageGate` in `NavigationSdk.PushFrame`.
- **Persistence:** key-value store only for the small armed record (armed flag, track id, session id). Session files are **JSON Lines**, appended and flushed per sample so a crash leaves a valid file. Tracks and sessions need a new platform file-store interface in YccuSdk (with an in-memory fake for tests). `TimerController` takes an injectable clock, vibrator and VPB sender; lap timing uses the fix's elapsed-realtime nanos, not wall clock.
- **Shared files/packages:** do not add members to `IBikeController`; Track Timer gets its own interface. Mapsui.Avalonia is spiked first on the Android head (SkiaSharp is pinned to match Avalonia.Skia 12.1; Mapsui brings its own); the custom tile control is the safer default. The `SplitView` content area is a plain presenter (no wrapping `ScrollViewer`) so editor touch-drag keeps working. Unit tests go in `YccuSdk.Tests`. A location monitor in Unify.Device would follow the power-monitor pattern (interface, unsupported stub, Android impl).

## Decisions I made (please check)
- Max 4 sectors, i.e. start/finish plus up to 3 intermediate sector lines (the bike's lap message carries 4 sector gaps). Implemented in `YccuSdk/Tracks` (namespace `CNCO.YccuSdk.Tracks`, since `Track` clashes with a `Track` namespace).
- Auto-select threshold 500 m, once at session start, no mid-session switching.
- GPS rates offered: 1/2/5/10 Hz, filtered by measured support.
- Mapsui + OSM tiles; Dashboard kept as home entry; Y-Trac its own entry.
- The bike's ready bit assumed to be CAN 0x03BF b4 bit5 until verified on the user's bike.

## Open items for the builder
- Exact vehicle-type difference in the official ready handling (which bikes use 0x03BF).
- Whether the lap message needs per-sector gap values and reference times from the best lap; reuse existing `AppSendLapSectorTime` semantics.
- Android specifics: foreground-service location type, background-location need, wake lock.

## TODO
- Offline map cache.
- Session playback on a map with telemetry.
