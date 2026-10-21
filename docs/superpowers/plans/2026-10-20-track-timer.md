# Track Timer, GPS logging and sidebar — implementation plan

Spec: `docs/superpowers/specs/2026-10-20-track-timer-design.md` (includes the SDK-session review corrections). Implemented inline (no agents). Android builds are slow: test the SDK with `dotnet test YccuSdk.Tests/YccuSdk.Tests.csproj`, build the Android head once per phase batch. No commits unless asked.

## Phase 1 — pure SDK logic (`YccuSdk/Track/`, tests in `YccuSdk.Tests`)
1. `GeoMath`: local planar projection (metres around a reference point), segment intersection returning the fraction along the movement segment, haversine distance.
2. `TrackModels`: `GeoPoint`, `TrackLine`, `Track` (id, name, start/finish, 0..4 sectors, created, last used, version); JSON (System.Text.Json, own classes), validation.
3. `ITrackFileStore` (list/read/write/append/delete named files in "tracks" and "sessions") + `InMemoryTrackFileStore` for tests; `TrackLibrary` (list, save, load, delete, import/export JSON, mark used, corrupt files skipped).
4. `LapDetector`: feed `GpsFix` (UTC, lat, lon, speed, bearing, accuracy, elapsed-realtime ns); detects directed start/finish and ordered sector crossings with interpolated crossing time; guards (minimum lap, direction, accuracy); emits `LapEvent`/`SectorEvent`.
5. `TrackSelector`: nearest start/finish midpoint within threshold, ties by last used.
6. `SessionWriter`: JSON Lines (header, sample, lap/sector events), append + flush per record; `GpxExporter` from a session file; reader for the viewer later.
7. `TimerController` state machine (Idle → Ready → Timing → Idle), `ITimerClock`, `IVibrator`, `IBoardMessageSender`, armed record persisted via `IYmcKeyValueStore`; restart-while-armed resume; never disarms on stale data.
8. `ReadyBitDetector`: reads CAN 0x03BF raw (byte 4 bit 5), freshness 3 s → unknown.
9. Tests for each of the above (synthetic fixes for crossings, JSONL crash-valid, state machine incl. restart).

## Phase 2 — SDK integration
1. `BikeService`: internal "required ids" set merged into the CAN interval request; request sent when Track Timer is enabled regardless of the user's CAN toggle; expose `CanTelemetry.Raw()` updates to the detector.
2. Board override hook (pause custom VPB presenter while Ready/Timing; track name via VPB) — coordinate with the SDK session; Nav via `NavImageGate`.
3. Lap/sector send via existing `AppSendLapSectorTime` (best-lap reference, sector gaps).
4. `ITrackTimer` interface (own, not `IBikeController`) + `FakeTrackTimer`.
5. `ILocationSource` + GPS rate test logic (`GpsRateProbe`: candidates 1/2/5/10 Hz, measured, tolerance).

## Phase 3 — Android platform
1. `AndroidLocationSource` (`LocationManager` GPS provider, rate from setting), `AndroidTrackFileStore` (app files dir), vibrator.
2. Manifest: fine + background location, `FOREGROUND_SERVICE_LOCATION`, wake lock; `BikeForegroundService` type `connectedDevice|location`, combined type in `StartForeground`, `PARTIAL_WAKE_LOCK` while armed.
3. Permission flow (fine, then background via settings) requested only outside the armed state; fallback to UI-arming if background is denied.

## Phase 4 — UI (shared Avalonia project)
1. Sidebar shell (`SplitView`, plain content presenter — no `ScrollViewer`): Dashboard, Bike Telemetry, Track, Y-Trac, Bike UI, Debug log, Settings (bottom). Move existing pages under these without behaviour change.
2. Debug log page: virtualised list, ring buffer, observes only while open; removed from the Dashboard.
3. Track pages: track list (select, delete, import/export), map editor, timer page (state, current lap, laps), sessions list, track settings (GPS frequency from tested rates, auto-select + threshold, vibrate, recorded signals).
4. Map: custom OSM tile control (default); Mapsui spiked separately and only adopted if SkiaSharp versions work.
5. Armed lock: all editing controls disabled while Ready/Timing; no dialogs.

## Phase 5 — verification
Run `dotnet test YccuSdk.Tests`; one Android + Desktop build; report what is untested on a bike (0x03BF bit, GPS rates, line crossings on a real track).
