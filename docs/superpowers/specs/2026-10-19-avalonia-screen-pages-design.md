# Avalonia-rendered screen pages (Nav + VPB) — design

## Goal
Replace the Skia-drawn, single-layout screen builder with screens built from **pages** of real Avalonia controls, authored as `.axaml`, edited in the app (visual editor + raw AXAML tab), with the bike's buttons delivered as **events** that pages handle (e.g. change page). Stays cross-platform (Avalonia off-screen render, no Android-specific code).

## What was agreed (user)
- Authoring = a mix of runtime-loaded `.axaml` files and an in-app editor; `.axaml` is the source of truth, the editor is a front end.
- Editor scope = fixed widget set + raw AXAML tab. Anything fancier is written as AXAML by hand (user may use an online editor).
- Zoom in/out must not zoom the UI; they are events screens handle. Multiple pages, switched by events.
- Goal includes building custom screens such as a music-player-style page.

## Assumptions (to confirm)
- Existing `ScreenLayout` JSON layouts are migrated on first load (converted to equivalent AXAML), not discarded.
- Reels mode keeps working; zoom events still scroll videos while Nav mode = Reels.
- VPB keeps its visible strip (0,163,526,66 of 600x306), low fps, existing JPEG/SPP pipeline.

## Architecture

### Units
1. **ScreenDocument** (`YccuSdk/Screens`): per target, holds `Pages` (id, name, AXAML text) and the target's size. Persisted as `.axaml` text in the platform key-value store; import/export as a file. Replaces `ScreenLayout`/`ScreenLayoutStore` JSON.
2. **ScreenState** (`YccuSdk/Screens`): the binding source. Properties: time/date, media (song, artist, album, position, duration, playing), night, zoom-free CAN values, `Page`, `PageIndex`, `PageCount`, user flags/variables. Raises `INotifyPropertyChanged`. Replaces `ScreenVariables`; `{name}` templates map to bindings during migration.
3. **ScreenEvents + ScreenActions**: bike input (`ZoomIn`, `ZoomOut`, later others) enters through `ScreenEventBus`. Pages declare handlers with an attached property (`y:Events.ZoomIn="NextPage"`, `y:Events.ZoomOut="GoTo:Player"`). Actions are a fixed set: `NextPage`, `PreviousPage`, `GoTo:<page>`, `Media.PlayPause/Next/Previous`, `Toggle:<flag>`, `Set:<var>=<value>`. No scripting. If the active page does not handle an event, a document-level default applies, else the event is ignored (and in Reels mode routed to the reels controller).
4. **ScreenHost** (UI-thread, shared project): loads a page's AXAML with `AvaloniaRuntimeXamlLoader`, sets `DataContext = ScreenState`, measures/arranges at the design size, renders to `RenderTargetBitmap`, converts to `SKBitmap`/JPEG for `NavigationSdk.PushImage` / VPB builder. Crops to the visible area for VPB. Re-renders on state change (debounced to the target fps) and page change. Parse errors fall back to the last good page and surface in the editor.
5. **Widgets** (`Y.Screens.Controls`, registered for the loader): `Clock`, `AlbumArt`, `MediaProgress` plus stock `Canvas/Grid/StackPanel/TextBlock/Image/Border/Rectangle/Ellipse/ProgressBar`. Only whitelisted types/properties are accepted when loading (guards against arbitrary XAML doing I/O).
6. **Editor** (`ScreenEditorView`): page list (add/rename/delete/reorder), design surface (select/move/resize with bounds clamping and only the visible VPB area, as today), property panel for the fixed widgets, events panel (event → action per page), preview context (night, playing/paused), a raw AXAML tab (two-way: visual edits rewrite the text; text edits reparse, invalid text is flagged and does not overwrite), import/export `.axaml`.
7. **Presenter**: `ScreenPresenter` keeps its send-on-change/retry behaviour but takes frames from `ScreenHost` and invalidates on `ScreenState` or page change.

