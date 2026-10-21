# Custom screens (Nav + VPB pages)

The custom screens on the navigation display and the Virtual Pit Board are built from **pages** of Avalonia controls. A screen is
one `.axaml` text per display, kept in the platform key-value store (`screen_doc_navigation` / `screen_doc_vpb`) and edited in
the app's *Screen editor* (Design / Pages / Code tabs) or exported, edited anywhere and imported again.

Code: `YccuSdk/Screens` (document, state, actions, validator, migration — no UI dependency, unit-tested in
`YccuSdk.Tests`) and `YammiC/YammiC/Screens` (the Avalonia renderer and the widgets).

## File format

```xml
<y:ScreenDocument xmlns="https://github.com/avaloniaui"
                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                  xmlns:y="clr-namespace:CNCO.YammiC.Screens;assembly=YammiC"
                  Target="Navigation" ZoomIn="NextPage" ZoomOut="PreviousPage">
  <y:Page Id="main" Name="Main" ZoomIn="GoTo:player">
    <y:ScreenCanvas Width="480" Height="240" Background="#F4F4F2" NightBackground="#000000">
      <y:ScreenBox Canvas.Left="0" Canvas.Top="0" Width="480" Height="144" Tag="Clock">
        <y:VarText Template="{}{time}" FontSize="120" FontWeight="Bold" TextAlignment="Center"
                   Foreground="#111111" NightForeground="#FFFFFF" VerticalAlignment="Center" />
      </y:ScreenBox>
    </y:ScreenCanvas>
  </y:Page>
</y:ScreenDocument>
```

- Design space: Nav 480x240, VPB 600x306 (only the strip at (0,163) 526x66 is visible on the bike; the editor shows and
  clamps to that).
- `ZoomIn` / `ZoomOut` on `<y:Page>` override the ones on `<y:ScreenDocument>`.
- Every page holds exactly one root control (normally `y:ScreenCanvas`).

## Default navigation screen

With nothing saved, the navigation display shows a *Now playing* page: track title, artist, album and elapsed/duration on the
left, the album art top-right (a note shows through while the track has none), and a progress bar along the bottom;
"No music playing" when nothing plays. Its zoom buttons skip tracks (`ZoomIn="Media.Next"`, `ZoomOut="Media.Previous"`), and it has
day and night colours. The pit board default is still the artist - song strip.

## Allowed content

Anything else is rejected (with a line number) before the XAML loader sees it (`ScreenXamlValidator`):

- Elements: `Canvas Grid StackPanel Border TextBlock Rectangle Ellipse` and the widgets `y:ScreenCanvas` (day/night
  background), `y:ScreenBox` (a box with `Background`/`NightBackground`, holds one child), `y:VarText` (`Template` with
  `{variables}`, `NightForeground`), `y:ScreenImage` (`Origin="Embedded"` with `Data` = base64 image, or `Origin="AlbumArt"` for the current track's
  cover; `Stretch`), `y:MediaProgress` (`Value`, `Fill`, `Track`, `NightFill`, `NightTrack`).
- Attributes only (no `<Border.Background>` property elements); the attribute list is in `ScreenXamlValidator`.
- Markup extensions: only a plain `{Binding ...}` (no converters, sources, `x:Static`, resources). Use `{}` in front of a
  value that must start with a brace (`Template="{}{time}"`).

## Bindings

Pages bind to `ScreenState`: `HasMusic IsPlaying IsPaused NoMusic Day Night Song Artist Album State PlayIcon Time Date Weekday
Elapsed Duration Progress PageId PageIndex PageNumber PageCount Now Flags[name] Vars[name]` and the bike data `Rpm Water Intake
Gear Tps Aps Bank Pitch AccelX AccelY FrontSpeed RearSpeed` (a dash when stale). `VarText` also understands the older
`{variable}` / `{variable:format}` templates (`ScreenVariables`).

## Bike buttons (events)

The zoom buttons are not zoom in UI mode: they raise `ZoomIn` / `ZoomOut` on the current page. Actions (no scripting):
`NextPage`, `PreviousPage`, `GoTo:<page id>`, `Media.PlayPause`, `Media.Next`, `Media.Previous`, `Toggle:<flag>`,
`Set:<name>=<value>`, `None`. A page's action wins over the screen's; no action means nothing happens. The bike's zoom buttons
are still acknowledged (it greys them out otherwise). Nav mode *Reels* keeps using them to scroll videos; *Sandwich* still
zooms the picture.

## Album art

The cover comes from the phone's media session (`IMediaSessionMonitor.GetArtwork()` in CNCO.Unify.Device: the session's art
bitmap, or a content/file URI it points to, scaled to 256 px) and is fetched when the track changes, retried for a few seconds
while a track has none. A remote (http) cover URL is not fetched.

## When the bike is not looking

Pictures are only sent while the bike is on the navigation screen. It says so with `START_CONTENT_UPDATE_REQUEST` (type 1) and
`STOP_CONTENT_UPDATE_REQUEST`; `NavImageGate` tracks them and `NavigationSdk.PushFrame` (the one path every mode uses: custom
screens, reels, the sandwich animation, and the idle resend) holds pictures back while the gate is closed. The last picture is
remembered and sent again on START. When the state changes the bike gets one status picture: "Rendering paused." after a STOP,
"Rendering paused (unknown state)" on a new connection until the first START - so if the bike's signals aren't what we think,
that message stays on the display and shows it. The screens skip rendering while closed; reels skip encoding.

## Refresh rate

Settings > *Nav refresh*: **Normal** (sender cap 15 fps, idle resend 2 fps, reels 8 fps at JPEG 50, screens at JPEG 85, checked every
1 s), **Low** (5 fps, 1 fps, 4 fps at JPEG 40, JPEG 70, every 2 s) or **Auto** (Low while the phone's Battery Saver is on).
The numbers are in `NavRefreshProfile`; the battery check is `IPowerMonitor` in CNCO.Unify.Device (`DeviceRuntime.PowerMonitor`).
A change applies at once; a playing reel restarts from its beginning to pick up the new rate. Custom screens only send when
something changed, so the setting mostly matters for reels, animations and the idle resend.

## Old layouts

The earlier JSON layouts (`screen_navigation` / `screen_vpb`) are converted to a one-page document on first load.

## Platforms

Pages are loaded with the run-time XAML loader, which needs dynamic code: Android (JIT or mixed AOT) and desktop work;
iOS and browser report "can't be drawn" and keep the last picture. The Android head roots the Avalonia and app assemblies
from the trimmer (`TrimmerRootAssembly` in `YammiC.Android.csproj`).

## Tests

`dotnet test YccuSdk.Tests/YccuSdk.Tests.csproj`
