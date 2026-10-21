# Avalonia Screen Pages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Skia-drawn single-layout screen builder with screens made of pages of real Avalonia controls, authored as `.axaml`, edited in the app, with the bike's zoom buttons delivered as events that pages handle.

**Architecture:** Pure, testable logic (document model, XAML whitelist, actions/page state, migration, edit model, `ScreenState`) lives in `YccuSdk` behind an `IScreenRenderer` interface. The Avalonia-dependent renderer (runtime XAML loader + `RenderTargetBitmap`) and custom widgets live in the shared UI project `YammiC/YammiC`. `ScreenPresenter` pulls the current page + a state snapshot, asks the renderer for a bitmap and sends the JPEG as today.

**Tech Stack:** .NET 10, Avalonia 12.1.4 (+ `Avalonia.Markup.Xaml.Loader`), SkiaSharp, CommunityToolkit.Mvvm, xunit (new test project).

**Spec:** `docs/superpowers/specs/2026-10-19-avalonia-screen-pages-design.md` (read its "Review notes" too).

## Global Constraints

- Solution root `C:\Storage\Development\GitHub\Y-Connect\YammiC\`. All commands run from there unless stated.
- `YccuSdk` must stay free of Avalonia references (it multi-targets net10.0, android, ios, browser).
- Central package management: versions live in `Directory.Packages.props`; `.csproj` files carry no versions.
- Do not add packages to CNCO.Unify (`C:\Storage\Development\Neptune\Lib\CNCO.Unify`).
- Keep the hardcoded dev credentials in `YmcBikeSessionStore.cs` untouched (marked "DO NOT COMMIT").
- **Do not run `git commit`/`git add`.** A peer session shares this working tree and the user has not asked for commits; leave changes uncommitted and report the file list at the end.
- Edits to `IBikeController` are limited to the three screen members (`GetScreenLayout`/`SaveScreenLayout`/`ResetScreenLayout`) because other work touches that file.
- Runtime XAML must pass `ScreenXamlValidator` before it reaches the loader (the loader cannot enforce a whitelist).
- Runtime-loaded XAML uses reflection bindings (no compiled bindings); `ScreenState` and the widgets must survive trimming.
- No scripting. Event actions are the fixed set in Task 4.
- The reels flow (`NavMode.Reels`) must keep working unchanged.
- Build check for the Android head: `dotnet build YammiC/YammiC.Android/YammiC.Android.csproj -c Debug` (expect `0 Error(s)`). The head can only be exercised on the phone (serial `df85e93c`, `adb -s df85e93c`).

## Review Focus

1. A page with bad AXAML (typo, unknown element, banned binding) while the bike is connected: last good page keeps showing, no crash, editor shows the message with a line number (Tasks 3, 7, 11).
2. Zoom event arriving when the page has no handler / the document has one page / the page id in `GoTo:` doesn't exist: nothing breaks, bike still gets its zoom-level reply (Tasks 4, 8).
3. Old saved JSON layouts and layouts with `{time:HH:mm}`, literal `{{`, night colours and images: appear identically after migration (Task 5).
4. Elements dragged beyond the VPB's visible strip or typed into the raw tab with huge sizes: clamped by the visual editor; raw tab is trusted but still validated (Tasks 9, 11).
5. Renderer called while the app has no Activity (swiped away) and on platforms without dynamic code (iOS/browser): returns null with a message, never throws into the sender loop (Tasks 2, 7).

---

### Task 1: Test project and `ScreenSnapshot` / `ScreenState`

**Files:**
- Create: `YccuSdk.Tests/YccuSdk.Tests.csproj`
- Create: `YccuSdk/Screens/ScreenSnapshot.cs`
- Create: `YccuSdk/Screens/ScreenState.cs`
- Create: `YccuSdk.Tests/ScreenStateTests.cs`
- Modify: `Directory.Packages.props` (test packages), `YammiC.slnx`

**Interfaces:**
- Produces:
  - `record ScreenSnapshot(ScreenContext Context, string PageId, int PageIndex, int PageCount, IReadOnlyDictionary<string,bool> Flags, IReadOnlyDictionary<string,string> Vars)` with `static ScreenSnapshot Of(ScreenContext context, string pageId = "main", int pageIndex = 0, int pageCount = 1)` and `string Signature(bool includeSeconds)`.
  - `sealed class ScreenState : INotifyPropertyChanged` with `void Apply(ScreenSnapshot)`, `string Resolve(string? template)`, properties `Now, Time, Date, Weekday, Song, Artist, Album, State, PlayIcon, Elapsed, Duration, Progress (double 0..1), Night, Day, HasMusic, NoMusic, IsPlaying, IsPaused, PageId, PageIndex, PageNumber, PageCount, Rpm, Water, Intake, Gear, Tps, Aps, Bank, Pitch, AccelX, AccelY, FrontSpeed, RearSpeed, Flags (StateBag<bool>), Vars (StateBag<string>)`.
  - `sealed class StateBag<T> : INotifyPropertyChanged` with indexer `T this[string key]` (default for missing) and `Replace(IReadOnlyDictionary<string,T>)`.

- [ ] **Step 1: Add test packages and the project**

Append to `Directory.Packages.props` inside the existing `<ItemGroup>`:

```xml
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.0" />
```

Create `YccuSdk.Tests/YccuSdk.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$(YmcDotNetVersion)</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>CNCO.YccuSdk.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\YccuSdk\YccuSdk.csproj" />
  </ItemGroup>
</Project>
```

Add `<Project Path="YccuSdk.Tests/YccuSdk.Tests.csproj" />` to `YammiC.slnx` next to the `YccuSdk` project line.

- [ ] **Step 2: Write the failing tests**

`YccuSdk.Tests/ScreenStateTests.cs`:

```csharp
using System.ComponentModel;
using CNCO.YccuSdk.Models;
using CNCO.YccuSdk.Screens;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class ScreenStateTests
{
    private static readonly DateTime Noon = new(2026, 10, 19, 12, 5, 9);

    private static ScreenContext Ctx(YmcMediaMetaData? media = null, bool night = false, DateTime? now = null) =>
        new(now ?? Noon, media, night);

    private static YmcMediaMetaData Song(YmcMediaPlaybackState state = YmcMediaPlaybackState.Playing, int elapsed = 83) =>
        new("Queen", "Opera", "Bohemian", 355, state, 1f, elapsed);

    [Fact]
    public void Apply_RaisesPropertyChangedForEverything()
    {
        var state = new ScreenState();
        string? raised = null;
        state.PropertyChanged += (_, e) => raised = e.PropertyName;
        state.Apply(ScreenSnapshot.Of(Ctx()));
        Assert.Equal(string.Empty, raised);
    }

    [Fact]
    public void NoMedia_ReportsNoMusic()
    {
        var state = new ScreenState();
        state.Apply(ScreenSnapshot.Of(Ctx()));
        Assert.True(state.NoMusic);
        Assert.False(state.HasMusic);
        Assert.Equal(0, state.Progress);
        Assert.Equal("", state.Song);
    }

    [Fact]
    public void Playing_ExposesTrackAndProgress()
    {
        var state = new ScreenState();
        state.Apply(ScreenSnapshot.Of(Ctx(Song())));
        Assert.True(state.HasMusic);
        Assert.True(state.IsPlaying);
        Assert.False(state.IsPaused);
        Assert.Equal("Bohemian", state.Song);
        Assert.Equal(83d / 355d, state.Progress, 3);
        Assert.Equal("1:23", state.Elapsed);
    }

    [Fact]
    public void Paused_IsPausedNotPlaying()
    {
        var state = new ScreenState();
        state.Apply(ScreenSnapshot.Of(Ctx(Song(YmcMediaPlaybackState.Paused))));
        Assert.True(state.IsPaused);
        Assert.False(state.IsPlaying);
    }

    [Fact]
    public void NightAndDay_AreOpposites()
    {
        var state = new ScreenState();
        state.Apply(ScreenSnapshot.Of(Ctx(night: true)));
        Assert.True(state.Night);
        Assert.False(state.Day);
    }

    [Fact]
    public void Resolve_KeepsTheTemplateLanguage()
    {
        var state = new ScreenState();
        state.Apply(ScreenSnapshot.Of(Ctx(Song())));
        Assert.Equal("Queen - Bohemian 12:05", state.Resolve("{artist} - {song} {time}"));
        Assert.Equal("{x}", state.Resolve("{{x}}"));
    }

    [Fact]
    public void Flags_DefaultFalseAndFollowSnapshot()
    {
        var state = new ScreenState();
        state.Apply(ScreenSnapshot.Of(Ctx()) with { Flags = new Dictionary<string, bool> { ["menu"] = true } });
        Assert.True(state.Flags["menu"]);
        Assert.False(state.Flags["other"]);
    }

    [Fact]
    public void Signature_IgnoresSecondsUnlessAsked()
    {
        var a = ScreenSnapshot.Of(Ctx(now: Noon));
        var b = ScreenSnapshot.Of(Ctx(now: Noon.AddSeconds(20)));
        Assert.Equal(a.Signature(false), b.Signature(false));
        Assert.NotEqual(a.Signature(true), b.Signature(true));
    }

    [Fact]
    public void Signature_ChangesWithSongNightAndPage()
    {
        var baseSnap = ScreenSnapshot.Of(Ctx(Song()));
        Assert.NotEqual(baseSnap.Signature(false), ScreenSnapshot.Of(Ctx(Song() with { SongName = "Other" })).Signature(false));
        Assert.NotEqual(baseSnap.Signature(false), ScreenSnapshot.Of(Ctx(Song(), night: true)).Signature(false));
        Assert.NotEqual(baseSnap.Signature(false), ScreenSnapshot.Of(Ctx(Song()), "page2", 1, 2).Signature(false));
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test YccuSdk.Tests/YccuSdk.Tests.csproj`
Expected: build FAIL (`ScreenState`/`ScreenSnapshot` not found).

- [ ] **Step 4: Implement**

`YccuSdk/Screens/ScreenSnapshot.cs`:

```csharp
using System.Globalization;

namespace CNCO.YccuSdk.Screens;

/// <summary>Everything a page can show at one moment, as plain data (safe to hand between threads).</summary>
public sealed record ScreenSnapshot(
    ScreenContext Context,
    string PageId,
    int PageIndex,
    int PageCount,
    IReadOnlyDictionary<string, bool> Flags,
    IReadOnlyDictionary<string, string> Vars)
{
    private static readonly IReadOnlyDictionary<string, bool> NoFlags = new Dictionary<string, bool>();
    private static readonly IReadOnlyDictionary<string, string> NoVars = new Dictionary<string, string>();

    public static ScreenSnapshot Of(ScreenContext context, string pageId = "main", int pageIndex = 0, int pageCount = 1) =>
        new(context, pageId, pageIndex, pageCount, NoFlags, NoVars);

    /// <summary>
    /// Changes whenever something a page could show changes. The clock only counts to the minute unless
    /// <paramref name="includeSeconds"/>; bike data is included as the text the pages would show (so "--" for stale values).
    /// </summary>
    public string Signature(bool includeSeconds)
    {
        var c = Context;
        var m = c.Media;
        var now = includeSeconds ? c.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : c.Now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        var can = string.Join(',', ScreenVariables.CanVariableNames.Select(n => ScreenVariables.Resolve("{" + n + "}", c)));
        var flags = string.Join(',', Flags.OrderBy(f => f.Key).Select(f => $"{f.Key}={f.Value}"));
        var vars = string.Join(',', Vars.OrderBy(v => v.Key).Select(v => $"{v.Key}={v.Value}"));
        return $"{PageId}|{PageIndex}/{PageCount}|{now}|{(c.Night ? 'N' : 'D')}|" +
               $"{m?.SongName}|{m?.ArtistName}|{m?.AlbumName}|{m?.PlaybackState}|{m?.ElapsedSeconds}|{m?.DurationSeconds}|{can}|{flags}|{vars}";
    }
}
```

`YccuSdk/Screens/ScreenState.cs`:

```csharp
using System.ComponentModel;
using CNCO.YccuSdk.Models;

namespace CNCO.YccuSdk.Screens;

/// <summary>A dictionary a page can bind to (<c>{Binding Flags[menu]}</c>): missing keys read as the default value.</summary>
public sealed class StateBag<T> : INotifyPropertyChanged
{
    private IReadOnlyDictionary<string, T> _values = new Dictionary<string, T>();

    public event PropertyChangedEventHandler? PropertyChanged;

    public T this[string key] => _values.TryGetValue(key, out var v) ? v : default!;

    public void Replace(IReadOnlyDictionary<string, T> values)
    {
        _values = values;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}

/// <summary>
/// What pages bind to (<c>{Binding Song}</c>, <c>{Binding IsPlaying}</c>, ...). Updated with <see cref="Apply"/> on the thread
/// that owns the controls; every update raises "all properties changed".
/// </summary>
public sealed class ScreenState : INotifyPropertyChanged
{
    private ScreenSnapshot _snapshot = ScreenSnapshot.Of(new ScreenContext(DateTime.Now, null, false));

    public event PropertyChangedEventHandler? PropertyChanged;

    public StateBag<bool> Flags { get; } = new();
    public StateBag<string> Vars { get; } = new();

    public void Apply(ScreenSnapshot snapshot)
    {
        _snapshot = snapshot;
        Flags.Replace(snapshot.Flags);
        Vars.Replace(snapshot.Vars);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    /// <summary>Expands <c>{variables}</c> in <paramref name="template"/> (see <see cref="ScreenVariables"/>).</summary>
    public string Resolve(string? template) => ScreenVariables.Resolve(template, _snapshot.Context);

    private ScreenContext C => _snapshot.Context;
    private YmcMediaMetaData? Media => C.HasMusic ? C.Media : null;
    private string V(string name) => ScreenVariables.Resolve("{" + name + "}", C);

    public DateTime Now => C.Now;
    public string Time => V("time");
    public string Date => V("date");
    public string Weekday => V("weekday");
    public string Song => V("song");
    public string Artist => V("artist");
    public string Album => V("album");
    public string State => V("state");
    public string PlayIcon => V("playicon");
    public string Elapsed => V("elapsed");
    public string Duration => V("duration");
    public double Progress => Media is { DurationSeconds: > 0 } m ? Math.Clamp((double)m.ElapsedSeconds / m.DurationSeconds, 0, 1) : 0;

    public bool Night => C.Night;
    public bool Day => !C.Night;
    public bool HasMusic => C.HasMusic;
    public bool NoMusic => !C.HasMusic;
    public bool IsPlaying => C.IsPlaying;
    public bool IsPaused => C.HasMusic && !C.IsPlaying;

    public string PageId => _snapshot.PageId;
    public int PageIndex => _snapshot.PageIndex;
    public int PageNumber => _snapshot.PageIndex + 1;
    public int PageCount => _snapshot.PageCount;

    public string Rpm => V("rpm");
    public string Water => V("water");
    public string Intake => V("intake");
    public string Gear => V("gear");
    public string Tps => V("tps");
    public string Aps => V("aps");
    public string Bank => V("bank");
    public string Pitch => V("pitch");
    public string AccelX => V("accelx");
    public string AccelY => V("accely");
    public string FrontSpeed => V("frontspeed");
    public string RearSpeed => V("rearspeed");
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test YccuSdk.Tests/YccuSdk.Tests.csproj`
Expected: 8 passed. (If the test project fails to restore packages on the offline machine, report it; do not change versions silently.)

---

### Task 2: Spike — Avalonia off-screen renderer, widgets, go/no-go (GATE)

This task decides whether the rest of the plan proceeds. If any go/no-go check fails, **stop and report to the user** with the fallback from the spec (same document model, pages built from a JSON→control mapper instead of the XAML loader).

**Files:**
- Create: `YccuSdk/Screens/IScreenRenderer.cs`
- Create: `YccuSdk/Screens/ScreenPage.cs` (minimal now, extended in Task 3)
- Create: `YammiC/YammiC/YammiC/Screens/AvaloniaScreenRenderer.cs`
- Create: `YammiC/YammiC/YammiC/Screens/Widgets.cs`
- Create: `YammiC/YammiC/YammiC.Android/RenderSpike.cs` (throwaway; deleted in Task 7)
- Modify: `Directory.Packages.props`, `YammiC/YammiC/YammiC/YammiC.csproj`, `YammiC/YammiC/YammiC.Android/YammiC.Android.csproj`, `YammiC/YammiC/YammiC.Android/Application.cs`

**Interfaces:**
- Consumes: `ScreenSnapshot`, `ScreenState` (Task 1).
- Produces:
  - `interface IScreenRenderer { string? LastError { get; } Task<SKBitmap?> RenderAsync(ScreenTarget target, ScreenPage page, ScreenSnapshot snapshot, int width, int height); }`
  - `class ScreenPage { string Id; string Name; string Xaml; }` (extended in Task 3)
  - `AvaloniaScreenRenderer : IScreenRenderer` with `bool IsSupported`.
  - Widgets in namespace `CNCO.YammiC.Screens`, assembly `YammiC` (XAML prefix `y`): `VarText` (Template, NightForeground), `ScreenBox` (NightBackground), `ScreenCanvas` (NightBackground), `ScreenImage` (Data), `MediaProgress` (Value, Fill, Track).

- [ ] **Step 1: Add the runtime loader package**

In `Directory.Packages.props` add `<PackageVersion Include="Avalonia.Markup.Xaml.Loader" Version="12.1.4" />`. In `YammiC/YammiC/YammiC/YammiC.csproj` add `<PackageReference Include="Avalonia.Markup.Xaml.Loader" />` to the Avalonia package group.

Run: `dotnet restore YammiC/YammiC/YammiC/YammiC.csproj`
Expected: restores. If NuGet reports the package doesn't exist at 12.1.4, find the correct package/version (`dotnet package search Avalonia.Markup.Xaml.Loader`), and if Avalonia 12 moved `AvaloniaRuntimeXamlLoader` into another package, use that; record what was used at the top of `AvaloniaScreenRenderer.cs`.

- [ ] **Step 2: Interface and minimal page type**

`YccuSdk/Screens/ScreenPage.cs`:

```csharp
namespace CNCO.YccuSdk.Screens;

/// <summary>One page of a screen: an id, a display name and the XAML of its root control.</summary>
public sealed class ScreenPage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>The root control as XAML, with its xmlns declarations.</summary>
    public string Xaml { get; set; } = "";
}
```

`YccuSdk/Screens/IScreenRenderer.cs`:

```csharp
using SkiaSharp;

namespace CNCO.YccuSdk.Screens;

/// <summary>Draws a page for the bike. Implemented with Avalonia in the UI project; never throws (returns null and sets <see cref="LastError"/>).</summary>
public interface IScreenRenderer
{
    /// <summary>Why the last call returned null; null after a success.</summary>
    string? LastError { get; }

    /// <summary>Renders <paramref name="page"/> showing <paramref name="snapshot"/>, scaled to <paramref name="width"/> x <paramref name="height"/>. The caller owns the bitmap.</summary>
    Task<SKBitmap?> RenderAsync(ScreenTarget target, ScreenPage page, ScreenSnapshot snapshot, int width, int height);
}
```

- [ ] **Step 3: Widgets**

`YammiC/YammiC/YammiC/Screens/Widgets.cs` — the only control types pages may use besides Canvas/Grid/StackPanel/Border/TextBlock/Rectangle/Ellipse. Each switches a brush to its night variant when `ScreenState.Night`.

```csharp
using System;
using System.ComponentModel;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CNCO.YccuSdk.Screens;

namespace CNCO.YammiC.Screens;

/// <summary>Remembers a control's day brush and swaps in a night brush when the state says it's night.</summary>
internal sealed class NightPair
{
    private readonly AvaloniaObject _owner;
    private readonly StyledProperty<IBrush?> _property;
    private IBrush? _day;
    private bool _applying;

    public NightPair(AvaloniaObject owner, StyledProperty<IBrush?> property)
    {
        _owner = owner;
        _property = property;
    }

    public IBrush? Night { get; set; }

    /// <summary>Call when the brush property changes: anything we didn't set ourselves is the new day brush.</summary>
    public void Capture()
    {
        if (!_applying) _day = _owner.GetValue(_property);
    }

    public void Apply(bool night)
    {
        _applying = true;
        try
        {
            _owner.SetValue(_property, night && Night != null ? Night : _day);
        }
        finally
        {
            _applying = false;
        }
    }
}

/// <summary>Text with <c>{variables}</c> (see <see cref="ScreenVariables"/>): <c>&lt;y:VarText Template="{}{artist} - {song}"/&gt;</c>.</summary>
public class VarText : TextBlock
{
    public static readonly StyledProperty<string?> TemplateProperty = AvaloniaProperty.Register<VarText, string?>(nameof(Template));
    public static readonly StyledProperty<IBrush?> NightForegroundProperty = AvaloniaProperty.Register<VarText, IBrush?>(nameof(NightForeground));

    private readonly NightPair _foreground;
    private ScreenState? _state;

    static VarText()
    {
        TemplateProperty.Changed.AddClassHandler<VarText>((c, _) => c.Refresh());
        ForegroundProperty.Changed.AddClassHandler<VarText>((c, _) => c._foreground.Capture());
        NightForegroundProperty.Changed.AddClassHandler<VarText>((c, e) =>
        {
            c._foreground.Night = e.NewValue as IBrush;
            c.Refresh();
        });
    }

    public VarText()
    {
        _foreground = new NightPair(this, ForegroundProperty);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public string? Template
    {
        get => GetValue(TemplateProperty);
        set => SetValue(TemplateProperty, value);
    }

    public IBrush? NightForeground
    {
        get => GetValue(NightForegroundProperty);
        set => SetValue(NightForegroundProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_state != null) _state.PropertyChanged -= OnStateChanged;
        _state = DataContext as ScreenState;
        if (_state != null) _state.PropertyChanged += OnStateChanged;
        Refresh();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        Text = _state == null ? Template : _state.Resolve(Template);
        _foreground.Apply(_state?.Night == true);
    }
}

/// <summary>A filled rectangle (optionally with a night fill) that holds one child; used for every text/image element.</summary>
public class ScreenBox : Border
{
    public static readonly StyledProperty<IBrush?> NightBackgroundProperty = AvaloniaProperty.Register<ScreenBox, IBrush?>(nameof(NightBackground));

    private readonly NightPair _background;
    private ScreenState? _state;

    static ScreenBox()
    {
        BackgroundProperty.Changed.AddClassHandler<ScreenBox>((c, _) => c._background.Capture());
        NightBackgroundProperty.Changed.AddClassHandler<ScreenBox>((c, e) =>
        {
            c._background.Night = e.NewValue as IBrush;
            c.Apply();
        });
    }

    public ScreenBox()
    {
        _background = new NightPair(this, BackgroundProperty);
    }

    protected override Type StyleKeyOverride => typeof(Border);

    public IBrush? NightBackground
    {
        get => GetValue(NightBackgroundProperty);
        set => SetValue(NightBackgroundProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_state != null) _state.PropertyChanged -= OnStateChanged;
        _state = DataContext as ScreenState;
        if (_state != null) _state.PropertyChanged += OnStateChanged;
        Apply();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    private void Apply() => _background.Apply(_state?.Night == true);
}

/// <summary>The root of a page: a Canvas with a day and a night background.</summary>
public class ScreenCanvas : Canvas
{
    public static readonly StyledProperty<IBrush?> NightBackgroundProperty = AvaloniaProperty.Register<ScreenCanvas, IBrush?>(nameof(NightBackground));

    private readonly NightPair _background;
    private ScreenState? _state;

    static ScreenCanvas()
    {
        BackgroundProperty.Changed.AddClassHandler<ScreenCanvas>((c, _) => c._background.Capture());
        NightBackgroundProperty.Changed.AddClassHandler<ScreenCanvas>((c, e) =>
        {
            c._background.Night = e.NewValue as IBrush;
            c.Apply();
        });
    }

    public ScreenCanvas()
    {
        _background = new NightPair(this, BackgroundProperty);
    }

    protected override Type StyleKeyOverride => typeof(Canvas);

    public IBrush? NightBackground
    {
        get => GetValue(NightBackgroundProperty);
        set => SetValue(NightBackgroundProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_state != null) _state.PropertyChanged -= OnStateChanged;
        _state = DataContext as ScreenState;
        if (_state != null) _state.PropertyChanged += OnStateChanged;
        Apply();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    private void Apply() => _background.Apply(_state?.Night == true);
}

/// <summary>An image kept inside the page as base64 (so a screen is one self-contained file).</summary>
public class ScreenImage : Image
{
    public static readonly StyledProperty<string?> DataProperty = AvaloniaProperty.Register<ScreenImage, string?>(nameof(Data));

    static ScreenImage()
    {
        DataProperty.Changed.AddClassHandler<ScreenImage>((c, e) => c.Decode(e.NewValue as string));
    }

    protected override Type StyleKeyOverride => typeof(Image);

    public string? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    private void Decode(string? base64)
    {
        try
        {
            Source = string.IsNullOrWhiteSpace(base64) ? null : new Bitmap(new MemoryStream(Convert.FromBase64String(base64)));
        }
        catch (Exception)
        {
            Source = null; // a broken picture shows nothing rather than failing the page
        }
    }
}

/// <summary>A horizontal bar filled to <see cref="Value"/> (0..1); bind it to <c>{Binding Progress}</c>.</summary>
public class MediaProgress : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<MediaProgress, double>(nameof(Value));
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<MediaProgress, IBrush?>(nameof(Fill), Brushes.White);
    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<MediaProgress, IBrush?>(nameof(Track), Brushes.Gray);
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty = AvaloniaProperty.Register<MediaProgress, CornerRadius>(nameof(CornerRadius));

    static MediaProgress()
    {
        AffectsRender<MediaProgress>(ValueProperty, FillProperty, TrackProperty, CornerRadiusProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var all = new Rect(Bounds.Size);
        var radius = CornerRadius.TopLeft;
        context.DrawRectangle(Track, null, all, radius, radius);
        var fill = new Rect(0, 0, all.Width * Math.Clamp(Value, 0, 1), all.Height);
        if (fill.Width > 0) context.DrawRectangle(Fill, null, fill, radius, radius);
    }
}
```

- [ ] **Step 4: The renderer**

`YammiC/YammiC/YammiC/Screens/AvaloniaScreenRenderer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CNCO.YccuSdk.Screens;
using SkiaSharp;

namespace CNCO.YammiC.Screens;

/// <summary>
/// Draws pages with Avalonia: the page's XAML is loaded at run time, bound to a <see cref="ScreenState"/>, laid out at the
/// target's design size and rendered into a <see cref="RenderTargetBitmap"/>. All Avalonia work happens on the UI thread.
/// Use one instance per consumer (the bike service, the editor preview): each caches one control per target.
/// </summary>
public sealed class AvaloniaScreenRenderer : IScreenRenderer
{
    private readonly Dictionary<ScreenTarget, ScreenState> _states = new();
    private readonly Dictionary<ScreenTarget, (string Key, Control Root)> _roots = new();

    public string? LastError { get; private set; }

    /// <summary>False where code can't be generated at run time (iOS, browser): pages can't be loaded there.</summary>
    public static bool IsSupported => RuntimeFeature.IsDynamicCodeSupported;

    public async Task<SKBitmap?> RenderAsync(ScreenTarget target, ScreenPage page, ScreenSnapshot snapshot, int width, int height)
    {
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(() => RenderOnUi(target, page, snapshot, width, height));
        }
        catch (Exception t)
        {
            LastError = $"{t.GetType().Name}: {t.Message}";
            return null;
        }
    }

    private SKBitmap? RenderOnUi(ScreenTarget target, ScreenPage page, ScreenSnapshot snapshot, int width, int height)
    {
        if (!IsSupported)
        {
            LastError = "Custom screens aren't available on this platform.";
            return null;
        }

        var root = RootFor(target, page);
        if (root == null) return null;

        if (!_states.TryGetValue(target, out var state)) _states[target] = state = new ScreenState();
        state.Apply(snapshot);
        root.DataContext = state;

        var (designWidth, designHeight) = ScreenSizes.DesignSize(target);
        root.Width = designWidth;
        root.Height = designHeight;
        root.Measure(new Size(designWidth, designHeight));
        root.Arrange(new Rect(0, 0, designWidth, designHeight));

        using var target2 = new RenderTargetBitmap(new PixelSize(designWidth, designHeight), new Vector(96, 96));
        target2.Render(root);

        using var stream = new MemoryStream();
        target2.Save(stream); // PNG
        stream.Position = 0;
        var full = SKBitmap.Decode(stream);
        if (full == null)
        {
            LastError = "The rendered picture couldn't be read back.";
            return null;
        }

        LastError = null;
        if (full.Width == width && full.Height == height) return full;
        var scaled = full.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear));
        full.Dispose();
        return scaled;
    }

    /// <summary>The loaded control for <paramref name="page"/> (re-loaded only when the page or its XAML changed); null with <see cref="LastError"/> set if it won't load.</summary>
    private Control? RootFor(ScreenTarget target, ScreenPage page)
    {
        var key = page.Id + "|" + page.Xaml.GetHashCode();
        if (_roots.TryGetValue(target, out var cached) && cached.Key == key) return cached.Root;

        var issues = ScreenXamlValidator.Validate(page.Xaml);
        if (issues.Count > 0)
        {
            LastError = issues[0].ToString();
            return null;
        }

        try
        {
            var root = AvaloniaRuntimeXamlLoader.Parse<Control>(page.Xaml, typeof(AvaloniaScreenRenderer).Assembly);
            _roots[target] = (key, root);
            return root;
        }
        catch (Exception t)
        {
            LastError = $"{t.GetType().Name}: {t.Message}";
            return null;
        }
    }
}
```

NOTE: `ScreenXamlValidator` doesn't exist until Task 3. For this spike only, create the stub `YccuSdk/Screens/ScreenXamlValidator.cs` with `public static IReadOnlyList<ScreenIssue> Validate(string xaml) => Array.Empty<ScreenIssue>();` and `public sealed record ScreenIssue(int Line, string Message)`; Task 3 replaces it. If `AvaloniaRuntimeXamlLoader.Parse<T>` has a different shape in 12.1.4 (e.g. `Load`), adapt and note it in the file header.

- [ ] **Step 5: Spike harness**

`YammiC/YammiC/YammiC.Android/RenderSpike.cs`:

```csharp
using System;
using System.Threading.Tasks;
using CNCO.YammiC.Screens;
using CNCO.YccuSdk.Logging;
using CNCO.YccuSdk.Screens;

namespace YammiC.Android;

/// <summary>THROWAWAY (Task 2): renders a hard-coded page every 10 s and logs whether it worked. Removed in Task 7.</summary>
internal static class RenderSpike
{
    private const string Xaml = """
        <y:ScreenCanvas xmlns="https://github.com/avaloniaui"
                        xmlns:y="clr-namespace:CNCO.YammiC.Screens;assembly=YammiC"
                        Background="#112233">
          <y:ScreenBox Canvas.Left="20" Canvas.Top="20" Width="440" Height="80" Background="#FF8800">
            <y:VarText Template="{}{time} {song}" FontSize="40" Foreground="#FFFFFF" VerticalAlignment="Center" HorizontalAlignment="Center" />
          </y:ScreenBox>
          <y:MediaProgress Canvas.Left="20" Canvas.Top="120" Width="440" Height="12" Value="{Binding Progress}" />
        </y:ScreenCanvas>
        """;

    public static void Start(Logger logger)
    {
        var log = logger.Section("Spike");
        var renderer = new AvaloniaScreenRenderer();
        var page = new ScreenPage { Id = "spike", Name = "Spike", Xaml = Xaml };
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(10_000);
                var started = Environment.TickCount64;
                try
                {
                    var snapshot = ScreenSnapshot.Of(new ScreenContext(DateTime.Now, null, false));
                    using var bitmap = await renderer.RenderAsync(ScreenTarget.Navigation, page, snapshot, 480, 240);
                    if (bitmap == null)
                    {
                        log.Debug($"render FAILED: {renderer.LastError}");
                        continue;
                    }
                    // Background #112233 at (5,5); orange box #FF8800 at (30,30). Both must be right for the picture to be real.
                    var bg = bitmap.GetPixel(5, 5);
                    var box = bitmap.GetPixel(30, 30);
                    log.Debug($"render ok {bitmap.Width}x{bitmap.Height} in {Environment.TickCount64 - started} ms; bg=#{bg.Red:X2}{bg.Green:X2}{bg.Blue:X2} box=#{box.Red:X2}{box.Green:X2}{box.Blue:X2}");
                }
                catch (Exception t)
                {
                    log.Debug($"render threw: {t}");
                }
            }
        });
    }
}
```

In `Application.cs`, right after `service.Start();` add `RenderSpike.Start(logger);`.

- [ ] **Step 6: Trimmer roots (Release)**

In `YammiC.Android.csproj` add:

```xml
  <!-- Runtime-loaded screen pages use reflection over these; keep them whole (see docs/superpowers/specs/2026-10-19-avalonia-screen-pages-design.md). -->
  <ItemGroup>
    <TrimmerRootAssembly Include="YammiC" />
    <TrimmerRootAssembly Include="YccuSdk" />
    <TrimmerRootAssembly Include="Avalonia.Markup.Xaml.Loader" />
    <TrimmerRootAssembly Include="Avalonia.Markup.Xaml" />
    <TrimmerRootAssembly Include="Avalonia.Base" />
    <TrimmerRootAssembly Include="Avalonia.Controls" />
  </ItemGroup>
```

- [ ] **Step 7: Build, then run the go/no-go checks**

Run: `dotnet build YammiC/YammiC.Android/YammiC.Android.csproj -c Debug`
Expected: `0 Error(s)`.

Install and check (the user's phone; ask the user to plug it in if `adb devices` doesn't list `df85e93c`). Use `MSYS_NO_PATHCONV=1` for `/sdcard` paths:

1. Debug, app in foreground. After 30 s: `adb -s df85e93c shell cat /sdcard/Download/YammiC/<latest log>` (find it with `ls`) contains `render ok 480x240 ... bg=#112233 box=#FF8800`.
2. Same install, press Home, then swipe the app away from recents (service keeps the process). Reconnect the bike (or just wait: the foreground service keeps running only while connected — if there's no service, the process will die; in that case start the bike's Bluetooth connection so auto-connect starts the process with no Activity). Check the log again for new `render ok` lines **after** the swipe-away. This is the Activity-less check.
3. Release: `dotnet build YammiC/YammiC.Android/YammiC.Android.csproj -c Release -t:Install -p:AdbTarget="-s df85e93c"`. Launch, repeat check 1 and 2. If it crashes or logs `render FAILED`/`threw`, capture `adb -s df85e93c logcat -b crash -d` and the log.

Go criteria: checks 1 and 2 pass on Debug **and** Release. Record the render time (`in N ms`) in the report.
No-go: report exact error and stop; do not proceed to Task 3.

---

### Task 3: `ScreenDocument`, page analysis, XAML whitelist

**Files:**
- Modify: `YccuSdk/Screens/ScreenPage.cs`
- Create: `YccuSdk/Screens/ScreenDocument.cs`
- Replace: `YccuSdk/Screens/ScreenXamlValidator.cs` (the spike stub)
- Create: `YccuSdk.Tests/ScreenDocumentTests.cs`, `YccuSdk.Tests/ScreenXamlValidatorTests.cs`

**Interfaces:**
- Produces:
  - `enum ScreenEvent { ZoomIn, ZoomOut }`
  - `ScreenPage`: `Dictionary<ScreenEvent,string> Actions`, `bool UsesSeconds`, `bool UsesCan`, `ScreenPage Clone()`.
  - `ScreenDocument`: `ScreenTarget Target`, `List<ScreenPage> Pages`, `Dictionary<ScreenEvent,string> DefaultActions`, `static ScreenDocument Parse(string axaml)` (throws `ScreenDocumentException(int Line, string Message)`), `string ToAxaml()`, `ScreenPage? Find(string id)`, `ScreenDocument Clone()`, constants `AvaloniaNs`, `XamlNs`, `WidgetNs`.
  - `ScreenXamlValidator.Validate(string pageXaml) -> IReadOnlyList<ScreenIssue>`; `ScreenIssue(int Line, string Message)` with `ToString()` = `"line N: message"`.

Document format (the `.axaml` file):

```xml
<y:ScreenDocument xmlns="https://github.com/avaloniaui"
                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                  xmlns:y="clr-namespace:CNCO.YammiC.Screens;assembly=YammiC"
                  Target="Navigation" ZoomIn="NextPage" ZoomOut="PreviousPage">
  <y:Page Id="main" Name="Main" ZoomIn="GoTo:player">
    <y:ScreenCanvas Background="#000000"> ... </y:ScreenCanvas>
  </y:Page>
</y:ScreenDocument>
```

- [ ] **Step 1: Write the failing tests**

`YccuSdk.Tests/ScreenDocumentTests.cs`:

```csharp
using CNCO.YccuSdk.Screens;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class ScreenDocumentTests
{
    private const string Two = """
        <y:ScreenDocument xmlns="https://github.com/avaloniaui"
                          xmlns:y="clr-namespace:CNCO.YammiC.Screens;assembly=YammiC"
                          Target="VirtualPitBoard" ZoomIn="NextPage" ZoomOut="PreviousPage">
          <y:Page Id="main" Name="Main" ZoomIn="GoTo:second">
            <y:ScreenCanvas Background="#000000">
              <y:VarText Canvas.Left="0" Canvas.Top="163" Width="100" Height="20" Template="{}{time}" />
            </y:ScreenCanvas>
          </y:Page>
          <y:Page Id="second" Name="Second">
            <y:ScreenCanvas Background="#111111" />
          </y:Page>
        </y:ScreenDocument>
        """;

    [Fact]
    public void Parse_ReadsPagesTargetAndActions()
    {
        var doc = ScreenDocument.Parse(Two);
        Assert.Equal(ScreenTarget.VirtualPitBoard, doc.Target);
        Assert.Equal(new[] { "main", "second" }, doc.Pages.Select(p => p.Id));
        Assert.Equal("Main", doc.Pages[0].Name);
        Assert.Equal("GoTo:second", doc.Pages[0].Actions[ScreenEvent.ZoomIn]);
        Assert.Equal("NextPage", doc.DefaultActions[ScreenEvent.ZoomIn]);
        Assert.Equal("PreviousPage", doc.DefaultActions[ScreenEvent.ZoomOut]);
        Assert.False(doc.Pages[1].Actions.ContainsKey(ScreenEvent.ZoomIn));
    }

    [Fact]
    public void PageXaml_IsStandaloneAndKeepsNamespaces()
    {
        var page = ScreenDocument.Parse(Two).Pages[0];
        Assert.StartsWith("<ScreenCanvas", page.Xaml.TrimStart().Replace("y:", ""), StringComparison.Ordinal);
        Assert.Contains("clr-namespace:CNCO.YammiC.Screens;assembly=YammiC", page.Xaml);
        Assert.Contains("https://github.com/avaloniaui", page.Xaml);
        Assert.Empty(ScreenXamlValidator.Validate(page.Xaml));
    }

    [Fact]
    public void RoundTrip_PreservesPagesAndActions()
    {
        var first = ScreenDocument.Parse(Two);
        var again = ScreenDocument.Parse(first.ToAxaml());
        Assert.Equal(first.Target, again.Target);
        Assert.Equal(first.Pages.Select(p => p.Id), again.Pages.Select(p => p.Id));
        Assert.Equal(first.Pages[0].Actions[ScreenEvent.ZoomIn], again.Pages[0].Actions[ScreenEvent.ZoomIn]);
        Assert.Equal(first.DefaultActions[ScreenEvent.ZoomOut], again.DefaultActions[ScreenEvent.ZoomOut]);
        Assert.Contains("Template=\"{}{time}\"", again.Pages[0].Xaml);
    }

    [Fact]
    public void Parse_RejectsBadDocuments()
    {
        var e1 = Assert.Throws<ScreenDocumentException>(() => ScreenDocument.Parse("<y:ScreenDocument"));
        Assert.True(e1.Line >= 0);
        Assert.Throws<ScreenDocumentException>(() => ScreenDocument.Parse("<Canvas xmlns=\"https://github.com/avaloniaui\" />"));
        Assert.Throws<ScreenDocumentException>(() => ScreenDocument.Parse(Two.Replace("Id=\"second\"", "Id=\"main\"")));      // duplicate id
        Assert.Throws<ScreenDocumentException>(() => ScreenDocument.Parse(Two.Replace("Id=\"main\" ", "")));                  // missing id
        Assert.Throws<ScreenDocumentException>(() => ScreenDocument.Parse(
            "<y:ScreenDocument xmlns=\"https://github.com/avaloniaui\" xmlns:y=\"clr-namespace:CNCO.YammiC.Screens;assembly=YammiC\" />")); // no pages
        Assert.Throws<ScreenDocumentException>(() => ScreenDocument.Parse(Two.Replace("<y:ScreenCanvas Background=\"#111111\" />", "<y:ScreenCanvas /><y:ScreenCanvas />"))); // two roots
    }

    [Fact]
    public void UsesSeconds_OnlyWhenAFormatShowsThem()
    {
        Assert.True(new ScreenPage { Xaml = "<VarText Template=\"{}{time:HH:mm:ss}\" />" }.UsesSeconds);
        Assert.True(new ScreenPage { Xaml = "<TextBlock Text=\"{Binding Now, StringFormat={}{0:HH:mm:ss}}\" />" }.UsesSeconds);
        Assert.False(new ScreenPage { Xaml = "<MediaProgress Value=\"{Binding Progress}\" />" }.UsesSeconds); // "Progress" contains "ss"
        Assert.False(new ScreenPage { Xaml = "<VarText Template=\"{}{time}\" />" }.UsesSeconds);
    }

    [Fact]
    public void UsesCan_WhenABikeValueIsMentioned()
    {
        Assert.True(new ScreenPage { Xaml = "<TextBlock Text=\"{Binding Rpm}\" />" }.UsesCan);
        Assert.True(new ScreenPage { Xaml = "<VarText Template=\"{}{water:F}\" />" }.UsesCan);
        Assert.False(new ScreenPage { Xaml = "<VarText Template=\"{}{song}\" />" }.UsesCan);
    }
}
```

`YccuSdk.Tests/ScreenXamlValidatorTests.cs`:

```csharp
using CNCO.YccuSdk.Screens;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class ScreenXamlValidatorTests
{
    private const string Head = "<y:ScreenCanvas xmlns=\"https://github.com/avaloniaui\" xmlns:y=\"clr-namespace:CNCO.YammiC.Screens;assembly=YammiC\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" ";

    private static IReadOnlyList<ScreenIssue> Check(string inner) => ScreenXamlValidator.Validate(Head + ">" + inner + "</y:ScreenCanvas>");

    [Fact]
    public void AcceptsTheWidgetSet()
    {
        Assert.Empty(Check("""
            <y:ScreenBox Canvas.Left="1" Canvas.Top="2" Width="30" Height="20" Background="#FF0000" NightBackground="#00FF00" IsVisible="{Binding HasMusic}" Tag="Clock">
              <y:VarText Template="{}{time:HH:mm}" FontSize="20" FontWeight="Bold" TextAlignment="Center" Foreground="#FFFFFF" />
            </y:ScreenBox>
            <TextBlock Text="{Binding Now, StringFormat={}{0:HH:mm}}" />
            <y:MediaProgress Value="{Binding Progress}" Fill="#FFF" Track="#333" />
            <Rectangle Fill="#123456" Width="5" Height="5" />
            <Grid RowDefinitions="*,*"><Border /></Grid>
            """));
    }

    [Theory]
    [InlineData("<Button />")]
    [InlineData("<Image Source=\"file:///x.png\" />")]
    [InlineData("<Border><Border.Background><SolidColorBrush /></Border.Background></Border>")]
    [InlineData("<TextBlock Text=\"{StaticResource Foo}\" />")]
    [InlineData("<TextBlock Text=\"{x:Static sys:Environment.MachineName}\" />")]
    [InlineData("<TextBlock Text=\"{Binding Song, Converter={StaticResource C}}\" />")]
    [InlineData("<TextBlock Text=\"{Binding Source={x:Static Foo.Bar}}\" />")]
    [InlineData("<TextBlock Text=\"{Binding Song}{Binding Artist}\" />")]
    [InlineData("<TextBlock Tooltip.Tip=\"x\" />")]
    [InlineData("<TextBlock Foo=\"1\" />")]
    public void RejectsEverythingElse(string inner)
    {
        Assert.NotEmpty(Check(inner));
    }

    [Fact]
    public void MalformedXml_ReportsALine()
    {
        var issues = ScreenXamlValidator.Validate("<y:ScreenCanvas\n  xmlns:y=\"a\">\n<oops>\n</y:ScreenCanvas>");
        Assert.Single(issues);
        Assert.True(issues[0].Line > 0);
    }

    [Fact]
    public void IssueText_NamesTheLine()
    {
        var issues = Check("\n<Button />");
        Assert.StartsWith("line ", issues[0].ToString());
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test YccuSdk.Tests/YccuSdk.Tests.csproj`
Expected: FAIL (types missing / stub validator accepts everything).

- [ ] **Step 3: Implement**

Replace `YccuSdk/Screens/ScreenPage.cs`:

```csharp
using System.Text.RegularExpressions;

namespace CNCO.YccuSdk.Screens;

/// <summary>What a button press on the bike can raise on a page.</summary>
public enum ScreenEvent
{
    ZoomIn,
    ZoomOut,
}

/// <summary>One page of a screen: an id, a display name, the XAML of its root control and what its events do.</summary>
public sealed partial class ScreenPage
{
    private static readonly Regex SecondsPattern = SecondsRegex();
    private static readonly Regex CanPattern = new(
        @"\b(" + string.Join('|', ScreenVariables.CanVariableNames) + @")\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>The root control as XAML, with its xmlns declarations.</summary>
    public string Xaml { get; set; } = "";

    /// <summary>Event name → action text (see <see cref="ScreenAction"/>). Missing = use the document's default.</summary>
    public Dictionary<ScreenEvent, string> Actions { get; } = new();

    /// <summary>True when the page shows seconds (so it must redraw every second, not every minute).</summary>
    public bool UsesSeconds => SecondsPattern.IsMatch(Xaml);

    /// <summary>True when the page mentions bike data (it then redraws faster). May be true for a page that merely contains the word.</summary>
    public bool UsesCan => CanPattern.IsMatch(Xaml);

    public ScreenPage Clone()
    {
        var copy = new ScreenPage { Id = Id, Name = Name, Xaml = Xaml };
        foreach (var action in Actions) copy.Actions[action.Key] = action.Value;
        return copy;
    }

    // "ss" as a whole word (a format such as HH:mm:ss) or :s, or Now.Second; not the "ss" inside "Progress".
    [GeneratedRegex(@"(?<![A-Za-z])ss(?![A-Za-z])|:s(?![A-Za-z])|\.Second", RegexOptions.None)]
    private static partial Regex SecondsRegex();
}
```

`YccuSdk/Screens/ScreenDocument.cs`:

```csharp
using System.Xml;
using System.Xml.Linq;

namespace CNCO.YccuSdk.Screens;

public sealed class ScreenDocumentException : Exception
{
    public ScreenDocumentException(int line, string message) : base(line > 0 ? $"line {line}: {message}" : message)
    {
        Line = line;
    }

    public int Line { get; }
}

/// <summary>
/// One screen (navigation or pit board): a list of pages, each the XAML of a root control, plus what the bike's buttons do.
/// Stored and exchanged as one <c>.axaml</c> text (see <see cref="Parse"/>/<see cref="ToAxaml"/>).
/// </summary>
public sealed class ScreenDocument
{
    public const string AvaloniaNs = "https://github.com/avaloniaui";
    public const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
    public const string WidgetNs = "clr-namespace:CNCO.YammiC.Screens;assembly=YammiC";

    public ScreenTarget Target { get; set; }
    public List<ScreenPage> Pages { get; } = new();

    /// <summary>Used for an event the current page has no action for.</summary>
    public Dictionary<ScreenEvent, string> DefaultActions { get; } = new();

    public ScreenPage? Find(string id) => Pages.FirstOrDefault(p => p.Id == id);

    public ScreenDocument Clone()
    {
        var copy = new ScreenDocument { Target = Target };
        foreach (var page in Pages) copy.Pages.Add(page.Clone());
        foreach (var action in DefaultActions) copy.DefaultActions[action.Key] = action.Value;
        return copy;
    }

    public static ScreenDocument Parse(string axaml)
    {
        XDocument xml;
        try
        {
            xml = XDocument.Parse(axaml, LoadOptions.SetLineInfo);
        }
        catch (XmlException e)
        {
            throw new ScreenDocumentException(e.LineNumber, e.Message);
        }

        var root = xml.Root!;
        if (root.Name.LocalName != "ScreenDocument") throw new ScreenDocumentException(Line(root), "The root element must be <y:ScreenDocument>.");

        var declarations = root.Attributes().Where(a => a.IsNamespaceDeclaration).ToList();
        var document = new ScreenDocument
        {
            Target = Enum.TryParse<ScreenTarget>(root.Attribute("Target")?.Value, out var target) ? target : ScreenTarget.Navigation,
        };
        ReadActions(root, document.DefaultActions);

        foreach (var page in root.Elements())
        {
            if (page.Name.LocalName != "Page") throw new ScreenDocumentException(Line(page), $"Unexpected <{page.Name.LocalName}>; a screen holds only <y:Page> elements.");

            var id = page.Attribute("Id")?.Value;
            if (string.IsNullOrWhiteSpace(id)) throw new ScreenDocumentException(Line(page), "A page needs an Id.");
            if (document.Find(id) != null) throw new ScreenDocumentException(Line(page), $"There are two pages with the Id \"{id}\".");

            var content = page.Elements().ToList();
            if (content.Count != 1) throw new ScreenDocumentException(Line(page), $"Page \"{id}\" must contain exactly one root element.");

            // Make the page stand alone: it carries the document's xmlns declarations.
            var copy = new XElement(content[0]);
            foreach (var declaration in declarations)
                if (copy.Attribute(declaration.Name) == null) copy.Add(new XAttribute(declaration.Name, declaration.Value));

            var result = new ScreenPage { Id = id, Name = page.Attribute("Name")?.Value ?? id, Xaml = copy.ToString() };
            ReadActions(page, result.Actions);
            document.Pages.Add(result);
        }

        if (document.Pages.Count == 0) throw new ScreenDocumentException(Line(root), "A screen needs at least one page.");
        return document;
    }

    public string ToAxaml()
    {
        XNamespace widgets = WidgetNs;
        var root = new XElement(widgets + "ScreenDocument",
            new XAttribute("xmlns", AvaloniaNs),
            new XAttribute(XNamespace.Xmlns + "x", XamlNs),
            new XAttribute(XNamespace.Xmlns + "y", WidgetNs),
            new XAttribute("Target", Target.ToString()));
        WriteActions(root, DefaultActions);

        var shared = new Dictionary<XName, string>
        {
            [XNamespace.None + "xmlns"] = AvaloniaNs,
            [XNamespace.Xmlns + "x"] = XamlNs,
            [XNamespace.Xmlns + "y"] = WidgetNs,
        };

        foreach (var page in Pages)
        {
            var element = new XElement(widgets + "Page", new XAttribute("Id", page.Id), new XAttribute("Name", page.Name));
            WriteActions(element, page.Actions);

            var content = XElement.Parse(page.Xaml);
            content.Attributes().Where(a => a.IsNamespaceDeclaration && shared.TryGetValue(a.Name, out var value) && value == a.Value).Remove();
            element.Add(content);
            root.Add(element);
        }

        return root.ToString();
    }

    private static void ReadActions(XElement element, Dictionary<ScreenEvent, string> into)
    {
        foreach (var screenEvent in Enum.GetValues<ScreenEvent>())
        {
            var value = element.Attribute(screenEvent.ToString())?.Value;
            if (!string.IsNullOrWhiteSpace(value)) into[screenEvent] = value.Trim();
        }
    }

    private static void WriteActions(XElement element, Dictionary<ScreenEvent, string> actions)
    {
        foreach (var action in actions.OrderBy(a => a.Key)) element.SetAttributeValue(action.Key.ToString(), action.Value);
    }

    private static int Line(XObject node) => (node as IXmlLineInfo)?.LineNumber ?? 0;
}
```

`YccuSdk/Screens/ScreenXamlValidator.cs` (replaces the stub):

```csharp
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CNCO.YccuSdk.Screens;

public sealed record ScreenIssue(int Line, string Message)
{
    public override string ToString() => Line > 0 ? $"line {Line}: {Message}" : Message;
}

/// <summary>
/// Checks a page's XAML against the allowed controls, properties and bindings <i>before</i> it reaches the XAML loader (which
/// would happily instantiate anything). Only attributes are allowed (no property elements), and markup extensions are limited
/// to a plain <c>{Binding ...}</c> without converters or other sources.
/// </summary>
public static partial class ScreenXamlValidator
{
    private static readonly HashSet<string> Elements = new(StringComparer.Ordinal)
    {
        "Canvas", "Grid", "StackPanel", "Border", "TextBlock", "Rectangle", "Ellipse",
        "ScreenCanvas", "ScreenBox", "VarText", "ScreenImage", "MediaProgress",
    };

    private static readonly HashSet<string> Attributes = new(StringComparer.Ordinal)
    {
        "Canvas.Left", "Canvas.Top", "Canvas.ZIndex", "Grid.Row", "Grid.Column", "Grid.RowSpan", "Grid.ColumnSpan",
        "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Margin", "Padding", "Opacity", "IsVisible", "ClipToBounds", "Tag",
        "HorizontalAlignment", "VerticalAlignment", "Background", "NightBackground", "Foreground", "NightForeground",
        "Fill", "Track", "Stroke", "StrokeThickness", "CornerRadius", "BorderBrush", "BorderThickness",
        "FontSize", "FontWeight", "FontStyle", "FontFamily", "TextAlignment", "TextWrapping", "TextTrimming", "MaxLines", "LineHeight",
        "Text", "Template", "Data", "Stretch", "Value", "Orientation", "Spacing", "RowDefinitions", "ColumnDefinitions",
    };

    private static readonly string[] BannedInBinding = { "Converter", "Source", "ElementName", "RelativeSource", "x:", "Static", "Resource" };

    public static IReadOnlyList<ScreenIssue> Validate(string xaml)
    {
        XElement root;
        try
        {
            root = XElement.Parse(xaml, LoadOptions.SetLineInfo);
        }
        catch (XmlException e)
        {
            return new[] { new ScreenIssue(e.LineNumber, e.Message) };
        }

        var issues = new List<ScreenIssue>();
        Visit(root, issues);
        return issues;
    }

    private static void Visit(XElement element, List<ScreenIssue> issues)
    {
        var line = (element as IXmlLineInfo)?.LineNumber ?? 0;
        var name = element.Name.LocalName;

        var knownNamespace = element.Name.NamespaceName is ScreenDocument.AvaloniaNs or ScreenDocument.WidgetNs;
        if (name.Contains('.')) issues.Add(new ScreenIssue(line, $"<{name}> isn't supported; set the property as an attribute instead."));
        else if (!knownNamespace || !Elements.Contains(name)) issues.Add(new ScreenIssue(line, $"<{name}> isn't an allowed element."));

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration) continue;

            var attributeName = attribute.Name.LocalName;
            var allowed = Attributes.Contains(attributeName) && attribute.Name.NamespaceName.Length == 0
                          || attribute.Name.NamespaceName == ScreenDocument.XamlNs && attributeName == "Name";
            if (!allowed)
            {
                issues.Add(new ScreenIssue(line, $"\"{attributeName}\" isn't an allowed property on <{name}>."));
                continue;
            }

            var problem = CheckValue(attribute.Value);
            if (problem != null) issues.Add(new ScreenIssue(line, $"{attributeName}: {problem}"));
        }

        foreach (var child in element.Elements()) Visit(child, issues);
    }

    private static string? CheckValue(string value)
    {
        var text = value.Trim();
        if (!text.StartsWith('{') || text.StartsWith("{}", StringComparison.Ordinal)) return null; // plain text (or an escaped literal)

        if (!text.StartsWith("{Binding", StringComparison.Ordinal) || (text.Length > 8 && !char.IsWhiteSpace(text[8]) && text[8] != '}' && text[8] != ','))
            return "only {Binding ...} is allowed as a markup extension.";
        foreach (var banned in BannedInBinding)
            if (text.Contains(banned, StringComparison.Ordinal)) return $"a binding can't use \"{banned}\".";

        // What is left after removing the escaped StringFormat pieces ({}{0:...}) must be exactly one { ... }.
        var stripped = EscapedFormat().Replace(text, "");
        if (stripped.Count(c => c == '{') != 1 || stripped.Count(c => c == '}') != 1 || !stripped.EndsWith('}'))
            return "the binding is malformed.";
        return null;
    }

    [GeneratedRegex(@"\{\}\{[^{}]*\}")]
    private static partial Regex EscapedFormat();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test YccuSdk.Tests/YccuSdk.Tests.csproj`
Expected: all pass. Fix the implementation (not the tests) if a case fails; if a test case itself is wrong for a real reason, say why.

---

### Task 4: Actions and `ScreenController`

**Files:**
- Create: `YccuSdk/Screens/ScreenAction.cs`, `YccuSdk/Screens/ScreenController.cs`
- Create: `YccuSdk.Tests/ScreenControllerTests.cs`

**Interfaces:**
- Consumes: `ScreenDocument`, `ScreenPage`, `ScreenEvent`, `ScreenSnapshot` (Tasks 1, 3).
- Produces:
  - `enum ScreenActionKind { None, NextPage, PreviousPage, GoTo, MediaPlayPause, MediaNext, MediaPrevious, Toggle, Set }`
  - `readonly record struct ScreenAction(ScreenActionKind Kind, string Arg = "", string Value = "")` with `static bool TryParse(string? text, out ScreenAction action)`.
  - `interface IScreenMediaActions { void PlayPause(); void Next(); void Previous(); }`
  - `sealed class ScreenController(ScreenDocument document, IScreenMediaActions? media = null)` with `ScreenDocument Document`, `void SetDocument(ScreenDocument)`, `ScreenPage CurrentPage`, `int PageIndex`, `int PageCount`, `bool Handle(ScreenEvent)`, `ScreenSnapshot Snapshot(ScreenContext)`, `event Action? Changed`, `void GoTo(string pageId)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using CNCO.YccuSdk.Screens;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class ScreenControllerTests
{
    private sealed class FakeMedia : IScreenMediaActions
    {
        public List<string> Calls { get; } = new();
        public void PlayPause() => Calls.Add("playpause");
        public void Next() => Calls.Add("next");
        public void Previous() => Calls.Add("previous");
    }

    private static ScreenDocument Doc(params string[] ids)
    {
        var doc = new ScreenDocument { Target = ScreenTarget.Navigation };
        foreach (var id in ids) doc.Pages.Add(new ScreenPage { Id = id, Name = id, Xaml = "<ScreenCanvas />" });
        return doc;
    }

    private static ScreenContext Ctx() => new(DateTime.Now, null, false);

    [Theory]
    [InlineData("NextPage", ScreenActionKind.NextPage, "", "")]
    [InlineData("previouspage", ScreenActionKind.PreviousPage, "", "")]
    [InlineData("GoTo:player", ScreenActionKind.GoTo, "player", "")]
    [InlineData("Media.PlayPause", ScreenActionKind.MediaPlayPause, "", "")]
    [InlineData("Media.Next", ScreenActionKind.MediaNext, "", "")]
    [InlineData("Media.Previous", ScreenActionKind.MediaPrevious, "", "")]
    [InlineData("Toggle:menu", ScreenActionKind.Toggle, "menu", "")]
    [InlineData("Set:speed=fast", ScreenActionKind.Set, "speed", "fast")]
    [InlineData("None", ScreenActionKind.None, "", "")]
    public void Parse_KnownActions(string text, ScreenActionKind kind, string arg, string value)
    {
        Assert.True(ScreenAction.TryParse(text, out var action));
        Assert.Equal(new ScreenAction(kind, arg, value), action);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Explode")]
    [InlineData("GoTo:")]
    [InlineData("Set:novalue")]
    public void Parse_RejectsJunk(string? text) => Assert.False(ScreenAction.TryParse(text, out _));

    [Fact]
    public void Pages_NextAndPreviousWrap()
    {
        var doc = Doc("a", "b", "c");
        doc.DefaultActions[ScreenEvent.ZoomIn] = "NextPage";
        doc.DefaultActions[ScreenEvent.ZoomOut] = "PreviousPage";
        var c = new ScreenController(doc);
        Assert.Equal("a", c.CurrentPage.Id);
        Assert.True(c.Handle(ScreenEvent.ZoomIn));
        Assert.Equal("b", c.CurrentPage.Id);
        c.Handle(ScreenEvent.ZoomIn); c.Handle(ScreenEvent.ZoomIn);
        Assert.Equal("a", c.CurrentPage.Id);
        c.Handle(ScreenEvent.ZoomOut);
        Assert.Equal("c", c.CurrentPage.Id);
    }

    [Fact]
    public void PageAction_BeatsDefault_AndGoToWorks()
    {
        var doc = Doc("a", "b");
        doc.DefaultActions[ScreenEvent.ZoomIn] = "NextPage";
        doc.Pages[0].Actions[ScreenEvent.ZoomIn] = "GoTo:b";
        var c = new ScreenController(doc);
        c.Handle(ScreenEvent.ZoomIn);
        Assert.Equal("b", c.CurrentPage.Id);
    }

    [Fact]
    public void GoToUnknownPage_IsIgnored_AndNoActionMeansUnhandled()
    {
        var doc = Doc("a");
        doc.Pages[0].Actions[ScreenEvent.ZoomIn] = "GoTo:nope";
        var c = new ScreenController(doc);
        Assert.True(c.Handle(ScreenEvent.ZoomIn));     // there was an action, it just had nowhere to go
        Assert.Equal("a", c.CurrentPage.Id);
        Assert.False(c.Handle(ScreenEvent.ZoomOut));   // no action at all
    }

    [Fact]
    public void SinglePage_NextPageStaysPut()
    {
        var doc = Doc("only");
        doc.DefaultActions[ScreenEvent.ZoomIn] = "NextPage";
        var c = new ScreenController(doc);
        c.Handle(ScreenEvent.ZoomIn);
        Assert.Equal("only", c.CurrentPage.Id);
    }

    [Fact]
    public void NoneAction_ConsumesTheEventAndDoesNothing()
    {
        var doc = Doc("a", "b");
        doc.DefaultActions[ScreenEvent.ZoomIn] = "NextPage";
        doc.Pages[0].Actions[ScreenEvent.ZoomIn] = "None";
        var c = new ScreenController(doc);
        Assert.True(c.Handle(ScreenEvent.ZoomIn));
        Assert.Equal("a", c.CurrentPage.Id);
    }

    [Fact]
    public void MediaActions_CallThrough()
    {
        var doc = Doc("a");
        doc.DefaultActions[ScreenEvent.ZoomIn] = "Media.Next";
        doc.DefaultActions[ScreenEvent.ZoomOut] = "Media.PlayPause";
        var media = new FakeMedia();
        var c = new ScreenController(doc, media);
        c.Handle(ScreenEvent.ZoomIn);
        c.Handle(ScreenEvent.ZoomOut);
        Assert.Equal(new[] { "next", "playpause" }, media.Calls);
    }

    [Fact]
    public void ToggleAndSet_AppearInTheSnapshot()
    {
        var doc = Doc("a");
        doc.DefaultActions[ScreenEvent.ZoomIn] = "Toggle:menu";
        doc.DefaultActions[ScreenEvent.ZoomOut] = "Set:mode=fast";
        var c = new ScreenController(doc);
        c.Handle(ScreenEvent.ZoomIn);
        c.Handle(ScreenEvent.ZoomOut);
        var snap = c.Snapshot(Ctx());
        Assert.True(snap.Flags["menu"]);
        Assert.Equal("fast", snap.Vars["mode"]);
        c.Handle(ScreenEvent.ZoomIn);
        Assert.False(c.Snapshot(Ctx()).Flags["menu"]);
    }

    [Fact]
    public void Changed_FiresOnPageChange_AndSetDocumentKeepsPageById()
    {
        var c = new ScreenController(Doc("a", "b"));
        var changes = 0;
        c.Changed += () => changes++;
        c.GoTo("b");
        Assert.Equal(1, changes);
        c.GoTo("b");
        Assert.Equal(1, changes);                     // not a change

        c.SetDocument(Doc("x", "b", "c"));
        Assert.Equal("b", c.CurrentPage.Id);          // same id still exists
        c.SetDocument(Doc("x", "y"));
        Assert.Equal("x", c.CurrentPage.Id);          // gone: back to the first
    }

    [Fact]
    public void Snapshot_CarriesPagePosition()
    {
        var c = new ScreenController(Doc("a", "b", "c"));
        c.GoTo("c");
        var snap = c.Snapshot(Ctx());
        Assert.Equal(("c", 2, 3), (snap.PageId, snap.PageIndex, snap.PageCount));
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test YccuSdk.Tests/YccuSdk.Tests.csproj`; expected FAIL (types missing).

- [ ] **Step 3: Implement**

`YccuSdk/Screens/ScreenAction.cs`:

```csharp
namespace CNCO.YccuSdk.Screens;

public enum ScreenActionKind
{
    None,
    NextPage,
    PreviousPage,
    GoTo,
    MediaPlayPause,
    MediaNext,
    MediaPrevious,
    Toggle,
    Set,
}

/// <summary>What a button press does. Text forms: <c>NextPage</c>, <c>PreviousPage</c>, <c>GoTo:id</c>, <c>Media.PlayPause|Next|Previous</c>, <c>Toggle:flag</c>, <c>Set:name=value</c>, <c>None</c>.</summary>
public readonly record struct ScreenAction(ScreenActionKind Kind, string Arg = "", string Value = "")
{
    public static bool TryParse(string? text, out ScreenAction action)
    {
        action = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        var colon = trimmed.IndexOf(':');
        var head = (colon < 0 ? trimmed : trimmed[..colon]).ToLowerInvariant();
        var tail = colon < 0 ? "" : trimmed[(colon + 1)..].Trim();

        switch (head)
        {
            case "none" when colon < 0: action = new(ScreenActionKind.None); return true;
            case "nextpage" when colon < 0: action = new(ScreenActionKind.NextPage); return true;
            case "previouspage" when colon < 0: action = new(ScreenActionKind.PreviousPage); return true;
            case "media.playpause" when colon < 0: action = new(ScreenActionKind.MediaPlayPause); return true;
            case "media.next" when colon < 0: action = new(ScreenActionKind.MediaNext); return true;
            case "media.previous" when colon < 0: action = new(ScreenActionKind.MediaPrevious); return true;
            case "goto" when tail.Length > 0: action = new(ScreenActionKind.GoTo, tail); return true;
            case "toggle" when tail.Length > 0: action = new(ScreenActionKind.Toggle, tail); return true;
            case "set":
                var equals = tail.IndexOf('=');
                if (equals <= 0) return false;
                action = new(ScreenActionKind.Set, tail[..equals].Trim(), tail[(equals + 1)..].Trim());
                return true;
            default:
                return false;
        }
    }
}
```

`YccuSdk/Screens/ScreenController.cs`:

```csharp
namespace CNCO.YccuSdk.Screens;

/// <summary>The phone's music player, as far as screen actions need it.</summary>
public interface IScreenMediaActions
{
    void PlayPause();
    void Next();
    void Previous();
}

/// <summary>
/// The live state of one screen: which page is showing, the flags and variables actions have set, and what the bike's
/// buttons do (the page's action for an event, else the document's default). Thread-safe.
/// </summary>
public sealed class ScreenController
{
    private readonly object _gate = new();
    private readonly IScreenMediaActions? _media;
    private readonly Dictionary<string, bool> _flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _vars = new(StringComparer.OrdinalIgnoreCase);
    private ScreenDocument _document;
    private int _index;

    public ScreenController(ScreenDocument document, IScreenMediaActions? media = null)
    {
        _document = document;
        _media = media;
    }

    /// <summary>Raised after a page, flag or variable changed (the screen needs redrawing).</summary>
    public event Action? Changed;

    public ScreenDocument Document
    {
        get
        {
            lock (_gate) return _document;
        }
    }

    public ScreenPage CurrentPage
    {
        get
        {
            lock (_gate) return _document.Pages[_index];
        }
    }

    public int PageIndex
    {
        get
        {
            lock (_gate) return _index;
        }
    }

    public int PageCount
    {
        get
        {
            lock (_gate) return _document.Pages.Count;
        }
    }

    /// <summary>Swaps in an edited document. Stays on the same page if it still exists, else the first.</summary>
    public void SetDocument(ScreenDocument document)
    {
        lock (_gate)
        {
            var currentId = _document.Pages[_index].Id;
            _document = document;
            var found = document.Pages.FindIndex(p => p.Id == currentId);
            _index = found < 0 ? 0 : found;
        }
        Changed?.Invoke();
    }

    public void GoTo(string pageId)
    {
        lock (_gate)
        {
            var found = _document.Pages.FindIndex(p => p.Id == pageId);
            if (found < 0 || found == _index) return;
            _index = found;
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Runs the action for <paramref name="screenEvent"/>. False when there is no action at all (neither on the page nor a
    /// document default), so the caller can fall back to something else; true otherwise, even for "None" or an action that
    /// had nothing to do.
    /// </summary>
    public bool Handle(ScreenEvent screenEvent)
    {
        ScreenAction action;
        lock (_gate)
        {
            var page = _document.Pages[_index];
            var text = page.Actions.TryGetValue(screenEvent, out var own) ? own
                : _document.DefaultActions.TryGetValue(screenEvent, out var fallback) ? fallback
                : null;
            if (!ScreenAction.TryParse(text, out action)) return false;
        }

        Run(action);
        return true;
    }

    private void Run(ScreenAction action)
    {
        var changed = false;
        lock (_gate)
        {
            var count = _document.Pages.Count;
            switch (action.Kind)
            {
                case ScreenActionKind.NextPage when count > 1:
                    _index = (_index + 1) % count;
                    changed = true;
                    break;
                case ScreenActionKind.PreviousPage when count > 1:
                    _index = (_index - 1 + count) % count;
                    changed = true;
                    break;
                case ScreenActionKind.GoTo:
                    var found = _document.Pages.FindIndex(p => p.Id == action.Arg);
                    if (found >= 0 && found != _index)
                    {
                        _index = found;
                        changed = true;
                    }
                    break;
                case ScreenActionKind.Toggle:
                    _flags[action.Arg] = !(_flags.TryGetValue(action.Arg, out var on) && on);
                    changed = true;
                    break;
                case ScreenActionKind.Set:
                    _vars[action.Arg] = action.Value;
                    changed = true;
                    break;
            }
        }

        // Media goes out of the lock: it talks to the phone.
        switch (action.Kind)
        {
            case ScreenActionKind.MediaPlayPause: _media?.PlayPause(); break;
            case ScreenActionKind.MediaNext: _media?.Next(); break;
            case ScreenActionKind.MediaPrevious: _media?.Previous(); break;
        }

        if (changed) Changed?.Invoke();
    }

    public ScreenSnapshot Snapshot(ScreenContext context)
    {
        lock (_gate)
        {
            return new ScreenSnapshot(
                context,
                _document.Pages[_index].Id,
                _index,
                _document.Pages.Count,
                new Dictionary<string, bool>(_flags, StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(_vars, StringComparer.OrdinalIgnoreCase));
        }
    }
}
```

- [ ] **Step 4: Run tests** — `dotnet test YccuSdk.Tests/YccuSdk.Tests.csproj`; expected all pass.

---

### Task 5: Migration, defaults and `ScreenDocumentStore`

**Files:**
- Create: `YccuSdk/Screens/ScreenLayoutMigration.cs`, `YccuSdk/Screens/ScreenDocumentStore.cs`
- Modify: `YccuSdk/Screens/ScreenDefaults.cs` (add `Document(target)`)
- Create: `YccuSdk.Tests/ScreenMigrationTests.cs`

**Interfaces:**
- Consumes: `ScreenLayout`/`ScreenElement` (existing), `ScreenDocument`, `ScreenXamlValidator`.
- Produces: `ScreenLayoutMigration.ToDocument(ScreenLayout) -> ScreenDocument` (one page, id `main`); `ScreenDefaults.Document(ScreenTarget)`; `ScreenDocumentStore(IYmcKeyValueStore)` with `ScreenDocument Load(ScreenTarget)`, `void Save(ScreenTarget, ScreenDocument)`, `void Reset(ScreenTarget)`.

Migration mapping (every element becomes a direct child of the root `y:ScreenCanvas`, so the editor can list them):
- Layout → `y:ScreenCanvas` with `Width/Height` (design size), `Background`, `NightBackground`.
- Text/box element → `y:ScreenBox` (`Canvas.Left/Top`, `Width`, `Height`, `Background`, `NightBackground`, `Tag`=name, `IsVisible` binding from show-when) containing, if `Text` is non-empty, `y:VarText` (`Template`, `FontSize`, `FontWeight`, `Foreground`, `NightForeground`, `TextAlignment`, `VerticalAlignment="Center"`, `TextTrimming="CharacterEllipsis"`).
- Image element → `y:ScreenBox` containing `y:ScreenImage` (`Data`, `Stretch`: Contain→Uniform, Cover→UniformToFill, Stretch→Fill).
- Show-when → `IsVisible="{Binding X}"`: MusicPresent→HasMusic, MusicPlaying→IsPlaying, MusicPaused→IsPaused, NoMusic→NoMusic, Day→Day, Night→Night; Always → no attribute.
- A template starting with `{` gets the `{}` escape prefix.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Xml.Linq;
using CNCO.YccuSdk.Platform;
using CNCO.YccuSdk.Screens;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class ScreenMigrationTests
{
    private sealed class MemoryStore : IYmcKeyValueStore
    {
        public Dictionary<string, string> Values { get; } = new();
        public string? GetString(string key) => Values.TryGetValue(key, out var v) ? v : null;
        public void SetString(string key, string value) => Values[key] = value;
    }

    private static XElement Root(ScreenDocument doc) => XElement.Parse(doc.Pages[0].Xaml);

    [Fact]
    public void DefaultNavigation_MigratesToAValidDocument()
    {
        var doc = ScreenDefaults.Document(ScreenTarget.Navigation);
        Assert.Equal(ScreenTarget.Navigation, doc.Target);
        Assert.Single(doc.Pages);
        Assert.Equal("main", doc.Pages[0].Id);
        Assert.Empty(ScreenXamlValidator.Validate(doc.Pages[0].Xaml));
        Assert.Equal(6, Root(doc).Elements().Count());
        var reparsed = ScreenDocument.Parse(doc.ToAxaml());
        Assert.Empty(ScreenXamlValidator.Validate(reparsed.Pages[0].Xaml));
        Assert.Equal(6, XElement.Parse(reparsed.Pages[0].Xaml).Elements().Count());
    }

    [Fact]
    public void Clock_KeepsTemplateColoursAndNightVariants()
    {
        var root = Root(ScreenDefaults.Document(ScreenTarget.Navigation));
        Assert.Equal("#F4F4F2", (string?)root.Attribute("Background"));
        Assert.Equal("#000000", (string?)root.Attribute("NightBackground"));

        var clock = root.Elements().First();
        Assert.Equal("Clock", (string?)clock.Attribute("Tag"));
        var text = clock.Elements().Single();
        Assert.Equal("{}{time}", (string?)text.Attribute("Template"));
        Assert.Equal("#111111", (string?)text.Attribute("Foreground"));
        Assert.Equal("#FFFFFF", (string?)text.Attribute("NightForeground"));
        Assert.Equal("Bold", (string?)text.Attribute("FontWeight"));
        Assert.Equal("Center", (string?)text.Attribute("TextAlignment"));
    }

    [Fact]
    public void ShowWhen_BecomesAnIsVisibleBinding()
    {
        var root = Root(ScreenDefaults.Document(ScreenTarget.Navigation));
        var song = root.Elements().First(e => (string?)e.Attribute("Tag") == "Song");
        Assert.Equal("{Binding HasMusic}", (string?)song.Attribute("IsVisible"));
        var none = root.Elements().First(e => (string?)e.Attribute("Tag") == "No music");
        Assert.Equal("{Binding NoMusic}", (string?)none.Attribute("IsVisible"));
        var clock = root.Elements().First();
        Assert.Null(clock.Attribute("IsVisible"));
    }

    [Fact]
    public void Divider_IsABoxWithoutText()
    {
        var divider = Root(ScreenDefaults.Document(ScreenTarget.Navigation)).Elements().First(e => (string?)e.Attribute("Tag") == "Divider");
        Assert.Empty(divider.Elements());
        Assert.Equal("#C8C8C4", (string?)divider.Attribute("Background"));
        Assert.Equal("#3C4043", (string?)divider.Attribute("NightBackground"));
    }

    [Fact]
    public void Positions_UseTheInvariantCulture()
    {
        var layout = new ScreenLayout { Target = ScreenTarget.VirtualPitBoard };
        layout.Elements.Add(new ScreenElement { Name = "A", Kind = ScreenElementKind.Text, Text = "x", X = 1.5f, Y = 163.25f, Width = 100.5f, Height = 20 });
        var box = Root(ScreenLayoutMigration.ToDocument(layout)).Elements().Single();
        Assert.Equal("1.5", (string?)box.Attribute("Canvas.Left"));
        Assert.Equal("163.25", (string?)box.Attribute("Canvas.Top"));
    }

    [Fact]
    public void Image_BecomesScreenImageWithStretch()
    {
        var layout = new ScreenLayout { Target = ScreenTarget.Navigation };
        layout.Elements.Add(new ScreenElement { Kind = ScreenElementKind.Image, ImageData = "QUJD", Fit = ScreenImageFit.Cover });
        var image = Root(ScreenLayoutMigration.ToDocument(layout)).Elements().Single().Elements().Single();
        Assert.Equal("ScreenImage", image.Name.LocalName);
        Assert.Equal("QUJD", (string?)image.Attribute("Data"));
        Assert.Equal("UniformToFill", (string?)image.Attribute("Stretch"));
    }

    [Fact]
    public void LiteralBraces_AndFormats_SurviveTheEscape()
    {
        var layout = new ScreenLayout { Target = ScreenTarget.Navigation };
        layout.Elements.Add(new ScreenElement { Kind = ScreenElementKind.Text, Text = "{{x}} {time:HH:mm:ss} {rpm}%" });
        var text = Root(ScreenLayoutMigration.ToDocument(layout)).Elements().Single().Elements().Single();
        Assert.Equal("{}{{x}} {time:HH:mm:ss} {rpm}%", (string?)text.Attribute("Template"));
        Assert.True(ScreenLayoutMigration.ToDocument(layout).Pages[0].UsesSeconds);
        Assert.True(ScreenLayoutMigration.ToDocument(layout).Pages[0].UsesCan);
    }

    [Fact]
    public void DuplicateAndOddNames_StillValid()
    {
        var layout = new ScreenLayout { Target = ScreenTarget.Navigation };
        layout.Elements.Add(new ScreenElement { Name = "A \"quoted\" <name>", Kind = ScreenElementKind.Text, Text = "x" });
        layout.Elements.Add(new ScreenElement { Name = "A \"quoted\" <name>", Kind = ScreenElementKind.Text, Text = "y" });
        var doc = ScreenLayoutMigration.ToDocument(layout);
        Assert.Empty(ScreenXamlValidator.Validate(doc.Pages[0].Xaml));
        var reparsed = XElement.Parse(ScreenDocument.Parse(doc.ToAxaml()).Pages[0].Xaml);
        Assert.Equal(2, reparsed.Elements().Count());
        Assert.All(reparsed.Elements(), e => Assert.Equal("A \"quoted\" <name>", (string?)e.Attribute("Tag")));
    }

    [Fact]
    public void Store_FallsBackToDefaultThenRoundTripsThenResets()
    {
        var memory = new MemoryStore();
        var store = new ScreenDocumentStore(memory);

        var first = store.Load(ScreenTarget.Navigation);
        Assert.Equal("main", first.Pages[0].Id);

        first.Pages.Add(new ScreenPage { Id = "extra", Name = "Extra", Xaml = first.Pages[0].Xaml });
        store.Save(ScreenTarget.Navigation, first);
        Assert.Equal(new[] { "main", "extra" }, store.Load(ScreenTarget.Navigation).Pages.Select(p => p.Id));
        Assert.Single(store.Load(ScreenTarget.VirtualPitBoard).Pages);

        store.Reset(ScreenTarget.Navigation);
        Assert.Single(store.Load(ScreenTarget.Navigation).Pages);
    }

    [Fact]
    public void Store_MigratesALegacyJsonLayout_AndIgnoresCorruptSavedText()
    {
        var memory = new MemoryStore();
        var legacy = new ScreenLayout { Name = "Mine", Target = ScreenTarget.Navigation, Background = "#123456" };
        legacy.Elements.Add(new ScreenElement { Name = "Hello", Kind = ScreenElementKind.Text, Text = "hi" });
        memory.SetString("screen_navigation", ScreenLayoutStore.ToJson(legacy));

        var store = new ScreenDocumentStore(memory);
        var migrated = store.Load(ScreenTarget.Navigation);
        Assert.Equal("Mine", migrated.Pages[0].Name);
        Assert.Equal("#123456", (string?)Root(migrated).Attribute("Background"));

        memory.SetString("screen_doc_navigation", "<<not xml");
        Assert.Equal("Mine", store.Load(ScreenTarget.Navigation).Pages[0].Name); // corrupt document ignored, legacy used
    }
}
```

- [ ] **Step 2: Run to verify failure** — expected FAIL (types missing).

- [ ] **Step 3: Implement**

`YccuSdk/Screens/ScreenLayoutMigration.cs`:

```csharp
using System.Globalization;
using System.Xml.Linq;

namespace CNCO.YccuSdk.Screens;

/// <summary>Turns an old <see cref="ScreenLayout"/> (the Skia screen builder's format) into a one-page <see cref="ScreenDocument"/>.</summary>
public static class ScreenLayoutMigration
{
    private static readonly XNamespace Av = ScreenDocument.AvaloniaNs;
    private static readonly XNamespace W = ScreenDocument.WidgetNs;

    public static ScreenDocument ToDocument(ScreenLayout layout)
    {
        var (width, height) = ScreenSizes.DesignSize(layout.Target);
        var root = new XElement(W + "ScreenCanvas",
            new XAttribute("xmlns", ScreenDocument.AvaloniaNs),
            new XAttribute(XNamespace.Xmlns + "x", ScreenDocument.XamlNs),
            new XAttribute(XNamespace.Xmlns + "y", ScreenDocument.WidgetNs),
            new XAttribute("Width", Num(width)),
            new XAttribute("Height", Num(height)),
            new XAttribute("Background", layout.Background));
        if (!string.IsNullOrWhiteSpace(layout.NightBackground)) root.SetAttributeValue("NightBackground", layout.NightBackground);

        foreach (var element in layout.Elements) root.Add(ConvertElement(element));

        var document = new ScreenDocument { Target = layout.Target };
        document.Pages.Add(new ScreenPage { Id = "main", Name = string.IsNullOrWhiteSpace(layout.Name) ? "Main" : layout.Name, Xaml = root.ToString() });
        return document;
    }

    private static XElement ConvertElement(ScreenElement e)
    {
        var box = new XElement(W + "ScreenBox",
            new XAttribute("Canvas.Left", Num(e.X)),
            new XAttribute("Canvas.Top", Num(e.Y)),
            new XAttribute("Width", Num(e.Width)),
            new XAttribute("Height", Num(e.Height)));
        if (!string.IsNullOrWhiteSpace(e.Name)) box.SetAttributeValue("Tag", e.Name);
        if (!string.IsNullOrWhiteSpace(e.Background)) box.SetAttributeValue("Background", e.Background);
        if (!string.IsNullOrWhiteSpace(e.NightBackground)) box.SetAttributeValue("NightBackground", e.NightBackground);
        var visible = VisibleBinding(e.ShowWhen);
        if (visible != null) box.SetAttributeValue("IsVisible", visible);

        if (e.Kind == ScreenElementKind.Image)
        {
            box.Add(new XElement(W + "ScreenImage",
                new XAttribute("Data", e.ImageData ?? ""),
                new XAttribute("Stretch", e.Fit switch { ScreenImageFit.Cover => "UniformToFill", ScreenImageFit.Stretch => "Fill", _ => "Uniform" })));
        }
        else if (!string.IsNullOrEmpty(e.Text))
        {
            var text = new XElement(W + "VarText",
                new XAttribute("Template", e.Text.StartsWith('{') ? "{}" + e.Text : e.Text),
                new XAttribute("FontSize", Num(e.FontSize)),
                new XAttribute("FontWeight", e.Bold ? "Bold" : "Normal"),
                new XAttribute("Foreground", e.Color),
                new XAttribute("TextAlignment", e.Align.ToString()),
                new XAttribute("VerticalAlignment", "Center"),
                new XAttribute("TextTrimming", "CharacterEllipsis"));
            if (!string.IsNullOrWhiteSpace(e.NightColor)) text.SetAttributeValue("NightForeground", e.NightColor);
            box.Add(text);
        }
        return box;
    }

    private static string? VisibleBinding(ScreenShowWhen when) => when switch
    {
        ScreenShowWhen.MusicPresent => "{Binding HasMusic}",
        ScreenShowWhen.MusicPlaying => "{Binding IsPlaying}",
        ScreenShowWhen.MusicPaused => "{Binding IsPaused}",
        ScreenShowWhen.NoMusic => "{Binding NoMusic}",
        ScreenShowWhen.Day => "{Binding Day}",
        ScreenShowWhen.Night => "{Binding Night}",
        _ => null,
    };

    private static string Num(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
```

Add to `ScreenDefaults`:

```csharp
    /// <summary>The built-in screen for <paramref name="target"/> as a (one page) document.</summary>
    public static ScreenDocument Document(ScreenTarget target) => ScreenLayoutMigration.ToDocument(For(target));
```

`YccuSdk/Screens/ScreenDocumentStore.cs`:

```csharp
using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk.Screens;

/// <summary>
/// Saves each target's <see cref="ScreenDocument"/> (as AXAML text) in the platform key-value store. With nothing saved it
/// uses the old JSON layout if there is one (migrated), else the built-in screen. Unreadable saved text is ignored.
/// </summary>
public sealed class ScreenDocumentStore
{
    private readonly IYmcKeyValueStore _storage;
    private readonly ScreenLayoutStore _legacy;

    public ScreenDocumentStore(IYmcKeyValueStore storage)
    {
        _storage = storage;
        _legacy = new ScreenLayoutStore(storage);
    }

    private static string Key(ScreenTarget target) => target == ScreenTarget.Navigation ? "screen_doc_navigation" : "screen_doc_vpb";

    public ScreenDocument Load(ScreenTarget target)
    {
        var text = _storage.GetString(Key(target));
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                var document = ScreenDocument.Parse(text);
                document.Target = target;
                if (document.Pages.All(p => ScreenXamlValidator.Validate(p.Xaml).Count == 0)) return document;
            }
            catch (ScreenDocumentException)
            {
                // Fall through to the old layout / the default.
            }
        }

        // ScreenLayoutStore.Load returns the built-in layout when nothing was saved, so this covers both.
        return ScreenLayoutMigration.ToDocument(_legacy.Load(target));
    }

    public void Save(ScreenTarget target, ScreenDocument document)
    {
        document.Target = target;
        _storage.SetString(Key(target), document.ToAxaml());
    }

    /// <summary>Forgets the saved screen (and any old layout) so <see cref="Load"/> returns the built-in one.</summary>
    public void Reset(ScreenTarget target)
    {
        _storage.SetString(Key(target), "");
        _legacy.Reset(target);
    }
}
```

- [ ] **Step 4: Run tests** — `dotnet test YccuSdk.Tests/YccuSdk.Tests.csproj`; expected all pass.

---

### Task 6: `PageEditModel` (the editor's brain)

**Files:**
- Create: `YccuSdk/Screens/PageEditModel.cs`
- Create: `YccuSdk.Tests/PageEditModelTests.cs`

**Interfaces:**
- Consumes: `ScreenDefaults.Document`, `ScreenSizes.Bounds`, `ScreenXamlValidator`.
- Produces:
  - `enum EditKind { Text, Image, Box, Progress, Other }`
  - `sealed class EditElement` — `Kind`, `Name` (the `Tag`), `X, Y, Width, Height` (floats), `Template, FontSize, Bold, Align, Color, NightColor, Background, NightBackground, Stretch, ShowWhen` as read/write properties that write through to the XML.
  - `sealed class PageEditModel(ScreenTarget target, string pageXaml)`: `IReadOnlyList<EditElement> Elements`, `string RootBackground`, `string RootNightBackground`, `string ToXaml()`, `EditElement AddText()`, `AddBox()`, `AddProgress()`, `AddImage(string base64)`, `Duplicate(EditElement)`, `Delete(EditElement)`, `Move(EditElement, int delta)` (z-order), `SetBounds(EditElement, float x, y, w, h)` (clamps to `ScreenSizes.Bounds`).
  - `enum ScreenShowWhen` reused for `EditElement.ShowWhen` (`Custom` bindings read as `Always` and are preserved until changed).

- [ ] **Step 1: Write the failing tests**

```csharp
using CNCO.YccuSdk.Screens;
using Xunit;

namespace CNCO.YccuSdk.Tests;

public class PageEditModelTests
{
    private static PageEditModel DefaultNav() =>
        new(ScreenTarget.Navigation, ScreenDefaults.Document(ScreenTarget.Navigation).Pages[0].Xaml);

    private static PageEditModel DefaultVpb() =>
        new(ScreenTarget.VirtualPitBoard, ScreenDefaults.Document(ScreenTarget.VirtualPitBoard).Pages[0].Xaml);

    [Fact]
    public void ListsTheElementsOfAMigratedScreen()
    {
        var m = DefaultNav();
        Assert.Equal(new[] { "Clock", "Divider", "Play state", "Song", "Artist", "No music" }, m.Elements.Select(e => e.Name));
        var clock = m.Elements[0];
        Assert.Equal(EditKind.Text, clock.Kind);
        Assert.Equal("{time}", clock.Template);   // shown unescaped
        Assert.Equal(120f, clock.FontSize);
        Assert.True(clock.Bold);
        Assert.Equal(ScreenTextAlign.Center, clock.Align);
        Assert.Equal("#111111", clock.Color);
        Assert.Equal("#FFFFFF", clock.NightColor);
        Assert.Equal(EditKind.Box, m.Elements[1].Kind);
        Assert.Equal(ScreenShowWhen.Always, clock.ShowWhen);
        Assert.Equal(ScreenShowWhen.MusicPresent, m.Elements[3].ShowWhen);
        Assert.Equal("#F4F4F2", m.RootBackground);
        Assert.Equal("#000000", m.RootNightBackground);
    }

    [Fact]
    public void EditsWriteThroughToTheXaml()
    {
        var m = DefaultNav();
        var song = m.Elements[3];
        song.Template = "{artist} / {song}";
        song.FontSize = 20;
        song.Bold = false;
        song.Color = "#ABCDEF";
        song.NightColor = null;
        song.ShowWhen = ScreenShowWhen.MusicPlaying;

        var again = new PageEditModel(ScreenTarget.Navigation, m.ToXaml());
        var s = again.Elements[3];
        Assert.Equal("{artist} / {song}", s.Template);
        Assert.Equal(20f, s.FontSize);
        Assert.False(s.Bold);
        Assert.Equal("#ABCDEF", s.Color);
        Assert.Null(s.NightColor);
        Assert.Equal(ScreenShowWhen.MusicPlaying, s.ShowWhen);
        Assert.Empty(ScreenXamlValidator.Validate(m.ToXaml()));
    }

    [Fact]
    public void Template_StartingWithABrace_IsEscapedInTheXaml()
    {
        var m = DefaultNav();
        m.Elements[0].Template = "{time}";
        Assert.Contains("Template=\"{}{time}\"", m.ToXaml());
    }

    [Fact]
    public void SetBounds_ClampsToTheNavScreen()
    {
        var m = DefaultNav();
        var e = m.Elements[0];
        m.SetBounds(e, -50, -50, 9999, 9999);
        Assert.Equal((0f, 0f, 480f, 240f), (e.X, e.Y, e.Width, e.Height));
        m.SetBounds(e, 470, 230, 100, 100);
        Assert.Equal((380f, 140f, 100f, 100f), (e.X, e.Y, e.Width, e.Height));
    }

    [Fact]
    public void SetBounds_ClampsToTheVisibleVpbStrip()
    {
        var m = DefaultVpb();
        var e = m.Elements[0];
        m.SetBounds(e, 0, 0, 600, 300);
        Assert.Equal((0f, 163f, 526f, 66f), (e.X, e.Y, e.Width, e.Height));
    }

    [Fact]
    public void AddTextBoxProgressImage_AppendAndAreValid()
    {
        var m = DefaultNav();
        var text = m.AddText();
        var box = m.AddBox();
        var bar = m.AddProgress();
        var image = m.AddImage("QUJD");
        Assert.Equal(10, m.Elements.Count);
        Assert.Equal(new[] { EditKind.Text, EditKind.Box, EditKind.Progress, EditKind.Image }, new[] { text.Kind, box.Kind, bar.Kind, image.Kind });
        Assert.Empty(ScreenXamlValidator.Validate(m.ToXaml()));
        var area = ScreenSizes.Bounds(ScreenTarget.Navigation);
        Assert.All(m.Elements.Skip(6), e => Assert.True(e.X >= area.X && e.Y >= area.Y && e.X + e.Width <= area.X + area.Width && e.Y + e.Height <= area.Y + area.Height));
    }

    [Fact]
    public void DeleteDuplicateAndReorder()
    {
        var m = DefaultNav();
        var copy = m.Duplicate(m.Elements[3]);
        Assert.Equal(7, m.Elements.Count);
        Assert.Equal("Song copy", copy.Name);
        Assert.Equal(m.Elements[3].Template, copy.Template);

        m.Delete(copy);
        Assert.Equal(6, m.Elements.Count);

        var first = m.Elements[0];
        m.Move(first, 1);
        Assert.Equal("Divider", m.Elements[0].Name);
        Assert.Equal("Clock", m.Elements[1].Name);
        m.Move(first, -5);                        // clamps at the back
        Assert.Equal("Clock", m.Elements[0].Name);
    }

    [Fact]
    public void UnknownContent_IsKeptAndListedAsOther()
    {
        const string xaml = """
            <y:ScreenCanvas xmlns="https://github.com/avaloniaui" xmlns:y="clr-namespace:CNCO.YammiC.Screens;assembly=YammiC" Width="480" Height="240">
              <Rectangle Canvas.Left="5" Canvas.Top="6" Width="7" Height="8" Fill="#123456" />
              <Grid />
            </y:ScreenCanvas>
            """;
        var m = new PageEditModel(ScreenTarget.Navigation, xaml);
        Assert.Equal(EditKind.Other, m.Elements[0].Kind);
        Assert.Equal((5f, 6f, 7f, 8f), (m.Elements[0].X, m.Elements[0].Y, m.Elements[0].Width, m.Elements[0].Height));
        Assert.Contains("<Grid", m.ToXaml());      // untouched
    }

    [Fact]
    public void BadXaml_Throws()
    {
        Assert.ThrowsAny<Exception>(() => new PageEditModel(ScreenTarget.Navigation, "<oops"));
    }
}
```

- [ ] **Step 2: Run to verify failure** — expected FAIL (types missing).

- [ ] **Step 3: Implement**

`YccuSdk/Screens/PageEditModel.cs`:

```csharp
using System.Globalization;
using System.Xml.Linq;

namespace CNCO.YccuSdk.Screens;

public enum EditKind
{
    Text,
    Image,
    Box,
    Progress,
    Other,
}

/// <summary>
/// One direct child of a page's root canvas, as the visual editor sees it. Property setters write straight into the XML
/// (<see cref="PageEditModel.ToXaml"/> returns the result). <c>ScreenBox</c> children are Text (holding a VarText), Image
/// (holding a ScreenImage) or Box (empty); <c>MediaProgress</c> is Progress; anything else is Other (position only).
/// </summary>
public sealed class EditElement
{
    internal EditElement(XElement node)
    {
        Node = node;
    }

    internal XElement Node { get; }

    private XElement? Inner => Node.Elements().FirstOrDefault();

    public EditKind Kind => Node.Name.LocalName switch
    {
        "ScreenBox" => Inner?.Name.LocalName switch { "VarText" => EditKind.Text, "ScreenImage" => EditKind.Image, _ => EditKind.Box },
        "MediaProgress" => EditKind.Progress,
        _ => EditKind.Other,
    };

    public string Name
    {
        get => (string?)Node.Attribute("Tag") ?? "";
        set => Set(Node, "Tag", string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public float X { get => Get("Canvas.Left"); internal set => Set(Node, "Canvas.Left", Num(value)); }
    public float Y { get => Get("Canvas.Top"); internal set => Set(Node, "Canvas.Top", Num(value)); }
    public float Width { get => Get("Width", 100); internal set => Set(Node, "Width", Num(value)); }
    public float Height { get => Get("Height", 40); internal set => Set(Node, "Height", Num(value)); }

    public string Template
    {
        get
        {
            var raw = (string?)Inner?.Attribute("Template") ?? "";
            return raw.StartsWith("{}", StringComparison.Ordinal) ? raw[2..] : raw;
        }
        set
        {
            if (Inner is { } inner) Set(inner, "Template", value.StartsWith('{') ? "{}" + value : value);
        }
    }

    public float FontSize
    {
        get => float.TryParse((string?)Inner?.Attribute("FontSize"), CultureInfo.InvariantCulture, out var v) ? v : 32;
        set => Set(Inner, "FontSize", Num(Math.Max(value, 1)));
    }

    public bool Bold
    {
        get => (string?)Inner?.Attribute("FontWeight") == "Bold";
        set => Set(Inner, "FontWeight", value ? "Bold" : "Normal");
    }

    public ScreenTextAlign Align
    {
        get => Enum.TryParse<ScreenTextAlign>((string?)Inner?.Attribute("TextAlignment"), out var v) ? v : ScreenTextAlign.Left;
        set => Set(Inner, "TextAlignment", value.ToString());
    }

    public string Color
    {
        get => (string?)Inner?.Attribute("Foreground") ?? (string?)Node.Attribute("Fill") ?? "#FFFFFF";
        set
        {
            if (Kind == EditKind.Progress) Set(Node, "Fill", value);
            else Set(Inner, "Foreground", string.IsNullOrWhiteSpace(value) ? "#FFFFFF" : value.Trim());
        }
    }

    public string? NightColor
    {
        get => (string?)Inner?.Attribute("NightForeground");
        set => Set(Inner, "NightForeground", string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public string? Background
    {
        get => (string?)Node.Attribute(Kind == EditKind.Progress ? "Track" : "Background");
        set => Set(Node, Kind == EditKind.Progress ? "Track" : "Background", string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public string? NightBackground
    {
        get => (string?)Node.Attribute("NightBackground");
        set => Set(Node, "NightBackground", string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public string Stretch
    {
        get => (string?)Inner?.Attribute("Stretch") ?? "Uniform";
        set => Set(Inner, "Stretch", value);
    }

    public ScreenShowWhen ShowWhen
    {
        get => ((string?)Node.Attribute("IsVisible")) switch
        {
            "{Binding HasMusic}" => ScreenShowWhen.MusicPresent,
            "{Binding IsPlaying}" => ScreenShowWhen.MusicPlaying,
            "{Binding IsPaused}" => ScreenShowWhen.MusicPaused,
            "{Binding NoMusic}" => ScreenShowWhen.NoMusic,
            "{Binding Day}" => ScreenShowWhen.Day,
            "{Binding Night}" => ScreenShowWhen.Night,
            _ => ScreenShowWhen.Always,
        };
        set => Set(Node, "IsVisible", value switch
        {
            ScreenShowWhen.MusicPresent => "{Binding HasMusic}",
            ScreenShowWhen.MusicPlaying => "{Binding IsPlaying}",
            ScreenShowWhen.MusicPaused => "{Binding IsPaused}",
            ScreenShowWhen.NoMusic => "{Binding NoMusic}",
            ScreenShowWhen.Day => "{Binding Day}",
            ScreenShowWhen.Night => "{Binding Night}",
            _ => null,
        });
    }

    private float Get(string attribute, float fallback = 0) =>
        float.TryParse((string?)Node.Attribute(attribute), CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static void Set(XElement? element, string attribute, string? value) => element?.SetAttributeValue(attribute, value);

    private static string Num(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>Edits one page's XAML as a list of elements. Positions are in the target's design space and are kept inside <see cref="ScreenSizes.Bounds"/>.</summary>
public sealed class PageEditModel
{
    private static readonly XNamespace W = ScreenDocument.WidgetNs;

    private readonly ScreenTarget _target;
    private readonly XElement _root;
    private readonly List<EditElement> _elements = new();

    /// <exception cref="System.Xml.XmlException">The XAML isn't well-formed.</exception>
    public PageEditModel(ScreenTarget target, string pageXaml)
    {
        _target = target;
        _root = XElement.Parse(pageXaml);
        foreach (var child in _root.Elements()) _elements.Add(new EditElement(child));
    }

    public IReadOnlyList<EditElement> Elements => _elements;

    public string RootBackground
    {
        get => (string?)_root.Attribute("Background") ?? "#000000";
        set => _root.SetAttributeValue("Background", string.IsNullOrWhiteSpace(value) ? "#000000" : value.Trim());
    }

    public string RootNightBackground
    {
        get => (string?)_root.Attribute("NightBackground") ?? "";
        set => _root.SetAttributeValue("NightBackground", string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public string ToXaml() => _root.ToString();

    public EditElement AddText()
    {
        var area = ScreenSizes.Bounds(_target);
        var height = Math.Min(48f, area.Height);
        var box = NewBox("Text", area.X + area.Width * 0.1f, area.Y + (area.Height - height) / 2f, area.Width * 0.8f, height);
        box.Add(new XElement(W + "VarText",
            new XAttribute("Template", "{}{time}"), new XAttribute("FontSize", "32"), new XAttribute("FontWeight", "Normal"),
            new XAttribute("Foreground", "#FFFFFF"), new XAttribute("TextAlignment", "Center"),
            new XAttribute("VerticalAlignment", "Center"), new XAttribute("TextTrimming", "CharacterEllipsis")));
        return Add(box);
    }

    public EditElement AddBox()
    {
        var area = ScreenSizes.Bounds(_target);
        var box = NewBox("Box", area.X + area.Width * 0.25f, area.Y + area.Height * 0.25f, area.Width * 0.5f, Math.Min(20f, area.Height));
        box.SetAttributeValue("Background", "#808080");
        return Add(box);
    }

    public EditElement AddProgress()
    {
        var area = ScreenSizes.Bounds(_target);
        var height = Math.Min(12f, area.Height);
        var bar = new XElement(W + "MediaProgress", new XAttribute("Tag", "Progress"),
            new XAttribute("Canvas.Left", Num(area.X + area.Width * 0.1f)), new XAttribute("Canvas.Top", Num(area.Y + area.Height / 2f)),
            new XAttribute("Width", Num(area.Width * 0.8f)), new XAttribute("Height", Num(height)),
            new XAttribute("Value", "{Binding Progress}"), new XAttribute("Fill", "#FFFFFF"), new XAttribute("Track", "#555555"));
        return Add(bar);
    }

    public EditElement AddImage(string base64)
    {
        var area = ScreenSizes.Bounds(_target);
        var box = NewBox("Image", area.X + area.Width * 0.25f, area.Y + area.Height * 0.2f, area.Width * 0.5f, area.Height * 0.6f);
        box.Add(new XElement(W + "ScreenImage", new XAttribute("Data", base64), new XAttribute("Stretch", "Uniform")));
        return Add(box);
    }

    public EditElement Duplicate(EditElement element)
    {
        var copy = new XElement(element.Node);
        var added = Add(copy, after: element);
        added.Name = element.Name + " copy";
        SetBounds(added, added.X + 10, added.Y + 10, added.Width, added.Height);
        return added;
    }

    public void Delete(EditElement element)
    {
        element.Node.Remove();
        _elements.Remove(element);
    }

    /// <summary>Moves an element in the drawing order (positive = towards the front), stopping at either end.</summary>
    public void Move(EditElement element, int delta)
    {
        var index = _elements.IndexOf(element);
        if (index < 0) return;
        var target = Math.Clamp(index + delta, 0, _elements.Count - 1);
        if (target == index) return;

        _elements.RemoveAt(index);
        _elements.Insert(target, element);
        element.Node.Remove();
        if (target == 0) _root.AddFirst(element.Node);
        else _elements[target - 1].Node.AddAfterSelf(element.Node);
    }

    /// <summary>Sets position and size, moved and shrunk as needed to lie inside the area the bike shows.</summary>
    public void SetBounds(EditElement element, float x, float y, float width, float height)
    {
        var b = ScreenSizes.Bounds(_target);
        width = Math.Clamp(width, 1, b.Width);
        height = Math.Clamp(height, 1, b.Height);
        element.X = Math.Clamp(x, b.X, b.X + b.Width - width);
        element.Y = Math.Clamp(y, b.Y, b.Y + b.Height - height);
        element.Width = width;
        element.Height = height;
    }

    private XElement NewBox(string name, float x, float y, float width, float height) =>
        new(W + "ScreenBox", new XAttribute("Tag", name),
            new XAttribute("Canvas.Left", Num(x)), new XAttribute("Canvas.Top", Num(y)),
            new XAttribute("Width", Num(width)), new XAttribute("Height", Num(height)));

    private EditElement Add(XElement node, EditElement? after = null)
    {
        var element = new EditElement(node);
        if (after == null)
        {
            _root.Add(node);
            _elements.Add(element);
        }
        else
        {
            after.Node.AddAfterSelf(node);
            _elements.Insert(_elements.IndexOf(after) + 1, element);
        }
        return element;
    }

    private static string Num(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: Run tests** — `dotnet test YccuSdk.Tests/YccuSdk.Tests.csproj`; expected all pass. Note the VPB default test assumes the "Strip" element is first; if `SetBounds(...,0,0,600,300)` on a 526x66 strip returns different numbers, fix the model, not the strip.

---

### Task 7: Wire the pages into `ScreenPresenter`, `BikeService`, `IBikeController`; remove the spike

**Files:**
- Modify: `YccuSdk/Screens/ScreenPresenter.cs`
- Modify: `YccuSdk/Bike/BikeService.cs` (fields ~101–106, ctor ~143–195, `SaveScreenLayout` region ~856–890, `VpbScreenEnabled`, `OnZoomRequest` is Task 8)
- Modify: `YccuSdk/Bike/BikeService.cs` `BikeServiceOptions` (add `ScreenRenderer`)
- Modify: `YammiC/YammiC/YammiC/Services/IBikeController.cs` (lines ~66–72), `BrokerBikeController.cs` (~203–208), `FakeBikeController.cs` (~100–107)
- Modify: `YammiC/YammiC/YammiC.Android/Application.cs` (pass renderer; remove `RenderSpike.Start`)
- Delete: `YammiC/YammiC/YammiC.Android/RenderSpike.cs`

**Interfaces:**
- Consumes: Tasks 1–5.
- Produces:
  - `ScreenPresenter(string name, ScreenTarget target, IScreenRenderer? renderer, Func<ScreenPage> page, Func<ScreenSnapshot> snapshot, Func<(int Width,int Height)> size, Func<bool> canSend, Func<byte[],Task<bool>> send, TimeSpan tick, TimeSpan minInterval, int quality, Logger logger, Func<bool>? wantsFastTick = null, TimeSpan? fastTick = null)`.
  - `BikeServiceOptions.ScreenRenderer : IScreenRenderer?`
  - `BikeService`: `string GetScreenAxaml(ScreenTarget)`, `IReadOnlyList<ScreenIssue> SaveScreenAxaml(ScreenTarget, string axaml)` (empty list = saved), `void ResetScreenDocument(ScreenTarget)`, `bool ScreenRenderingSupported`.
  - `IBikeController`: the three layout members become `string GetScreenAxaml(ScreenTarget)`, `IReadOnlyList<ScreenIssue> SaveScreenAxaml(ScreenTarget, string axaml)`, `void ResetScreenDocument(ScreenTarget)`.

- [ ] **Step 1: Presenter**

In `ScreenPresenter.cs`: replace the constructor parameters `layout`/`context` and the fields `_layout`/`_context` with `_target`, `_renderer`, `_page`, `_snapshot`; keep everything else. Replace the render section of `RefreshAsync`:

```csharp
    private async Task RefreshAsync()
    {
        if (!IsRunning || !_canSend()) return;
        if (_renderer == null)
        {
            LogOnce("no screen renderer on this platform");
            return;
        }
        if (!await _busy.WaitAsync(0).ConfigureAwait(false)) return; // a send is still in flight
        try
        {
            var page = _page();
            var snapshot = _snapshot();
            var (width, height) = _size();
            var signature = $"{width}x{height}|{page.Id}|{page.Xaml.GetHashCode()}|{snapshot.Signature(page.UsesSeconds)}";

            lock (_gate)
            {
                if (signature == _lastSignature) return;
                if (DateTime.UtcNow - _lastAttempt < _minInterval) return;
                _lastAttempt = DateTime.UtcNow;
            }

            using var bitmap = await _renderer.RenderAsync(_target, page, snapshot, width, height).ConfigureAwait(false);
            if (bitmap == null)
            {
                // Keep showing the last good picture; don't retry until the page or state changes.
                LogOnce($"page \"{page.Id}\" can't be drawn: {_renderer.LastError}");
                lock (_gate) _lastSignature = signature;
                return;
            }

            var jpeg = YmcImaging.EncodeJpeg(bitmap, _quality);
            var sent = await _send(jpeg).ConfigureAwait(false);
            if (sent)
            {
                lock (_gate) _lastSignature = signature;
            }
            else
            {
                _log.Debug("send failed, will retry");
            }
        }
        catch (Exception t)
        {
            _log.Debug($"refresh failed: {t.Message}");
        }
        finally
        {
            _busy.Release();
        }
    }

    private string? _lastLogged;

    private void LogOnce(string message)
    {
        if (message == _lastLogged) return;
        _lastLogged = message;
        _log.Debug(message);
    }
```

Remove the now-unused `using` for `ScreenRenderer` if any. `ScreenRenderer.cs` itself stays (the reels message screen still uses it).

- [ ] **Step 2: BikeService**

Replace the layout fields and load:

```csharp
    private readonly ScreenDocumentStore _screenDocs;
    private readonly ScreenController _navController;
    private readonly ScreenController _vpbController;
```
(remove `_screens`, `_navLayout`, `_vpbLayout`). In the constructor replace lines 143–145 with:

```csharp
        _screenDocs = new ScreenDocumentStore(platform.Storage);
        var media = new ScreenMediaActions(this);
        _navController = new ScreenController(_screenDocs.Load(ScreenTarget.Navigation), media);
        _vpbController = new ScreenController(_screenDocs.Load(ScreenTarget.VirtualPitBoard), media);
```

Add the nested class (bottom of `BikeService`, same file) implementing the media actions with the existing `IYmcMediaController` (check the exact namespace of `CcuMusicControlRequest` at the top of `YccuSdk/Packets/ccu/CcuMusicControlRequest.cs` and add its `using`):

```csharp
    private sealed class ScreenMediaActions : IScreenMediaActions
    {
        private readonly BikeService _owner;

        public ScreenMediaActions(BikeService owner) => _owner = owner;

        public void PlayPause() =>
            Send(_owner.MediaMetadata?.PlaybackState == YmcMediaPlaybackState.Playing ? CcuMusicControlRequest.Action.Pause : CcuMusicControlRequest.Action.Play);

        public void Next() => Send(CcuMusicControlRequest.Action.NextTrack);

        public void Previous() => Send(CcuMusicControlRequest.Action.PreviousTrack);

        private void Send(CcuMusicControlRequest.Action action)
        {
            var controller = _owner._options.MediaController;
            if (controller != null && !controller.TryTransportControl(action)) controller.DispatchMediaKey(action);
        }
    }
```

Replace the two presenter constructions (lines ~165–206):

```csharp
        ScreenSnapshot CurrentSnapshot(ScreenController controller) =>
            controller.Snapshot(new(DateTime.Now, MediaMetadata, _navigation.DayNightMode == NavDayNightMode.Night, 1f,
                _canDataEnabled ? _can.Values(DateTime.UtcNow) : null));

        _navScreen = new ScreenPresenter(
            "NavScreen",
            ScreenTarget.Navigation,
            _options.ScreenRenderer,
            () => _navController.CurrentPage,
            () => CurrentSnapshot(_navController),
            () => (navigation.DisplaySize.Width, navigation.DisplaySize.Height),
            () => navigation.State is NavigationState.Active,
            jpeg =>
            {
                navigation.PushImage(jpeg);
                return Task.FromResult(true);
            },
            tick: TimeSpan.FromSeconds(1),
            minInterval: TimeSpan.Zero,
            quality: 85,
            logger,
            wantsFastTick: () => _canDataEnabled && _navController.CurrentPage.UsesCan,
            fastTick: TimeSpan.FromMilliseconds(500));

        // A pit board picture is a multi-chunk transfer, so: check rarely, send rarely, and not while the connect
        // sequence is still using the board (its "Connected" message).
        _vpbScreen = new ScreenPresenter(
            "VpbScreen",
            ScreenTarget.VirtualPitBoard,
            _options.ScreenRenderer,
            () => _vpbController.CurrentPage,
            () => CurrentSnapshot(_vpbController),
            () => ScreenSizes.DesignSize(ScreenTarget.VirtualPitBoard),
            () => _vpbEnabled && _vpbScreenEnabled && _controller.IsBikeConnected &&
                  DateTime.UtcNow - _connectedAt > TimeSpan.FromSeconds(8),
            async jpeg =>
            {
                var sent = await _controller.SendVirtualPitBoardMessageAsync(jpeg).ConfigureAwait(false);
                if (sent) SetVpbStatus("Custom screen");
                return sent;
            },
            tick: TimeSpan.FromSeconds(3),
            minInterval: TimeSpan.FromSeconds(15),
            quality: 85,
            logger);

        _navController.Changed += () => _navScreen.Refresh();
        _vpbController.Changed += () => _vpbScreen.Refresh();
```

(Keep the original `CanValues` constructor argument order: `ScreenContext(Now, Media, Night, Zoom, Can)` — the call above matches it. The old `CurrentContext()` local function and its `Zoom = 1f` wrinkle are removed.)

Add to `BikeServiceOptions`: `public IScreenRenderer? ScreenRenderer { get; init; }`.

Replace the layout members (~856–890) with:

```csharp
    /// <summary>The saved screen for <paramref name="target"/> (the built-in one if none was saved) as AXAML text.</summary>
    public string GetScreenAxaml(ScreenTarget target) => Controller(target).Document.ToAxaml();

    /// <summary>
    /// Checks and saves <paramref name="axaml"/> as the screen for <paramref name="target"/> and puts it on the bike's display
    /// straight away (if that screen is running). Returns the problems and saves nothing if there are any.
    /// </summary>
    public IReadOnlyList<ScreenIssue> SaveScreenAxaml(ScreenTarget target, string axaml)
    {
        ScreenDocument document;
        try
        {
            document = ScreenDocument.Parse(axaml);
        }
        catch (ScreenDocumentException e)
        {
            return new[] { new ScreenIssue(e.Line, e.Message) };
        }

        var issues = new List<ScreenIssue>();
        foreach (var page in document.Pages)
            foreach (var issue in ScreenXamlValidator.Validate(page.Xaml))
                issues.Add(issue with { Message = $"page \"{page.Id}\": {issue.Message}" });
        if (issues.Count > 0) return issues;

        document.Target = target;
        _screenDocs.Save(target, document);
        UseScreenDocument(target, document);
        return Array.Empty<ScreenIssue>();
    }

    /// <summary>Discards the saved screen for <paramref name="target"/> and goes back to the built-in one.</summary>
    public void ResetScreenDocument(ScreenTarget target)
    {
        _screenDocs.Reset(target);
        UseScreenDocument(target, _screenDocs.Load(target));
    }

    /// <summary>False where pages can't be drawn (no renderer, or the platform can't load XAML at run time).</summary>
    public bool ScreenRenderingSupported => _options.ScreenRenderer != null;

    private ScreenController Controller(ScreenTarget target) => target == ScreenTarget.Navigation ? _navController : _vpbController;

    private void UseScreenDocument(ScreenTarget target, ScreenDocument document)
    {
        Controller(target).SetDocument(document);
        if (target == ScreenTarget.Navigation) _navScreen.Invalidate();
        else _vpbScreen.Invalidate();
    }
```

Fix any remaining references (`grep -n "_navLayout\|_vpbLayout\|_screens\b\|ScreenVariables.UsesCan\|GetScreenLayout" YccuSdk/Bike/BikeService.cs` must return nothing).

- [ ] **Step 3: Controller interface members**

`IBikeController.cs` (keep the doc comments' intent; replace only the three members):

```csharp
    /// <summary>The saved screen for <paramref name="target"/> (the built-in one if none was saved) as AXAML text.</summary>
    string GetScreenAxaml(ScreenTarget target);

    /// <summary>Checks and saves a screen and shows it on the bike. Returns the problems found (nothing is saved if there are any).</summary>
    System.Collections.Generic.IReadOnlyList<ScreenIssue> SaveScreenAxaml(ScreenTarget target, string axaml);

    /// <summary>Discards the saved screen and goes back to the built-in one.</summary>
    void ResetScreenDocument(ScreenTarget target);
```

`BrokerBikeController.cs`:

```csharp
    public string GetScreenAxaml(ScreenTarget target) =>
        _service?.GetScreenAxaml(target) ?? ScreenDefaults.Document(target).ToAxaml();

    public IReadOnlyList<ScreenIssue> SaveScreenAxaml(ScreenTarget target, string axaml) =>
        _service?.SaveScreenAxaml(target, axaml) ?? Array.Empty<ScreenIssue>();

    public void ResetScreenDocument(ScreenTarget target) => _service?.ResetScreenDocument(target);
```

`FakeBikeController.cs`:

```csharp
    private readonly System.Collections.Generic.Dictionary<ScreenTarget, string> _screens = new();

    public string GetScreenAxaml(ScreenTarget target) =>
        _screens.TryGetValue(target, out var axaml) ? axaml : ScreenDefaults.Document(target).ToAxaml();

    public System.Collections.Generic.IReadOnlyList<ScreenIssue> SaveScreenAxaml(ScreenTarget target, string axaml)
    {
        _screens[target] = axaml;
        return System.Array.Empty<ScreenIssue>();
    }

    public void ResetScreenDocument(ScreenTarget target) => _screens.Remove(target);
```

- [ ] **Step 4: Android head**

In `Application.cs`: delete the `RenderSpike.Start(logger);` line, add `ScreenRenderer = AvaloniaScreenRenderer.IsSupported ? new AvaloniaScreenRenderer() : null,` to `BikeServiceOptions`, add `using CNCO.YammiC.Screens;`. Delete `RenderSpike.cs`.

- [ ] **Step 5: Build everything that exists**

`dotnet build YccuSdk/YccuSdk.csproj -c Debug` → `0 Error(s)`.
`dotnet build YammiC/YammiC.Android/YammiC.Android.csproj -c Debug` → `0 Error(s)` (this will fail in `ScreenEditorViewModel.cs` for the removed controller members until Task 11; to keep the build green now, make the minimal compile fix there: replace the calls with the new members as in Task 11 Step 3, or finish Task 11 first). Run `dotnet test YccuSdk.Tests/YccuSdk.Tests.csproj` → all pass.

Manual check (phone): with Nav mode "UI" the bike shows the default clock/music screen (rendered by Avalonia now), updating as before; with a bad page saved via the editor's raw tab (Task 11) the old picture stays and the log has one `page "x" can't be drawn:` line.

---

### Task 8: Zoom buttons as events

**Files:**
- Modify: `YccuSdk/Bike/BikeService.cs` (`OnZoomRequest`)

- [ ] **Step 1: Route zoom by mode**

Replace `OnZoomRequest`:

```csharp
    /// <summary>
    /// The bike's zoom buttons. Reels: scroll the videos. UI: they are events for the current page (default: none, so nothing
    /// happens). Sandwich: zoom the picture.
    /// </summary>
    private void OnZoomRequest(bool zoomIn)
    {
        if (_navMode == NavMode.Reels && _reels != null)
        {
            if (zoomIn) _reels.Next();
            else _reels.Previous();
            // The bike greys out its buttons until it hears the zoom level back, even when nothing zoomed.
            _navigation.AcknowledgeZoom();
            return;
        }

        if (_navMode == NavMode.Ui)
        {
            _navController.Handle(zoomIn ? ScreenEvent.ZoomIn : ScreenEvent.ZoomOut);
            _navigation.AcknowledgeZoom();
            return;
        }

        if (zoomIn) _navigation.ZoomIn();
        else _navigation.ZoomOut();
        _navScreen.Refresh();
    }
```

- [ ] **Step 2: Default pages respond to zoom**

In `ScreenDefaults.Document` leave actions empty (a one-page default has nothing to page through). Nothing else to change.

- [ ] **Step 3: Build and test** — `dotnet build YccuSdk/YccuSdk.csproj` `0 Error(s)`; on the phone, Nav mode "UI": pressing zoom in/out leaves the picture alone and the bike's zoom buttons stay enabled (zoom-level reply still sent). After adding a second page with `ZoomIn="NextPage"` (Task 11), the button changes page.

---

### Task 9: Editor — canvas control

**Files:**
- Modify (rewrite the model-facing parts): `YammiC/YammiC/YammiC/Controls/ScreenCanvas.cs`

**Interfaces:**
- Consumes: `PageEditModel`, `EditElement`, `ScreenSizes`.
- Produces: `ScreenCanvas` (editor control; not the widget of the same name — it lives in `YammiC.Controls`, the widget in `CNCO.YammiC.Screens`) with `IReadOnlyList<EditElement>? Elements`, `ScreenTarget Target`, `EditElement? Selected`, `Avalonia.Media.Imaging.Bitmap? Preview` (the rendered design-size picture; null until the first render), `event Action<EditElement?> SelectionChanged`, `event Action<EditElement, float, float, float, float> BoundsDragged` (element, x, y, w, h — the owner clamps/applies via `PageEditModel.SetBounds`), `void Refresh()`.

Keep all the existing pointer handling that stops page scrolling (`PreventGestureRecognition`, `Capture`, re-capture on move, `e.Handled`) and the fit/centre maths. The control no longer renders anything itself; the view model supplies `Preview`.

- [ ] **Step 1: Replace the data members and rendering**

Replace the `using`s of `Skia`/`CNCO.YccuSdk.Screens` renderer parts and these members of `ScreenCanvas`:

```csharp
    private IReadOnlyList<EditElement>? _elements;
    private ScreenTarget _target;
    private EditElement? _selected;
    private Bitmap? _preview;

    public event Action<EditElement?>? SelectionChanged;

    /// <summary>The user dragged the selected element to this position/size (design space). The owner applies it (clamped) and calls <see cref="Refresh"/>.</summary>
    public event Action<EditElement, float, float, float, float>? BoundsDragged;

    public IReadOnlyList<EditElement>? Elements
    {
        get => _elements;
        set { _elements = value; Refresh(); }
    }

    public ScreenTarget Target
    {
        get => _target;
        set { _target = value; InvalidateMeasure(); Refresh(); }
    }

    public Bitmap? Preview
    {
        get => _preview;
        set { _preview = value; InvalidateVisual(); }
    }

    public EditElement? Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; InvalidateVisual(); } }
    }

    public void Refresh() => InvalidateVisual();

    private (int Width, int Height) DesignSize => ScreenSizes.DesignSize(_target);

    private (float X, float Y, float Width, float Height) View => ScreenSizes.Bounds(_target);
```

`Render`: replace the bitmap block with drawing `Preview` (a design-size bitmap, so source rect is `view` in design pixels, not ×2):

```csharp
        var view = View;
        var (scale, origin) = Fit();
        var destination = ToScreen(view.X, view.Y, view.Width, view.Height, scale, origin);
        if (_preview != null)
            context.DrawImage(_preview, new Rect(view.X, view.Y, view.Width, view.Height), destination);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Colors.Gray, 0.8), 1), destination);

        var faint = new Pen(new SolidColorBrush(Colors.Gray, 0.5), 1);
        foreach (var element in _elements ?? Array.Empty<EditElement>())
            context.DrawRectangle(null, faint, ToScreen(element.X, element.Y, element.Width, element.Height, scale, origin));
        // selected outline + corner handles: unchanged
```

Delete `RenderToBitmap`, the `Layout`/`Context` properties, `_dirty`, and the SkiaSharp/Marshal usings.

`OnPointerPressed`: replace `_layout == null` with `_elements == null`, and the hit test with `_elements.LastOrDefault(el => design.X >= el.X && ...)` (types now `EditElement`). `_dragStartRect` stays a float tuple.

`OnPointerMoved`: compute the new rectangle exactly as today, but instead of mutating `_selected` fields, raise the event:

```csharp
        float nx, ny, nw, nh;
        if (_drag == Drag.Move)
        {
            (nx, ny, nw, nh) = (x + dx, y + dy, w, h);
        }
        else
        {
            var left = x; var top = y; var right = x + w; var bottom = y + h;
            if (_resizeCorner is 0 or 3) left = Math.Min(x + dx, right - 4); else right = Math.Max(x + w + dx, left + 4);
            if (_resizeCorner is 0 or 1) top = Math.Min(y + dy, bottom - 4); else bottom = Math.Max(y + h + dy, top + 4);
            (nx, ny, nw, nh) = (left, top, right - left, bottom - top);
        }
        BoundsDragged?.Invoke(_selected, nx, ny, nw, nh);
        e.Handled = true;
```

(The clamping that used to happen here now happens in `PageEditModel.SetBounds`.)

- [ ] **Step 2: Build** — `dotnet build YammiC/YammiC/YammiC/YammiC.csproj -f net10.0 -c Debug` → errors only in `ScreenEditorView*`/`ScreenEditorViewModel` (fixed in Task 11).

---

### Task 10: Editor — preview rendering

**Files:**
- Create: `YammiC/YammiC/YammiC/Screens/PreviewRenderer.cs`

**Interfaces:**
- Consumes: `AvaloniaScreenRenderer`, `ScreenSnapshot`.
- Produces: `PreviewRenderer(ScreenTarget target)` with `Task<Avalonia.Media.Imaging.Bitmap?> RenderAsync(ScreenPage page, ScreenSnapshot snapshot)` (coalesces: if a render is running, the newest request replaces any waiting one and only that one runs next), `string? LastError`.

- [ ] **Step 1: Implement**

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CNCO.YccuSdk.Screens;
using SkiaSharp;

namespace CNCO.YammiC.Screens;

/// <summary>
/// The editor's own renderer (separate from the bike's so previewing never disturbs it). Renders at design size and returns an
/// Avalonia bitmap. Requests made while a render runs are coalesced: only the latest one is drawn next.
/// </summary>
public sealed class PreviewRenderer
{
    private readonly ScreenTarget _target;
    private readonly AvaloniaScreenRenderer _renderer = new();
    private (ScreenPage Page, ScreenSnapshot Snapshot, TaskCompletionSource<Bitmap?> Result)? _waiting;
    private bool _running;

    public PreviewRenderer(ScreenTarget target)
    {
        _target = target;
    }

    public string? LastError => _renderer.LastError;

    public Task<Bitmap?> RenderAsync(ScreenPage page, ScreenSnapshot snapshot)
    {
        var result = new TaskCompletionSource<Bitmap?>();
        _waiting?.Result.TrySetResult(null); // superseded
        _waiting = (page, snapshot, result);
        if (!_running) _ = Pump();
        return result.Task;
    }

    private async Task Pump()
    {
        _running = true;
        try
        {
            while (_waiting is { } next)
            {
                _waiting = null;
                var (width, height) = ScreenSizes.DesignSize(_target);
                using var bitmap = await _renderer.RenderAsync(_target, next.Page, next.Snapshot, width, height);
                if (bitmap == null)
                {
                    next.Result.TrySetResult(null);
                    continue;
                }
                using var encoded = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
                next.Result.TrySetResult(new Bitmap(new MemoryStream(encoded.ToArray())));
            }
        }
        finally
        {
            _running = false;
        }
    }
}
```

Both `RenderAsync` and `Pump` run on the UI thread (called from the view model), so the unlocked fields are safe.

- [ ] **Step 2: Build** — compiles with the shared project.

---

### Task 11: Editor — view model and view

**Files:**
- Rewrite: `YammiC/YammiC/YammiC/ViewModels/ScreenEditorViewModel.cs`
- Rewrite: `YammiC/YammiC/YammiC/Views/ScreenEditorView.axaml`, `ScreenEditorView.axaml.cs`

Behaviour:
- Header: display picker (Navigation / Pit board), "Show on the bike" toggle (unchanged semantics: `NavMode.Ui` / `VpbScreenEnabled`).
- Pinned preview (same layout trick: `Grid RowDefinitions="Auto,*"`, preview in row 0, settings scroll in row 1) with Night + music preview toggles and the page's bike-event preview note.
- Settings area has a `TabControl`: **Design** (pages list + elements list + selected-element fields) and **Code** (a monospace `TextBox` bound to the raw AXAML of the whole document; **Check** button; issues list).
- Pages: add / rename / delete / move, per-page ZoomIn/ZoomOut action pickers (text box with the action vocabulary hint), plus document default actions.
- Visual edits regenerate the page XAML; the Code tab shows the whole document (`ToAxaml()`); editing the code and pressing **Check** (or switching back to Design) parses it: if it fails, the issues are shown and the working document is **not** replaced.
- Apply = `SaveScreenAxaml`; issues come back as a list; success message as before. Revert / Reset as before. Import/Export now use `.axaml`.
- Preview context: `PreviewNight`, `PreviewMusicState`, `CanValues.Sample` (as the current editor does).

**Interfaces:**
- Consumes: `IBikeController.GetScreenAxaml/SaveScreenAxaml/ResetScreenDocument`, `ScreenDocument`, `PageEditModel`, `PreviewRenderer`, `ScreenController` (for the preview snapshot: a throwaway `new ScreenController(doc)` is not needed — build `ScreenSnapshot` directly with `ScreenSnapshot.Of(context, page.Id, index, count)`).
- Produces: `ScreenEditorViewModel` public surface used by the view: `Target`, `PreviewNight`, `PreviewMusicState`, `ScreenEnabled`, `Pages` (`ObservableCollection<PageItem>`), `SelectedPage`, `Items` (`ObservableCollection<ElementItem>`), `SelectedItem`, `SelectedElement` (`EditElement?`), `Preview` (`Bitmap?`), `PreviewElements` (`IReadOnlyList<EditElement>?`), `Code` (string), `Issues` (`ObservableCollection<string>`), `Status`, commands, plus `OnBoundsDragged(EditElement,float,float,float,float)`, `Select(EditElement?)`, `AddImage(byte[] file, string name)`, `ReplaceImage(byte[])`, `ExportAxaml()`, `ImportAxaml(string)`.

- [ ] **Step 1: View model skeleton and document handling**

Keep from the current file, unchanged: `PreviewMusic` enum, `Targets`, `PreviewMusics`, `VariableHelp`, `ScreenEnabled` semantics, `PrepareImage` (image shrinking), the `_loading` guard pattern, the `Sel*` field pattern (`Edit(...)`, `EditGeometry(...)`, `NullIfEmpty`), `Status`. Replace the layout members. Core new code:

```csharp
public sealed record PageItemInfo(string Id, string Name);

public partial class PageItem : ObservableObject
{
    public PageItem(ScreenPage page) { Page = page; }
    public ScreenPage Page { get; }
    public string Title => string.IsNullOrWhiteSpace(Page.Name) ? Page.Id : Page.Name;
    public void Refresh() => OnPropertyChanged(nameof(Title));
}

public partial class ElementItem : ObservableObject
{
    public ElementItem(EditElement element) { Element = element; }
    public EditElement Element { get; }
    public string Title => string.IsNullOrWhiteSpace(Element.Name) ? Element.Kind.ToString() : Element.Name;
    public string Kind => Element.Kind.ToString();
    public void Refresh() => OnPropertyChanged(nameof(Title));
}
```

Fields and loading:

```csharp
    private ScreenDocument _document = new();
    private PageEditModel? _model;                 // the selected page's XAML as elements
    private readonly PreviewRenderer?[] _renderers = new PreviewRenderer?[2];

    private void LoadTarget()
    {
        _loading = true;
        try
        {
            var text = _controller.GetScreenAxaml(Target);
            _document = TryParseOrDefault(text);
            Code = _document.ToAxaml();
            Issues.Clear();
            ScreenEnabled = Target == ScreenTarget.Navigation ? _controller.NavigationMode == NavMode.Ui : _controller.VpbScreenEnabled;
            Pages.Clear();
            foreach (var page in _document.Pages) Pages.Add(new PageItem(page));
            DefaultZoomIn = _document.DefaultActions.GetValueOrDefault(ScreenEvent.ZoomIn, "");
            DefaultZoomOut = _document.DefaultActions.GetValueOrDefault(ScreenEvent.ZoomOut, "");
        }
        finally { _loading = false; }
        SelectedPage = Pages.FirstOrDefault();
    }

    private ScreenDocument TryParseOrDefault(string text)
    {
        try { return ScreenDocument.Parse(text); }
        catch (ScreenDocumentException) { return ScreenDefaults.Document(Target); }
    }

    partial void OnSelectedPageChanged(PageItem? value)
    {
        _model = null;
        Items.Clear();
        SelectedItem = null;
        if (value == null) { Preview = null; return; }

        PageZoomIn = value.Page.Actions.GetValueOrDefault(ScreenEvent.ZoomIn, "");
        PageZoomOut = value.Page.Actions.GetValueOrDefault(ScreenEvent.ZoomOut, "");
        try
        {
            _model = new PageEditModel(Target, value.Page.Xaml);
            foreach (var element in _model.Elements) Items.Add(new ElementItem(element));
            PageBackground = _model.RootBackground;
            PageNightBackground = _model.RootNightBackground;
            PreviewElements = _model.Elements;
        }
        catch (System.Xml.XmlException e)
        {
            Issues.Add($"page \"{value.Page.Id}\": {e.Message}");
            PreviewElements = null;
        }
        RequestPreview();
    }
```

Changing the design writes back into the page and the Code tab:

```csharp
    /// <summary>Call after any visual edit: store the page XAML, refresh the code view and the picture.</summary>
    private void PageEdited()
    {
        if (_loading || _model == null || SelectedPage == null) return;
        SelectedPage.Page.Xaml = _model.ToXaml();
        Code = _document.ToAxaml();
        RequestPreview();
    }

    private async void RequestPreview()
    {
        if (SelectedPage == null) return;
        var renderer = _renderers[(int)Target] ??= new PreviewRenderer(Target);
        var index = Pages.IndexOf(SelectedPage);
        var snapshot = ScreenSnapshot.Of(PreviewContext, SelectedPage.Page.Id, Math.Max(index, 0), Math.Max(Pages.Count, 1));
        var bitmap = await renderer.RenderAsync(SelectedPage.Page, snapshot);
        if (bitmap != null) Preview = bitmap;
        else if (renderer.LastError != null) Status = $"Can't draw this page: {renderer.LastError}";
    }
```

(`PreviewContext` is built exactly like the current `UpdatePreviewContext()`. `Preview` is a plain `[ObservableProperty] Bitmap?`.)

Selection and field edits: port the existing `Sel*` properties to `EditElement`. Properties and their element mapping:

| VM property | `EditElement` member |
|---|---|
| `SelName` | `Name` |
| `SelX/Y/Width/Height` | via `_model.SetBounds(e, x, y, w, h)` then echo back (the clamped values) |
| `SelShowWhen` | `ShowWhen` |
| `SelText` | `Template` |
| `SelFontSize`, `SelBold`, `SelAlign` | `FontSize`, `Bold`, `Align` |
| `SelColor`, `SelNightColor` | `Color`, `NightColor` |
| `SelBackground`, `SelNightBackground` | `Background`, `NightBackground` |
| `SelFit` (Contain/Cover/Stretch) | `Stretch` ("Uniform"/"UniformToFill"/"Fill") |

`IsTextSelected` = `Kind == Text`; `IsImageSelected` = `Kind == Image`; `IsProgressSelected` = `Kind == Progress` (shows only colour/background/position fields). Every `Edit(...)` ends with `PageEdited()` (instead of the old `Changed()`).

Canvas drag handler:

```csharp
    public void OnBoundsDragged(EditElement element, float x, float y, float w, float h)
    {
        if (_model == null) return;
        _model.SetBounds(element, x, y, w, h);
        EchoGeometry(element);   // sets SelX.. under _loading so the fields show the clamped numbers
        PageEdited();
    }
```

Commands (each calls `PageEdited()` after changing `_model`):
`AddText` (`_model.AddText()`), `AddBox`, `AddProgress`, `AddImage(file,name)` (`PrepareImage` then `_model.AddImage(Convert.ToBase64String(data))`), `DuplicateElement`, `DeleteElement`, `MoveForward` (`Move(+1)`), `MoveBackward` (`Move(-1)`); after add/duplicate: rebuild `Items` and select the new element.

Page commands:

```csharp
    [RelayCommand]
    private void AddPage()
    {
        var id = NextPageId();
        var blank = new ScreenPage { Id = id, Name = "Page " + (Pages.Count + 1), Xaml = BlankPageXaml() };
        _document.Pages.Add(blank);
        var item = new PageItem(blank);
        Pages.Add(item);
        SelectedPage = item;
        Code = _document.ToAxaml();
    }

    [RelayCommand]
    private void DeletePage()
    {
        if (SelectedPage == null || Pages.Count <= 1) { Status = "A screen needs at least one page."; return; }
        var item = SelectedPage;
        _document.Pages.Remove(item.Page);
        Pages.Remove(item);
        SelectedPage = Pages.FirstOrDefault();
        Code = _document.ToAxaml();
    }

    [RelayCommand] private void MovePageUp() => MovePage(-1);
    [RelayCommand] private void MovePageDown() => MovePage(1);

    private string NextPageId()
    {
        for (var i = Pages.Count + 1; ; i++)
            if (_document.Find("page" + i) == null) return "page" + i;
    }

    private string BlankPageXaml() =>
        ScreenLayoutMigration.ToDocument(new ScreenLayout { Target = Target, Background = "#000000" }).Pages[0].Xaml;
```

(`MovePage` swaps in both `_document.Pages` and `Pages`, keeps selection, refreshes `Code`.) Page name edits: `partial void OnPageNameChanged(string value)` sets `SelectedPage.Page.Name`, refreshes the item's title and `Code`. Page actions: `PageZoomIn/PageZoomOut` and `DefaultZoomIn/DefaultZoomOut` text properties: on change, validate with `ScreenAction.TryParse` (empty = remove the entry; invalid = `Status = "Unknown action \"...\""` and do **not** store), store in `Page.Actions` / `_document.DefaultActions`, refresh `Code`.

Code tab:

```csharp
    [RelayCommand]
    private void CheckCode()
    {
        Issues.Clear();
        ScreenDocument parsed;
        try { parsed = ScreenDocument.Parse(Code); }
        catch (ScreenDocumentException e) { Issues.Add(e.Message); Status = "The code has problems."; return; }

        foreach (var page in parsed.Pages)
            foreach (var issue in ScreenXamlValidator.Validate(page.Xaml))
                Issues.Add($"page \"{page.Id}\": {issue}");
        if (Issues.Count > 0) { Status = "The code has problems."; return; }

        // Good: it replaces the working document (the visual editor re-reads it).
        parsed.Target = Target;
        _document = parsed;
        var keep = SelectedPage?.Page.Id;
        _loading = true;
        try
        {
            Pages.Clear();
            foreach (var page in _document.Pages) Pages.Add(new PageItem(page));
            DefaultZoomIn = _document.DefaultActions.GetValueOrDefault(ScreenEvent.ZoomIn, "");
            DefaultZoomOut = _document.DefaultActions.GetValueOrDefault(ScreenEvent.ZoomOut, "");
        }
        finally { _loading = false; }
        SelectedPage = Pages.FirstOrDefault(p => p.Page.Id == keep) ?? Pages.FirstOrDefault();
        Status = "Code is valid.";
    }
```

Apply / Revert / Reset / Import / Export:

```csharp
    [RelayCommand]
    private void Apply()
    {
        CheckCode();                                // the code tab is the truth when it was edited
        if (Issues.Count > 0) return;
        var issues = _controller.SaveScreenAxaml(Target, _document.ToAxaml());
        if (issues.Count > 0)
        {
            foreach (var issue in issues) Issues.Add(issue.ToString());
            Status = "Not saved: fix the problems first.";
            return;
        }
        Status = Target == ScreenTarget.Navigation && !ScreenEnabled
            ? "Saved. Turn on \"Show on the bike\" to see it."
            : "Saved and sent to the bike.";
    }

    [RelayCommand]
    private void Revert() { LoadTarget(); Status = "Reverted to the saved screen."; }

    [RelayCommand]
    private void ResetToDefault() { _controller.ResetScreenDocument(Target); LoadTarget(); Status = "Back to the built-in screen."; }

    public string ExportAxaml() => _document.ToAxaml();

    public void ImportAxaml(string text)
    {
        Code = text;
        CheckCode();
        if (Issues.Count == 0) Status = "Imported. Press Apply to use it.";
    }
```

Edge: `Code` edited by hand and then the user goes to Design without pressing Check — in `OnSelectedTabChanged` (Design selected) call `CheckCode()` only if the code differs from `_document.ToAxaml()`.

- [ ] **Step 2: View**

`ScreenEditorView.axaml`: keep the pinned-preview `Grid RowDefinitions="Auto,*"`, the display picker and Show-on-bike toggle, and the card styling. Changes:
- Row 0 canvas: `<c:ScreenCanvas x:Name="Canvas" />` unchanged tag; hint text now "Drag a box to move it, drag a corner square to resize. The bike's zoom buttons change pages if the page says so (Pages tab)."
- Row 1: `<ScrollViewer><StackPanel>` containing a `TabControl` with three `TabItem`s:
  1. **Design**: Elements card (Add text / Add box / Add progress / Add image... / Duplicate / Delete / Forward / Back + the `ListBox` bound to `Items`), Selected element card (fields as today, plus `IsProgressSelected` handling; Fit combo values now Uniform/UniformToFill/Fill via an `ObservableCollection<string>` or keep the `ScreenImageFit` enum in the VM and map), Page background card (`PageBackground`, `PageNightBackground`).
  2. **Pages**: `ListBox ItemsSource=Pages SelectedItem=SelectedPage`; buttons Add / Delete / Up / Down; `PageName` text box; `PageZoomIn`/`PageZoomOut` text boxes labelled "Zoom in button" / "Zoom out button" with the hint "Actions: NextPage, PreviousPage, GoTo:<page id>, Media.PlayPause, Media.Next, Media.Previous, Toggle:<flag>, Set:<name>=<value>, None. Empty = use the screen default."; the same pair for `DefaultZoomIn`/`DefaultZoomOut`.
  3. **Code**: `TextBox AcceptsReturn="True" FontFamily="Consolas,Menlo,monospace" FontSize="12" MinHeight="320" Text="{Binding Code}"`, a Check button, and `ItemsControl ItemsSource=Issues` (red text).
- Below the tabs (always visible): Apply / Revert / Reset / Export... / Import... buttons, `Status`, the Variables card (now also listing the bindable names: `HasMusic IsPlaying IsPaused NoMusic Day Night Song Artist Album Time Date Progress Elapsed Duration PageNumber PageCount Flags[name] Vars[name]` and the CAN ones).

`ScreenEditorView.axaml.cs`: same shape as today. Changes:
- `Canvas.SelectionChanged += element => _vm?.Select(element);`
- `Canvas.BoundsDragged += (element, x, y, w, h) => _vm?.OnBoundsDragged(element, x, y, w, h);`
- `Attach`: set `Canvas.Target`, `Canvas.Elements = vm.PreviewElements`, `Canvas.Preview = vm.Preview`, `Canvas.Selected = vm.SelectedElement`; in `OnViewModelPropertyChanged` map `Target`, `PreviewElements`, `Preview`, `SelectedItem` (→ `SelectedElement`) to the canvas and call `Canvas.Refresh()` on `PreviewElements`/`Preview`.
- File pickers: type `*.axaml` (`MimeTypes = ["application/xml"]`), suggested file name `$"{_vm.Target}.axaml"`, `ExportAxaml()` / `ImportAxaml(text)`.

- [ ] **Step 3: Build**

`dotnet build YammiC/YammiC.Android/YammiC.Android.csproj -c Debug` → `0 Error(s)`. Also `dotnet build YammiC/YammiC.Desktop/YammiC.Desktop.csproj` → `0 Error(s)` (the desktop head must still compile; its `BikeService` wiring, if it creates one, needs `ScreenRenderer = new AvaloniaScreenRenderer()` too — add it where `BikeServiceOptions` is built; if it doesn't build a service, nothing to do).

- [ ] **Step 4: Manual verification on the phone**

1. Open the Screen editor → Navigation: the default clock/music screen appears in the pinned preview; drag the Clock → the page behind doesn't scroll and the numbers update; dragging past the edge stops at the edge.
2. Pit board: only the strip is shown; elements can't leave it.
3. Add text/box/progress/image; set Show when / colours; Apply; the bike shows it.
4. Pages tab: add a second page, set the default Zoom in = `NextPage`, Zoom out = `PreviousPage`, Apply, set Nav mode to UI; the bike's zoom buttons switch pages and stay enabled.
5. Code tab: introduce `<Button />` → Check lists `line N: <Button> isn't an allowed element.`; Apply refuses; the bike keeps the last screen. Fix → Check says valid.
6. Export `.axaml`, edit it in a text editor, Import → Check passes → Apply.
7. Kill the app from recents with the bike connected → the screen keeps updating (the Activity-less path from Task 2).

---

### Task 12: Docs, TODO and cleanup

**Files:**
- Modify: `docs/superpowers/specs/2026-10-19-avalonia-screen-pages-design.md` (deviations list)
- Create: `docs/screens.md` (how pages work; the format, widgets, bindings, actions)

- [ ] **Step 1: Record deviations in the spec** — append a "Deviations made while planning" section: events are attributes on `<y:Page>`/`<y:ScreenDocument>` (not an attached property); the widget set uses `ScreenCanvas/ScreenBox/VarText/ScreenImage/MediaProgress` (stock `Image` and `ProgressBar` were replaced: file access and templates); `AlbumArt` is not in the first version because `YmcMediaMetaData` has no artwork; day/night colours are widget properties (`NightForeground`, `NightBackground`) not duplicated elements.

- [ ] **Step 2: `docs/screens.md`** — one page: the `.axaml` format with the example from Task 3, the allowed elements/attributes (from `ScreenXamlValidator`), the bindable names (the property list from `ScreenState`), the action vocabulary, how Reels still works, and "how to render-test": `dotnet test YccuSdk.Tests/YccuSdk.Tests.csproj`.

- [ ] **Step 3: Final checks**

`dotnet test YccuSdk.Tests/YccuSdk.Tests.csproj` → all pass; `dotnet build YammiC/YammiC.Android/YammiC.Android.csproj -c Debug` and `-c Release` → `0 Error(s)`; `grep -rn "RenderSpike\|GetScreenLayout\|SaveScreenLayout\|ResetScreenLayout" YccuSdk YammiC` → no matches.

Report to the user: files changed (do not commit), Task 2's measured render time, anything that deviated.

---

## Self-review (done while writing)

- **Spec coverage:** pages + events (Tasks 3, 4, 8); Avalonia render + widgets + spike/gates (2); AXAML store + migration (5); editor visual + raw tab + import/export (6, 9–11); VPB strip/low fps/min interval preserved (7: presenter parameters unchanged, strip clamp in 6); Reels untouched (8); loader/trim/whitelist/preview-state review notes (2, 3, 11). Open-source editor evaluation and Reels-as-page stay in the spec's TODO.
- **Type consistency:** `ScreenSnapshot`/`ScreenState`/`ScreenPage`/`ScreenDocument`/`ScreenController`/`PageEditModel`/`EditElement` names and signatures match across tasks; `IBikeController` members are `GetScreenAxaml/SaveScreenAxaml/ResetScreenDocument` everywhere.
- **Known soft spots (called out, not hidden):** Task 2's loader API shape in Avalonia 12.1.4 and off-screen rendering without an Activity are unverified until the spike runs; Task 11 gives the view model's new code in full but ports the existing field-by-field `Sel*` plumbing by the mapping table rather than repeating 150 lines.