### Data flow
bike button → `BikeService.OnZoomRequest` → (Reels? reels controller : `ScreenEventBus`) → page handler → action → `ScreenState.Page` changes → `ScreenHost` re-renders → `ScreenPresenter` sends. Zoom-level reply to the bike is still sent (so its buttons stay enabled) but no longer scales the image in UI mode.

### Platform notes
- Rendering must run on the Avalonia UI thread; the service (headless, no window) needs Avalonia initialised. If the app process has no Avalonia lifetime when the service runs (background connection), the host must create/own a minimal one — **first spike**.
- Runtime XAML loader uses reflection: add trimmer roots for the widget assemblies; verify a Release build on Android early. Fallback if blocked: same document model, pages built by a JSON → control-tree mapper instead of the XAML loader.

### Review notes (UI/Android head owner)
- `YammiC.Android/Application.cs` creates the whole SDK (broker, `BikeService`, nav) in `Application.OnCreate`, before any Activity; the foreground service only keeps the process alive. So the renderer runs in a process that may have no Activity. Avalonia's Android dispatcher is the main looper, which exists without an Activity, but whether `RenderTargetBitmap` works before/without an `AvaloniaView` is **unverified** — the spike must run with the Activity destroyed (swipe-away + bike reconnect), not just backgrounded.
- Runtime loader: `AvaloniaRuntimeXamlLoader` is in the separate package `Avalonia.Markup.Xaml.Loader` (not referenced today; add it to `YammiC.csproj` at the Avalonia 12.1.x version) and uses System.Reflection.Emit, so it works under Mono JIT but **not** with full AOT. The Android csproj only disables profiled AOT; check Release has no `RunAOTCompilation`. Runtime-loaded XAML gets no compiled bindings (the project sets `AvaloniaUseCompiledBindingsByDefault`), so use reflection bindings and root `ScreenState` and the widget types for the trimmer.
- The whitelist cannot be enforced by the loader itself: pre-parse the AXAML (XDocument) and reject unknown elements/properties/markup extensions before calling the loader.
- Migration must keep what `ScreenVariables`/`ScreenPresenter` do today: `{name:format}` specifiers (e.g. `%`), CAN values showing "--" when stale (3 s), the fast tick (500 ms) only when a page uses CAN values, and the VPB minimum update interval.
- Editor preview currently uses `CanValues.Sample` and `PreviewNight`; keep an equivalent preview `ScreenState`.

## Error handling
Bad AXAML → keep last good page, show message in editor, log it. Unknown element/property → rejected with line info. Render exceptions never reach the sender loop.

## Testing
Unit-test (no UI needed): migration of old layouts, action parsing/dispatch, page state machine, whitelist validation. Manual: render fidelity on phone, off-screen render while the app is backgrounded, Release build load, bike button → page change on hardware.

## Build order (for the plan)
1. Spike: off-screen Avalonia render from the service while the app is backgrounded (go/no-go), plus Release-build runtime loader check.
2. ScreenState + ScreenHost + one hard-coded page rendering to the Nav display.
3. Document model, persistence, migration of old layouts.
4. Events/actions + multi-page.
5. Widgets.
6. Editor (visual + raw tab).
7. VPB crop/low-fps path; Reels routing.

## TODO / out of scope now
- Evaluate an existing open-source XAML/visual editor to embed instead of ours.
- Full visual designer for arbitrary controls, styles, animations.
- More bike events beyond zoom in/out.
- Move Reels Mode into a page built with this system (likely needs scripting, which is also out of scope now).

## Deviations made while planning/implementing
- Events are attributes on `<y:Page>` / `<y:ScreenDocument>` (`ZoomIn="NextPage"`), not an attached property.
- The widget set is `ScreenCanvas` / `ScreenBox` / `VarText` / `ScreenImage` / `MediaProgress` plus stock layout controls; stock `Image` and `ProgressBar` were replaced (file access, control templates). Day/night colours are widget properties (`NightForeground`, `NightBackground`), not duplicated elements.
- `AlbumArt` is not in the first version: `YmcMediaMetaData` has no artwork.
- In UI mode the zoom buttons only raise page events; Sandwich mode still zooms the picture.
