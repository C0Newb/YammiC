# CCU Emulator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a self-contained C# / Avalonia emulator of a Yamaha CCU that a phone-side client (the planned C# app, YammiC, or the official Y-Connect app) can connect to, with music, weather, notification, call, phone-state and Virtual Pit Board emulation plus action buttons.

**Architecture:** One core project (`CCU_Emulator`) holding `Protocol/` (pure wire codecs), `Emulation/` (a `CcuSession` state machine over an `ICcuTransport`), `Transport/` (loopback TCP everywhere, Windows BLE GATT server + RFCOMM client behind `WINDOWS_BLUETOOTH`) and the Avalonia UI. Milestone 1 is everything except the Windows transports and runs against a loopback TCP transport; milestone 2 adds Windows Bluetooth. A test project covers `Protocol/` and `Emulation/`.

**Tech Stack:** .NET 10, Avalonia 12.1, CommunityToolkit.Mvvm 8.4.2, xUnit 2.9.3, WinRT Bluetooth APIs (`net10.0-windows10.0.19041.0`) for milestone 2.

**Spec:** `docs/superpowers/specs/2026-10-10-ccu-emulator-design.md`

## Global Constraints

- **No Yamaha code.** Do not use, copy or paraphrase official Yamaha code. The CCU side is written from `docs/`, the YammiC wire behavior and the captured logs. (`AGENTS.md`.)
- **Self-contained.** No dependency on CNCO.Unify or any external library of the author's. NuGet packages only.
- **Do not port YammiC client classes.** Read them only as a wire-format reference.
- **Bike identity is never committed.** `emulator.settings.json` is gitignored; only `emulator.settings.example.json` with fake values (CCUID `01234567890123`, passkey `012345`, MAC `00:AA:BB:CC:DD:EE`) is tracked. Never copy values from `.local/AGENTS.md`.
- **When `docs/` and YammiC's working code disagree, YammiC wins** (it is verified against hardware). Known disagreements already resolved: DDT body has no length prefix (only `NotificationDataV2` has `[len 3B]`); music duration/elapsed are **seconds**, not ms; the DDT characteristics are control `56851826` / status `75AB2788` / data `E998AEAB`; weather slot wire values are `Current 0, After1Day 1 … After5Days 5, After1Hour 6, After2Hours 7, After3Hours 8, After6Days 9`; `PhoneVolumeLevel` is `[cmd][volume][type]`; `NotificationCategory` is `[cmd][12 bytes]`.
- **All multi-byte integers are big-endian.**
- **Protocol code is pure.** `Protocol/` and `Emulation/` never reference Avalonia or any Bluetooth API. Bluetooth APIs appear only under `Transport/Platforms/Windows/` behind `#if WINDOWS_BLUETOOTH`.
- **Unknown or malformed packets are logged as hex and ignored, never thrown out of the session.**
- **xUnit note:** the tests compare collections with `Assert.Equal([...], list)`. If the compiler reports an ambiguous call (CS0121) for a collection expression as the first argument, wrap it: `Assert.Equal(new[] { ... }, list)`. Do not change what is asserted.
- **Namespaces:** `CCU_Emulator.Protocol`, `.Emulation`, `.Transport`, `.ViewModels`, `.Views`. Tests: `CCU_Emulator.Tests`.
- **Build/test commands** (run from the repo root; PowerShell or Bash):
  - Build: `dotnet build CCU-Emulator/CCU-Emulator.slnx`
  - Test: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
- **Commits are signed with GPG.** If signing fails, stop and tell the user; never pass `--no-gpg-sign`. Commit trailer: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

Throughout this plan `EMU` means the repo-relative folder `CCU-Emulator/CCU-Emulator` (it holds `Directory.Packages.props` and the project folders).

## Review Focus

Failure modes the spec implies that no feature task would otherwise test; each has a pinning test in the task named in brackets.

1. **Truncated or garbled packet of any known command** must never throw anything but `MalformedPacketException` from the decoder, and never escape the session. [Task 3, Task 6]
2. **DDT misbehavior:** a data chunk before any session, a wrong request ID, an offset past the declared size, a second session start mid-transfer, or duplicate chunks must not overrun the buffer or crash. [Task 4]
3. **Corrupt SPP stream:** a frame header claiming a huge length (e.g. `01 FF FF FF FF`), or garbage between frames, must resync without allocating the claimed length. [Task 2]
4. **Strings:** a length byte larger than the bytes remaining, multi-byte UTF-8, and 0-length strings in music, call and notification messages. [Task 3]
5. **Disconnect mid-transfer:** BLE or SPP dropping during a DDT session or a VPB image must reset partial state so the next connection starts clean, and an undecodable VPB JPEG must show an error instead of crashing the dash. [Task 6, Task 9, Task 11]

## File Structure

```
.gitignore                                      modify  (ignore bin/obj/.vs and the real settings file)
AGENTS.md                                       modify  (workspace layout, final task)
EMU/Directory.Packages.props                    modify  (test packages)
CCU-Emulator/CCU-Emulator.slnx                  modify  (drop mobile/browser heads, add tests)
EMU/CCU_Emulator/CCU_Emulator.csproj            modify  (multi-target, WINDOWS_BLUETOOTH)
EMU/CCU_Emulator.Desktop/CCU_Emulator.Desktop.csproj  modify (multi-target)
EMU/CCU_Emulator.Tests/CCU_Emulator.Tests.csproj      create
EMU/CCU_Emulator/Protocol/
    CommandCode.cs            command code enum
    MalformedPacketException.cs
    PacketReader.cs           bounds-checked big-endian reader
    PacketWriter.cs           big-endian writer
    VpbCrc32.cs               nonstandard CRC32 (init 0)
    SppFrame.cs               SPP framing: Encode + SppFrameReader
    Messages.cs               enums + phone->CCU message records
    PhoneMessageDecoder.cs    byte[] -> PhoneMessage
    NotificationProto.cs      hand-rolled protobuf decode for 0x012F
    CcuPackets.cs             CCU->phone packet builders
    DdtReceiver.cs            CCU side of the DDT session
EMU/CCU_Emulator/Transport/
    CcuChannel.cs, ICcuTransport.cs
    LoopbackTcpTransport.cs
    TransportFactory.cs
    Platforms/Windows/        (milestone 2) WindowsBleTransport.cs, WindowsSppClient.cs
EMU/CCU_Emulator/Emulation/
    CcuSettings.cs            settings + CcuBehavior + SettingsStore
    PacketLog.cs
    CcuState.cs               observable emulated-CCU state
    VpbReceiver.cs            chunk reassembly
    CcuSession.cs             core: transport wiring, dispatch, state updates
    CcuSession.Requests.cs    bootstrap requests + Tick
    CcuSession.Buttons.cs     button commands
    EmulatorHost.cs           owns settings, transport, session, tick timer
EMU/CCU_Emulator/ViewModels/  MainViewModel, DashViewModel, ConnectionViewModel, ButtonsViewModel, BehaviorViewModel, NotificationsViewModel, LogViewModel
EMU/CCU_Emulator/Views/       MainView.axaml(+.cs), DashView, ConnectionView, ButtonsView, BehaviorView, NotificationsView, LogView
EMU/CCU_Emulator.Tests/       tests mirroring the folders above, plus Support/ (FakeTransport, PhoneSimulator, Bytes)
EMU/emulator.settings.example.json   create
tools/PhoneSim/PhoneSim.cs           create (file-based app: scripted phone for the loopback transport)
tools/BtsnoopDump/BtsnoopDump.cs     create (file-based app: ATT dump of an Android btsnoop log)
docs/emulator.md                     create
```

---

## Milestone 1 — Protocol, session and UI over a loopback transport

### Task 1: Scaffold the solution

**Files:**
- Modify: `.gitignore`
- Modify: `EMU/Directory.Packages.props`
- Modify: `EMU/CCU_Emulator/CCU_Emulator.csproj`
- Modify: `EMU/CCU_Emulator.Desktop/CCU_Emulator.Desktop.csproj`
- Modify: `CCU-Emulator/CCU-Emulator.slnx`
- Create: `EMU/CCU_Emulator.Tests/CCU_Emulator.Tests.csproj`
- Create: `EMU/CCU_Emulator.Tests/SmokeTests.cs`

**Interfaces:**
- Produces: a building solution with a `net10.0` (and, on Windows, `net10.0-windows10.0.19041.0` defining `WINDOWS_BLUETOOTH`) core project, a Desktop head, and an xUnit project referencing the core.

- [ ] **Step 1: Ignore build output and the real settings file**

Append to `.gitignore`:

```gitignore

# CCU emulator
CCU-Emulator/**/bin/
CCU-Emulator/**/obj/
CCU-Emulator/.vs/
CCU-Emulator/**/emulator.settings.json
```

- [ ] **Step 2: Add the test packages**

In `EMU/Directory.Packages.props`, inside the `<ItemGroup>` after the `CommunityToolkit.Mvvm` line, add:

```xml
        <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
        <PackageVersion Include="xunit" Version="2.9.3" />
        <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.4" />
```

- [ ] **Step 3: Multi-target the core project**

Replace the first `<PropertyGroup>` of `EMU/CCU_Emulator/CCU_Emulator.csproj` with:

```xml
  <PropertyGroup>
    <RootNamespace>CCU_Emulator</RootNamespace>
    <TargetFrameworks>net10.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <!-- The Windows Bluetooth transports need the Windows TFM; it is only added when building on Windows. -->
  <PropertyGroup Condition="$([MSBuild]::IsOSPlatform('Windows'))">
    <TargetFrameworks>net10.0;net10.0-windows10.0.19041.0</TargetFrameworks>
  </PropertyGroup>
  <PropertyGroup Condition="'$(TargetFramework)' == 'net10.0-windows10.0.19041.0'">
    <DefineConstants>$(DefineConstants);WINDOWS_BLUETOOTH</DefineConstants>
  </PropertyGroup>
```

- [ ] **Step 4: Multi-target the Desktop head**

In `EMU/CCU_Emulator.Desktop/CCU_Emulator.Desktop.csproj` replace `<TargetFramework>net10.0</TargetFramework>` with `<TargetFrameworks>net10.0</TargetFrameworks>` and add, directly after that `PropertyGroup`:

```xml
  <PropertyGroup Condition="$([MSBuild]::IsOSPlatform('Windows'))">
    <TargetFrameworks>net10.0;net10.0-windows10.0.19041.0</TargetFrameworks>
  </PropertyGroup>
```

- [ ] **Step 5: Create the test project**

`EMU/CCU_Emulator.Tests/CCU_Emulator.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>CCU_Emulator.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CCU_Emulator\CCU_Emulator.csproj" />
  </ItemGroup>
</Project>
```

`EMU/CCU_Emulator.Tests/SmokeTests.cs`:

```csharp
using Xunit;

namespace CCU_Emulator.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreAssemblyLoads() => Assert.NotNull(typeof(CCU_Emulator.App).Assembly);
}
```

- [ ] **Step 6: Rewrite the solution**

Replace `CCU-Emulator/CCU-Emulator.slnx` with (the Android, iOS and Browser project folders stay on disk, they are just out of the solution):

```xml
<Solution>
  <Project Path="CCU-Emulator/CCU_Emulator.Desktop/CCU_Emulator.Desktop.csproj" />
  <Project Path="CCU-Emulator/CCU_Emulator/CCU_Emulator.csproj" />
  <Project Path="CCU-Emulator/CCU_Emulator.Tests/CCU_Emulator.Tests.csproj" />
</Solution>
```

- [ ] **Step 7: Build and test**

Run: `dotnet build CCU-Emulator/CCU-Emulator.slnx` — Expected: `Build succeeded` for both `net10.0` and `net10.0-windows10.0.19041.0` of the core and Desktop projects.
Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests` — Expected: `Passed! - Failed: 0, Passed: 1`.

- [ ] **Step 8: Commit**

```bash
git add .gitignore CCU-Emulator/CCU-Emulator.slnx CCU-Emulator/CCU-Emulator/Directory.Packages.props CCU-Emulator/CCU-Emulator/CCU_Emulator CCU-Emulator/CCU-Emulator/CCU_Emulator.Desktop CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests
git commit -m "Scaffold emulator solution with multi-target core and test project"
```

---

### Task 2: Protocol primitives (reader, writer, CRC32, SPP framing)

**Files:**
- Create: `EMU/CCU_Emulator/Protocol/MalformedPacketException.cs`, `PacketReader.cs`, `PacketWriter.cs`, `VpbCrc32.cs`, `SppFrame.cs`, `CommandCode.cs`
- Test: `EMU/CCU_Emulator.Tests/Support/Bytes.cs`, `Protocol/PacketReaderWriterTests.cs`, `Protocol/VpbCrc32Tests.cs`, `Protocol/SppFrameTests.cs`

**Interfaces:**
- Produces:
  - `class MalformedPacketException(string message) : Exception`
  - `PacketReader(byte[] data)`: `int Position`, `int Remaining`, `byte U8()`, `int U16()`, `int U24()`, `uint U32()`, `int I32()`, `short I16()`, `long I64()`, `ulong Varint()`, `byte[] Bytes(int n)`, `string Utf8(int n)`; every method throws `MalformedPacketException` instead of reading out of range.
  - `PacketWriter`: `U8(int)`, `U16(int)`, `U24(int)`, `I32(int)`, `U32(uint)`, `Bytes(byte[])`, `Utf8(string)`, `byte[] ToArray()`; each returns `PacketWriter` for chaining.
  - `static uint VpbCrc32.Compute(ReadOnlySpan<byte>)`
  - `static byte[] SppFrame.Encode(byte[] payload)`; `SppFrameReader.Feed(ReadOnlySpan<byte>) : List<byte[]>`
  - `enum CommandCode : ushort` (full list below)

- [ ] **Step 1: Write the test helpers and failing tests**

`EMU/CCU_Emulator.Tests/Support/Bytes.cs`:

```csharp
using System.Text;

namespace CCU_Emulator.Tests.Support;

internal static class Bytes
{
    /// <summary>Hex string (spaces allowed) to bytes.</summary>
    public static byte[] H(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    /// <summary>Length-prefixed (1 byte) UTF-8 string.</summary>
    public static byte[] Str8(string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        return [(byte)b.Length, .. b];
    }

    public static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    public static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}
```

`EMU/CCU_Emulator.Tests/Protocol/PacketReaderWriterTests.cs`:

```csharp
using CCU_Emulator.Protocol;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Protocol;

public class PacketReaderWriterTests
{
    [Fact]
    public void ReaderReadsBigEndianIntegers()
    {
        var r = new PacketReader(H("01 0203 040506 07080910 FFFE"));
        Assert.Equal(1, r.U8());
        Assert.Equal(0x0203, r.U16());
        Assert.Equal(0x040506, r.U24());
        Assert.Equal(0x07080910, r.I32());
        Assert.Equal((short)-2, r.I16());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void ReaderReadsInt64AndVarint()
    {
        var r = new PacketReader(H("00 00 00 00 65 00 00 00  E9 0F"));
        Assert.Equal(0x65000000L, r.I64());
        Assert.Equal(2025UL, r.Varint());
    }

    [Fact]
    public void ReaderThrowsMalformedWhenOutOfBytes()
    {
        var r = new PacketReader(H("01"));
        r.U8();
        Assert.Throws<MalformedPacketException>(() => r.U8());
        Assert.Throws<MalformedPacketException>(() => new PacketReader(H("01")).Bytes(5));
        Assert.Throws<MalformedPacketException>(() => new PacketReader(H("01")).Bytes(-1));
        Assert.Throws<MalformedPacketException>(() => new PacketReader(H("FF FF FF FF FF FF FF FF FF FF FF")).Varint());
    }

    [Fact]
    public void Utf8LengthLargerThanRemainingIsMalformed()
    {
        var r = new PacketReader(Cat(H("0A"), Ascii("abc")));
        Assert.Throws<MalformedPacketException>(() => r.Utf8(r.U8()));
    }

    [Fact]
    public void WriterRoundTrips()
    {
        var bytes = new PacketWriter().U8(1).U16(0x0203).U24(0x040506).I32(0x07080910).Utf8("hi").ToArray();
        Assert.Equal(H("01 0203 040506 07080910 6869"), bytes);
    }
}
```

`EMU/CCU_Emulator.Tests/Protocol/VpbCrc32Tests.cs`:

```csharp
using CCU_Emulator.Protocol;
using Xunit;

namespace CCU_Emulator.Tests.Protocol;

public class VpbCrc32Tests
{
    // Independent bit-by-bit reference of the variant described in docs/vpb.md:
    // IEEE-802.3 polynomial, register initialised to 0 (not 0xFFFFFFFF), final complement.
    private static uint Reference(byte[] data)
    {
        uint crc = 0;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    [Fact]
    public void EmptyInputIsAllOnes() => Assert.Equal(0xFFFFFFFFu, VpbCrc32.Compute([]));

    [Theory]
    [InlineData("00")]
    [InlineData("313233343536373839")]
    [InlineData("FFD8FFE000104A46494600010100000100010000")]
    public void MatchesBitwiseReference(string hex)
    {
        var data = Convert.FromHexString(hex);
        Assert.Equal(Reference(data), VpbCrc32.Compute(data));
    }

    [Fact]
    public void DiffersFromStandardCrc32()
    {
        // Standard CRC32("123456789") is 0xCBF43926; the variant must not produce it.
        Assert.NotEqual(0xCBF43926u, VpbCrc32.Compute("123456789"u8));
    }
}
```

`EMU/CCU_Emulator.Tests/Protocol/SppFrameTests.cs`:

```csharp
using CCU_Emulator.Protocol;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Protocol;

public class SppFrameTests
{
    [Fact]
    public void EncodeWrapsPayload()
        => Assert.Equal(H("01 00000003 AABBCC 04"), SppFrame.Encode(H("AABBCC")));

    [Fact]
    public void ReaderYieldsFramesSplitAcrossFeeds()
    {
        var wire = Cat(SppFrame.Encode(H("0102")), SppFrame.Encode(H("0304050607")));
        var reader = new SppFrameReader();
        var frames = new List<byte[]>();
        foreach (var b in wire) frames.AddRange(reader.Feed([b]));
        Assert.Equal(2, frames.Count);
        Assert.Equal(H("0102"), frames[0]);
        Assert.Equal(H("0304050607"), frames[1]);
    }

    [Fact]
    public void ReaderResyncsPastGarbageAndHugeLength()
    {
        var reader = new SppFrameReader();
        // Garbage, then a header claiming ~4 GiB, then a good frame.
        var wire = Cat(H("7F7F"), H("01 FFFFFFFF"), SppFrame.Encode(H("AA")));
        var frames = reader.Feed(wire);
        Assert.Contains(frames, f => f.SequenceEqual(H("AA")));
    }

    [Fact]
    public void ReaderToleratesMissingTerminator()
    {
        var reader = new SppFrameReader();
        var frames = reader.Feed(H("01 00000002 AABB 01 00000001 CC 04"));
        Assert.Equal(H("AABB"), frames[0]);
    }
}
```

- [ ] **Step 2: Run tests, verify they fail to compile**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: build FAIL — `PacketReader`, `PacketWriter`, `VpbCrc32`, `SppFrame` not found.

- [ ] **Step 3: Implement the primitives**

`EMU/CCU_Emulator/Protocol/MalformedPacketException.cs`:

```csharp
namespace CCU_Emulator.Protocol;

/// <summary>A packet was shorter than its layout requires, or otherwise undecodable.</summary>
public sealed class MalformedPacketException(string message) : Exception(message);
```

`EMU/CCU_Emulator/Protocol/PacketReader.cs`:

```csharp
using System.Text;

namespace CCU_Emulator.Protocol;

/// <summary>Bounds-checked big-endian reader. Never reads past the end; throws <see cref="MalformedPacketException"/>.</summary>
public sealed class PacketReader(byte[] data)
{
    private int _pos;

    public int Position => _pos;
    public int Remaining => data.Length - _pos;

    public byte U8()
    {
        Need(1);
        return data[_pos++];
    }

    public int U16() => (U8() << 8) | U8();

    public int U24() => (U8() << 16) | (U8() << 8) | U8();

    public uint U32() => ((uint)U8() << 24) | ((uint)U8() << 16) | ((uint)U8() << 8) | U8();

    public int I32() => unchecked((int)U32());

    public short I16() => unchecked((short)U16());

    public long I64()
    {
        long v = 0;
        for (var i = 0; i < 8; i++) v = (v << 8) | U8();
        return v;
    }

    /// <summary>Protobuf base-128 varint.</summary>
    public ulong Varint()
    {
        ulong result = 0;
        var shift = 0;
        while (true)
        {
            var b = U8();
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
            if (shift > 63) throw new MalformedPacketException("varint too long");
        }
    }

    public byte[] Bytes(int n)
    {
        if (n < 0) throw new MalformedPacketException($"negative length {n}");
        Need(n);
        var result = data.AsSpan(_pos, n).ToArray();
        _pos += n;
        return result;
    }

    public string Utf8(int n) => Encoding.UTF8.GetString(Bytes(n));

    private void Need(int n)
    {
        if (n > Remaining)
            throw new MalformedPacketException($"need {n} byte(s) at offset {_pos}, only {Remaining} left");
    }
}
```

`EMU/CCU_Emulator/Protocol/PacketWriter.cs`:

```csharp
using System.Text;

namespace CCU_Emulator.Protocol;

/// <summary>Big-endian packet builder.</summary>
public sealed class PacketWriter
{
    private readonly List<byte> _buf = [];

    public PacketWriter U8(int v) { _buf.Add((byte)v); return this; }
    public PacketWriter U16(int v) { _buf.Add((byte)(v >> 8)); _buf.Add((byte)v); return this; }
    public PacketWriter U24(int v) { _buf.Add((byte)(v >> 16)); _buf.Add((byte)(v >> 8)); _buf.Add((byte)v); return this; }
    public PacketWriter I32(int v) => U32(unchecked((uint)v));
    public PacketWriter U32(uint v)
    {
        _buf.Add((byte)(v >> 24)); _buf.Add((byte)(v >> 16)); _buf.Add((byte)(v >> 8)); _buf.Add((byte)v);
        return this;
    }
    public PacketWriter Bytes(byte[] b) { _buf.AddRange(b); return this; }
    public PacketWriter Utf8(string s) => Bytes(Encoding.UTF8.GetBytes(s));
    public byte[] ToArray() => _buf.ToArray();
}
```

`EMU/CCU_Emulator/Protocol/VpbCrc32.cs`:

```csharp
namespace CCU_Emulator.Protocol;

/// <summary>
/// The CRC32 variant the CCU expects on VPB graphic chunks (docs/vpb.md): the IEEE-802.3
/// polynomial table, but with the register initialised to 0 and a final bitwise complement.
/// </summary>
public static class VpbCrc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0;
        foreach (var b in data) crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        return ~crc;
    }
}
```

`EMU/CCU_Emulator/Protocol/SppFrame.cs`:

```csharp
namespace CCU_Emulator.Protocol;

/// <summary>SPP framing: <c>[0x01][length 4B BE][payload][0x04]</c>.</summary>
public static class SppFrame
{
    public static byte[] Encode(byte[] payload)
        => new PacketWriter().U8(0x01).I32(payload.Length).Bytes(payload).U8(0x04).ToArray();
}

/// <summary>Incremental SPP frame parser. Resyncs on garbage and implausible lengths.</summary>
public sealed class SppFrameReader
{
    /// <summary>Largest payload accepted; anything bigger is treated as a corrupt header.</summary>
    public const int MaxPayload = 1_000_000;

    private readonly List<byte> _buf = [];

    public List<byte[]> Feed(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes) _buf.Add(b);
        var frames = new List<byte[]>();
        while (true)
        {
            var start = _buf.IndexOf(0x01);
            if (start < 0) { _buf.Clear(); break; }
            if (start > 0) _buf.RemoveRange(0, start);
            if (_buf.Count < 5) break;

            var len = ((long)_buf[1] << 24) | ((long)_buf[2] << 16) | ((long)_buf[3] << 8) | _buf[4];
            if (len > MaxPayload) { _buf.RemoveAt(0); continue; } // corrupt header: skip this start byte

            var total = 5 + (int)len;
            if (_buf.Count < total + 1) break; // need payload and a look at the terminator byte

            frames.Add(_buf.GetRange(5, (int)len).ToArray());
            var terminated = _buf[total] == 0x04;
            _buf.RemoveRange(0, total + (terminated ? 1 : 0));
        }
        return frames;
    }
}
```

`EMU/CCU_Emulator/Protocol/CommandCode.cs`:

```csharp
namespace CCU_Emulator.Protocol;

/// <summary>Command codes seen on the wire (names follow YammiC; see docs/commands.md).</summary>
public enum CommandCode : ushort
{
    LibVersion = 0x0001,
    IdentifyApp = 0x0002,
    WeatherRequest = 0x0003,
    WeatherLocation = 0x0004,
    WeatherInfo = 0x0005,
    VehicleSettingRequest = 0x0009, // CCU asks for the time
    DatetimeResponse = 0x000A,
    LanguageRequest = 0x000B,
    LanguageResponse = 0x000C,
    AddNotification = 0x0010,
    UpdateNotification = 0x0011,
    RemoveNotification = 0x0012,
    NotificationRemoved = 0x0013,
    AuthenticationRequest = 0x0101,
    AuthenticationResponse = 0x0102,
    PhoneBattery = 0x0115,
    MusicMetaData = 0x0116,
    MusicControl = 0x0117,
    IncomingCallInformation = 0x0118,
    IncomingCallControl = 0x0119,
    PhoneThermalState = 0x011A,
    HeadsetStateRequest = 0x011B,
    HeadsetStateResponse = 0x011C,
    VolumeControl = 0x011D,
    VolumeLevel = 0x011E,
    NotificationCategory = 0x0128,
    VolumeControllableRequest = 0x0129,
    VolumeControllableResponse = 0x012A,
    NotificationDataV2 = 0x012F,
    CellSignalRequest = 0x0132,
    CellSignalResponse = 0x0133,
    CallChange = 0x0134,
    PingRequest = 0x0135,
    PongResponse = 0x0136,
    BleConnectInformation = 0x013A,
    PhoneStateCombined = 0x013C,
    VpbRequest = 0x0231,
    VpbResponseNg = 0x0232,
    VpbGraphicRequest = 0x0233,
    VpbGraphicResponseNg = 0x0234,
    VpbSendGraphic = 0x0235,
    VpbSendGraphicResponse = 0x0236,
    VpbClear = 0x0237,
    VpbClearOk = 0x0238,
    VpbClearNg = 0x0239,
}
```

- [ ] **Step 4: Run tests, verify they pass**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: all PASS. (If `ReaderResyncsPastGarbageAndHugeLength` fails, the resync loop is skipping the good frame — re-check that `RemoveAt(0)` then `IndexOf(0x01)` is re-evaluated each iteration.)

- [ ] **Step 5: Commit**

```bash
git add CCU-Emulator/CCU-Emulator/CCU_Emulator/Protocol CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests
git commit -m "Add protocol primitives: reader, writer, VPB CRC32, SPP framing"
```

---

### Task 3: Phone→CCU message models and decoder

**Files:**
- Create: `EMU/CCU_Emulator/Protocol/Messages.cs`, `PhoneMessageDecoder.cs`, `NotificationProto.cs`
- Test: `EMU/CCU_Emulator.Tests/Protocol/PhoneMessageDecoderTests.cs`

**Interfaces:**
- Consumes: `PacketReader`, `PacketWriter`, `VpbCrc32`, `CommandCode` (Task 2).
- Produces:
  - Enums `PlaybackState`, `WeatherSlot`, `WeatherType`, `CellSignal`, `ThermalState`, `VolumeType` (all `: byte`).
  - `abstract record PhoneMessage` and sealed subtypes: `IdentifyAppMessage(byte RequestId, int AppId)`, `AuthenticationMessage(string CcuId, string PassKey)`, `LanguageResponseMessage(byte RequestId, byte Language)`, `WeatherLocationMessage(byte RequestId, string Name)`, `WeatherInfoMessage(byte RequestId, WeatherSlot Slot, WeatherType Type, double Temperature, double MaxTemperature, double MinTemperature, int PrecipitationPercent)`, `DeviceTimeMessage(byte RequestId, DateTime Time)`, `MusicMetaDataMessage(string Artist, string Album, string Song, int DurationSeconds, PlaybackState State, double Rate, int ElapsedSeconds)`, `NotificationMessage(int Id, int Category, DateTime? Timestamp, string Title, string SubTitle, string Detail)`, `NotificationLifecycleMessage(CommandCode Operation, byte RequestId, int Id)`, `NotificationCategoryMessage(bool[] Enabled)`, `IncomingCallInfoMessage(string Name, string Number, bool Missed)`, `CallChangeMessage(bool Started)`, `PhoneStateMessage(int Battery, bool Charging, int PhoneVolume, int MediaVolume, CellSignal Signal, ThermalState Thermal, bool HeadsetConnected, bool OnHook)`, `ThermalStateMessage(ThermalState State)`, `CellSignalResponseMessage(CellSignal Signal)`, `HeadsetStateResponseMessage(bool Connected)`, `VolumeControllableResponseMessage(bool CanControl)`, `VolumeLevelMessage(VolumeType Type, int Percent)`, `PongMessage(byte RequestId)`, `VpbRequestMessage`, `VpbClearMessage`, `VpbGraphicResponseNgMessage`, `VpbChunkMessage(int ContentId, byte Index, byte TotalIndex, int TotalSize, uint Crc, byte[] Data)` with `bool CrcOk`, `UnknownMessage(int Command, byte[] Data)`.
  - `static PhoneMessage PhoneMessageDecoder.Decode(byte[] data)`; throws only `MalformedPacketException`.

- [ ] **Step 1: Write the failing tests**

`EMU/CCU_Emulator.Tests/Protocol/PhoneMessageDecoderTests.cs`:

```csharp
using CCU_Emulator.Protocol;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Protocol;

public class PhoneMessageDecoderTests
{
    private static T Decode<T>(byte[] data) where T : PhoneMessage => Assert.IsType<T>(PhoneMessageDecoder.Decode(data));

    [Fact]
    public void Auth()
    {
        var m = Decode<AuthenticationMessage>(Cat(H("0101"), Ascii("01234567890123"), Ascii("012345")));
        Assert.Equal("01234567890123", m.CcuId);
        Assert.Equal("012345", m.PassKey);
    }

    [Fact]
    public void Identify()
    {
        var m = Decode<IdentifyAppMessage>(H("0002 01 00001234"));
        Assert.Equal(1, m.RequestId);
        Assert.Equal(0x1234, m.AppId);
    }

    [Fact]
    public void Music_UsesSecondsAndLengthPrefixedStrings()
    {
        var m = Decode<MusicMetaDataMessage>(Cat(H("0116"), Str8("Test Artist"), Str8("Test Album"), Str8("Test Song"), H("0000D2 02 000A 00000F")));
        Assert.Equal("Test Artist", m.Artist);
        Assert.Equal("Test Album", m.Album);
        Assert.Equal("Test Song", m.Song);
        Assert.Equal(210, m.DurationSeconds);
        Assert.Equal(PlaybackState.Playing, m.State);
        Assert.Equal(1.0, m.Rate);
        Assert.Equal(15, m.ElapsedSeconds);
    }

    [Fact]
    public void Music_MultibyteAndEmptyStrings()
    {
        var m = Decode<MusicMetaDataMessage>(Cat(H("0116"), Str8("Björk – Jóga"), Str8(""), Str8("日本語"), H("000000 01 000A 000000")));
        Assert.Equal("Björk – Jóga", m.Artist);
        Assert.Equal("", m.Album);
        Assert.Equal("日本語", m.Song);
        Assert.Equal(PlaybackState.Paused, m.State);
    }

    [Fact]
    public void Music_StringLengthBeyondPacketIsMalformed()
        => Assert.Throws<MalformedPacketException>(() => PhoneMessageDecoder.Decode(Cat(H("0116 40"), Ascii("abc"))));

    [Fact]
    public void WeatherInfo()
    {
        var m = Decode<WeatherInfoMessage>(H("0005 07 00 00 00FA 0118 00C8 02"));
        Assert.Equal(WeatherSlot.Current, m.Slot);
        Assert.Equal(WeatherType.Sunny, m.Type);
        Assert.Equal(25.0, m.Temperature);
        Assert.Equal(28.0, m.MaxTemperature);
        Assert.Equal(20.0, m.MinTemperature);
        Assert.Equal(10, m.PrecipitationPercent); // wire carries percent / 5
    }

    [Fact]
    public void WeatherInfo_NegativeTemperature()
        => Assert.Equal(-5.5, Decode<WeatherInfoMessage>(H("0005 01 06 14 FFC9 0000 FF9C 00")).Temperature);

    [Fact]
    public void WeatherLocation()
        => Assert.Equal("Berlin", Decode<WeatherLocationMessage>(Cat(H("0004 01"), Ascii("Berlin"))).Name);

    [Fact]
    public void DeviceTime_SecondsSinceEpoch()
    {
        var secs = new DateTimeOffset(2026, 10, 10, 12, 34, 56, TimeSpan.Zero).ToUnixTimeSeconds();
        var m = Decode<DeviceTimeMessage>(new PacketWriter().U16(0x000A).U8(3).I32(0).I32((int)secs).ToArray());
        Assert.Equal(new DateTime(2026, 10, 10, 12, 34, 56), m.Time);
    }

    [Fact]
    public void DeviceTime_OutOfRangeIsMalformed()
        => Assert.Throws<MalformedPacketException>(() => PhoneMessageDecoder.Decode(H("000A 03 7FFFFFFFFFFFFFFF")));

    [Fact]
    public void NotificationV2()
    {
        var proto = Cat(H("18 01 20 00 28 E9 0F 30 06 38 1D 40 00 48 00 50 00 5A 04"), Ascii("Test"), H("6A 09"), Ascii("It works!"));
        var packet = Cat(new PacketWriter().U16(0x012F).U24(proto.Length).ToArray(), proto);
        var m = Decode<NotificationMessage>(packet);
        Assert.Equal(1, m.Id);
        Assert.Equal(0, m.Category);
        Assert.Equal(new DateTime(2025, 6, 29, 0, 0, 0), m.Timestamp);
        Assert.Equal("Test", m.Title);
        Assert.Equal("", m.SubTitle);
        Assert.Equal("It works!", m.Detail);
    }

    [Fact]
    public void NotificationV2_UnsupportedWireTypeIsMalformed()
        => Assert.Throws<MalformedPacketException>(() => PhoneMessageDecoder.Decode(H("012F 000002 1D 00")));

    [Theory]
    [InlineData("0010", CommandCode.AddNotification)]
    [InlineData("0011", CommandCode.UpdateNotification)]
    [InlineData("0012", CommandCode.RemoveNotification)]
    public void NotificationLifecycle(string cmd, CommandCode op)
    {
        var m = Decode<NotificationLifecycleMessage>(H(cmd + " 09 00000007"));
        Assert.Equal(op, m.Operation);
        Assert.Equal(7, m.Id);
    }

    [Fact]
    public void NotificationCategory()
    {
        var m = Decode<NotificationCategoryMessage>(H("0128 00 01 00 00 01 00 00 00 00 00 00 01"));
        Assert.Equal(12, m.Enabled.Length);
        Assert.True(m.Enabled[1]);
        Assert.True(m.Enabled[4]);
        Assert.True(m.Enabled[11]);
        Assert.False(m.Enabled[0]);
    }

    [Fact]
    public void IncomingCallInfo()
    {
        var m = Decode<IncomingCallInfoMessage>(Cat(H("0118"), Str8("Alice"), Str8("+15551234"), H("00")));
        Assert.Equal("Alice", m.Name);
        Assert.Equal("+15551234", m.Number);
        Assert.False(m.Missed);
        Assert.True(Decode<IncomingCallInfoMessage>(Cat(H("0118"), Str8(""), Str8(""), H("01"))).Missed);
    }

    [Theory]
    [InlineData("0134 01", true)]
    [InlineData("0134 02", false)]
    public void CallChange(string hex, bool started) => Assert.Equal(started, Decode<CallChangeMessage>(H(hex)).Started);

    [Fact]
    public void PhoneStateCombined()
    {
        var m = Decode<PhoneStateMessage>(H("013C E4 B2 C8 47"));
        Assert.Equal(100, m.Battery);
        Assert.True(m.Charging);
        Assert.Equal(50, m.PhoneVolume);
        Assert.Equal(72, m.MediaVolume);
        Assert.Equal(CellSignal.Lv3, m.Signal);
        Assert.Equal(ThermalState.Middle, m.Thermal);
        Assert.True(m.HeadsetConnected);
        Assert.True(m.OnHook);
    }

    [Fact]
    public void SmallStateMessages()
    {
        Assert.Equal(ThermalState.Middle, Decode<ThermalStateMessage>(H("011A 01")).State);
        Assert.Equal(ThermalState.High, Decode<ThermalStateMessage>(H("011A 09")).State);
        Assert.Equal(CellSignal.Lv2, Decode<CellSignalResponseMessage>(H("0133 03")).Signal);
        Assert.True(Decode<HeadsetStateResponseMessage>(H("011C 01")).Connected);
        Assert.True(Decode<VolumeControllableResponseMessage>(H("012A 01")).CanControl);
        var v = Decode<VolumeLevelMessage>(H("011E 32 01"));
        Assert.Equal(50, v.Percent);
        Assert.Equal(VolumeType.Media, v.Type);
        Assert.Equal(5, Decode<PongMessage>(H("0136 05")).RequestId);
    }

    [Fact]
    public void VpbMessages()
    {
        Assert.IsType<VpbRequestMessage>(PhoneMessageDecoder.Decode(H("0231")));
        Assert.IsType<VpbClearMessage>(PhoneMessageDecoder.Decode(H("0237")));
        Assert.IsType<VpbGraphicResponseNgMessage>(PhoneMessageDecoder.Decode(H("0234")));
    }

    [Fact]
    public void VpbChunk_ChecksCrc()
    {
        var data = H("FFD8FFE0");
        var good = ChunkPacket(data, VpbCrc32.Compute(data));
        var bad = ChunkPacket(data, 0x12345678);
        var m = Decode<VpbChunkMessage>(good);
        Assert.Equal(20101, m.ContentId);
        Assert.Equal(0, m.Index);
        Assert.Equal(2, m.TotalIndex);
        Assert.Equal(13000, m.TotalSize);
        Assert.True(m.CrcOk);
        Assert.False(Decode<VpbChunkMessage>(bad).CrcOk);
    }

    private static byte[] ChunkPacket(byte[] data, uint crc)
        => new PacketWriter().U16(0x0235).I32(20101).U8(0).I32(data.Length).U8(2).I32(13000).U32(crc).Bytes(data).ToArray();

    [Fact]
    public void UnknownCommandIsReportedNotThrown()
    {
        var m = Decode<UnknownMessage>(H("7777 AABB"));
        Assert.Equal(0x7777, m.Command);
    }

    [Fact]
    public void TooShortForACommandCodeIsMalformed()
    {
        Assert.Throws<MalformedPacketException>(() => PhoneMessageDecoder.Decode([]));
        Assert.Throws<MalformedPacketException>(() => PhoneMessageDecoder.Decode(H("01")));
    }

    // Review Focus 1: no truncation of any known command may throw anything but MalformedPacketException.
    [Fact]
    public void EveryTruncationOfEveryKnownPacketFailsCleanly()
    {
        var data = H("FFD8FFE0");
        var proto = Cat(H("18 01 20 00 5A 04"), Ascii("Test"));
        var samples = new[]
        {
            Cat(H("0101"), Ascii("01234567890123"), Ascii("012345")),
            H("0002 01 00001234"), H("000C 05 00"), Cat(H("0004 01"), Ascii("Berlin")),
            H("0005 07 00 00 00FA 0118 00C8 02"), H("000A 03 0000000069E8B5F0"),
            Cat(H("0116"), Str8("A"), Str8("B"), Str8("C"), H("0000D2 02 000A 00000F")),
            Cat(new PacketWriter().U16(0x012F).U24(proto.Length).ToArray(), proto),
            H("0010 09 00000007"), H("0128 00 01 00 00 01 00 00 00 00 00 00 01"),
            Cat(H("0118"), Str8("Alice"), Str8("555"), H("00")), H("0134 01"), H("013C E4 B2 C8 47"),
            H("011A 01"), H("0133 03"), H("011C 01"), H("012A 01"), H("011E 32 01"), H("0136 05"),
            ChunkPacket(data, VpbCrc32.Compute(data)),
        };
        foreach (var sample in samples)
        {
            for (var len = 0; len < sample.Length; len++)
            {
                var prefix = sample[..len];
                try { PhoneMessageDecoder.Decode(prefix); }
                catch (MalformedPacketException) { }
            }
            PhoneMessageDecoder.Decode(sample); // the full sample must decode
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: build FAIL — message types and `PhoneMessageDecoder` not found.

- [ ] **Step 3: Implement the message types**

`EMU/CCU_Emulator/Protocol/Messages.cs`:

```csharp
using System.Text;

namespace CCU_Emulator.Protocol;

public enum PlaybackState : byte { Unknown = 0, Paused = 1, Playing = 2, Rewinding = 3, FastForwarding = 4, Unavailable = 0xFF }

/// <summary>Wire values as observed with YammiC (they differ from the ordering in docs/mvp-music-and-weather.md).</summary>
public enum WeatherSlot : byte
{
    Current = 0, After1Day = 1, After2Days = 2, After3Days = 3, After4Days = 4, After5Days = 5,
    After1Hour = 6, After2Hours = 7, After3Hours = 8, After6Days = 9,
}

public enum WeatherType : byte
{
    Sunny = 0, PartlyCloudyDay, CloudyDay, Rain, Snow, Wind, RainStormMix, RainSnowMix, Fog, Smog, Hail,
    ScatteredShowersDay, ScatteredStormDay, NoData, RainLight, RainHeavy, SnowLight, SnowHeavy,
    RainSnowMixLight, RainSnowMixHeavy, Cloudy, Dust, Tornado, Clear, PartlyCloudyNight, CloudyNight,
    ScatteredShowersNight, ScatteredStormNight,
}

public enum CellSignal : byte { NoConnection = 0, NoSignal = 1, Lv1 = 2, Lv2 = 3, Lv3 = 4, Lv4 = 5 }
public enum ThermalState : byte { Low = 0, Middle = 1, High = 2 }
public enum VolumeType : byte { Phone = 0, Media = 1 }

public abstract record PhoneMessage;

public sealed record IdentifyAppMessage(byte RequestId, int AppId) : PhoneMessage;
public sealed record AuthenticationMessage(string CcuId, string PassKey) : PhoneMessage;
public sealed record LanguageResponseMessage(byte RequestId, byte Language) : PhoneMessage;
public sealed record WeatherLocationMessage(byte RequestId, string Name) : PhoneMessage;

public sealed record WeatherInfoMessage(
    byte RequestId, WeatherSlot Slot, WeatherType Type,
    double Temperature, double MaxTemperature, double MinTemperature, int PrecipitationPercent) : PhoneMessage;

/// <summary>Phone's local wall-clock time (the wire value already includes the timezone offset).</summary>
public sealed record DeviceTimeMessage(byte RequestId, DateTime Time) : PhoneMessage;

public sealed record MusicMetaDataMessage(
    string Artist, string Album, string Song, int DurationSeconds, PlaybackState State, double Rate, int ElapsedSeconds) : PhoneMessage;

public sealed record NotificationMessage(
    int Id, int Category, DateTime? Timestamp, string Title, string SubTitle, string Detail) : PhoneMessage;

public sealed record NotificationLifecycleMessage(CommandCode Operation, byte RequestId, int Id) : PhoneMessage;
public sealed record NotificationCategoryMessage(bool[] Enabled) : PhoneMessage;
public sealed record IncomingCallInfoMessage(string Name, string Number, bool Missed) : PhoneMessage;
public sealed record CallChangeMessage(bool Started) : PhoneMessage;

public sealed record PhoneStateMessage(
    int Battery, bool Charging, int PhoneVolume, int MediaVolume,
    CellSignal Signal, ThermalState Thermal, bool HeadsetConnected, bool OnHook) : PhoneMessage;

public sealed record ThermalStateMessage(ThermalState State) : PhoneMessage;
public sealed record CellSignalResponseMessage(CellSignal Signal) : PhoneMessage;
public sealed record HeadsetStateResponseMessage(bool Connected) : PhoneMessage;
public sealed record VolumeControllableResponseMessage(bool CanControl) : PhoneMessage;
public sealed record VolumeLevelMessage(VolumeType Type, int Percent) : PhoneMessage;
public sealed record PongMessage(byte RequestId) : PhoneMessage;

public sealed record VpbRequestMessage : PhoneMessage;
public sealed record VpbClearMessage : PhoneMessage;
public sealed record VpbGraphicResponseNgMessage : PhoneMessage;

public sealed record VpbChunkMessage(int ContentId, byte Index, byte TotalIndex, int TotalSize, uint Crc, byte[] Data) : PhoneMessage
{
    public bool CrcOk => VpbCrc32.Compute(Data) == Crc;

    protected override bool PrintMembers(StringBuilder b)
    {
        b.Append($"ContentId = {ContentId}, Index = {Index}/{TotalIndex}, TotalSize = {TotalSize}, DataLength = {Data.Length}, CrcOk = {CrcOk}");
        return true;
    }
}

public sealed record UnknownMessage(int Command, byte[] Data) : PhoneMessage
{
    protected override bool PrintMembers(StringBuilder b)
    {
        b.Append($"Command = 0x{Command:X4}, Length = {Data.Length}");
        return true;
    }
}
```

- [ ] **Step 4: Implement the decoder and protobuf reader**

`EMU/CCU_Emulator/Protocol/NotificationProto.cs`:

```csharp
namespace CCU_Emulator.Protocol;

/// <summary>Decodes the hand-encoded protobuf inside NotificationDataV2 (docs/notifications.md).</summary>
internal static class NotificationProto
{
    public static NotificationMessage Decode(byte[] proto)
    {
        var r = new PacketReader(proto);
        int id = 0, category = 0, year = 0, month = 0, day = 0, hour = 0, minute = 0, second = 0;
        string title = "", subTitle = "", detail = "";

        while (r.Remaining > 0)
        {
            var tag = (int)r.Varint();
            var field = tag >> 3;
            var wire = tag & 7;
            switch (wire)
            {
                case 0:
                {
                    var v = (int)r.Varint();
                    switch (field)
                    {
                        case 3: id = v; break;
                        case 4: category = v; break;
                        case 5: year = v; break;
                        case 6: month = v; break;
                        case 7: day = v; break;
                        case 8: hour = v; break;
                        case 9: minute = v; break;
                        case 10: second = v; break;
                    }
                    break;
                }
                case 2:
                {
                    var text = r.Utf8((int)r.Varint());
                    switch (field)
                    {
                        case 11: title = text; break;
                        case 12: subTitle = text; break;
                        case 13: detail = text; break;
                    }
                    break;
                }
                default:
                    throw new MalformedPacketException($"unsupported protobuf wire type {wire}");
            }
        }

        DateTime? timestamp = null;
        try { if (year > 0) timestamp = new DateTime(year, month, day, hour, minute, second); }
        catch (ArgumentOutOfRangeException) { /* leave the timestamp unset */ }

        return new NotificationMessage(id, category, timestamp, title, subTitle, detail);
    }
}
```

`EMU/CCU_Emulator/Protocol/PhoneMessageDecoder.cs`:

```csharp
namespace CCU_Emulator.Protocol;

/// <summary>Decodes a complete phone→CCU message (<c>[cmd 2B][payload]</c>). Throws only <see cref="MalformedPacketException"/>.</summary>
public static class PhoneMessageDecoder
{
    // Seconds-since-epoch range DateTimeOffset accepts.
    private const long MinUnixSeconds = -62135596800;
    private const long MaxUnixSeconds = 253402300799;

    private static string Str8(PacketReader r) => r.Utf8(r.U8());

    public static PhoneMessage Decode(byte[] data)
    {
        var r = new PacketReader(data);
        var cmd = r.U16();
        switch ((CommandCode)cmd)
        {
            case CommandCode.IdentifyApp:
                return new IdentifyAppMessage(r.U8(), r.I32());

            case CommandCode.AuthenticationRequest:
                return new AuthenticationMessage(r.Utf8(14), r.Utf8(6));

            case CommandCode.LanguageResponse:
                return new LanguageResponseMessage(r.U8(), r.U8());

            case CommandCode.WeatherLocation:
            {
                var id = r.U8();
                return new WeatherLocationMessage(id, r.Utf8(r.Remaining).TrimEnd('\0'));
            }

            case CommandCode.WeatherInfo:
            {
                var id = r.U8();
                var slot = (WeatherSlot)r.U8();
                var type = (WeatherType)r.U8();
                var temp = r.I16() / 10.0;
                var max = r.I16() / 10.0;
                var min = r.I16() / 10.0;
                var precipitation = r.U8() * 5;
                return new WeatherInfoMessage(id, slot, type, temp, max, min, precipitation);
            }

            case CommandCode.DatetimeResponse:
            {
                var id = r.U8();
                var seconds = r.I64();
                if (seconds is < MinUnixSeconds or > MaxUnixSeconds)
                    throw new MalformedPacketException($"device time out of range: {seconds}");
                return new DeviceTimeMessage(id, DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime);
            }

            case CommandCode.MusicMetaData:
            {
                var artist = Str8(r);
                var album = Str8(r);
                var song = Str8(r);
                var duration = r.U24();
                var state = (PlaybackState)r.U8();
                var rate = r.U16() / 10.0;
                var elapsed = r.U24();
                return new MusicMetaDataMessage(artist, album, song, duration, state, rate, elapsed);
            }

            case CommandCode.NotificationDataV2:
                return NotificationProto.Decode(r.Bytes(r.U24()));

            case CommandCode.AddNotification:
            case CommandCode.UpdateNotification:
            case CommandCode.RemoveNotification:
                return new NotificationLifecycleMessage((CommandCode)cmd, r.U8(), r.I32());

            case CommandCode.NotificationCategory:
            {
                var flags = new bool[12];
                for (var i = 0; i < flags.Length && r.Remaining > 0; i++) flags[i] = r.U8() != 0;
                return new NotificationCategoryMessage(flags);
            }

            case CommandCode.IncomingCallInformation:
            {
                var name = Str8(r);
                var number = Str8(r);
                var missed = r.Remaining > 0 && r.U8() != 0;
                return new IncomingCallInfoMessage(name, number, missed);
            }

            case CommandCode.CallChange:
                return new CallChangeMessage(r.U8() == 1);

            case CommandCode.PhoneStateCombined:
            {
                var b0 = r.U8();
                var b1 = r.U8();
                var b2 = r.U8();
                var b3 = r.U8();
                return new PhoneStateMessage(
                    Battery: b0 & 0x7F, Charging: (b0 & 0x80) != 0,
                    PhoneVolume: b1 & 0x7F, MediaVolume: b2 & 0x7F,
                    Signal: (CellSignal)((b3 >> 4) & 0x0F), Thermal: (ThermalState)((b3 >> 2) & 0x03),
                    HeadsetConnected: (b3 & 0x02) != 0, OnHook: (b3 & 0x01) != 0);
            }

            case CommandCode.PhoneThermalState:
                return new ThermalStateMessage((ThermalState)Math.Min(r.U8(), (byte)2));

            case CommandCode.CellSignalResponse:
                return new CellSignalResponseMessage((CellSignal)r.U8());

            case CommandCode.HeadsetStateResponse:
                return new HeadsetStateResponseMessage(r.U8() != 0);

            case CommandCode.VolumeControllableResponse:
                return new VolumeControllableResponseMessage(r.U8() != 0);

            case CommandCode.VolumeLevel:
            {
                var percent = r.U8(); // YammiC order: [cmd][volume][type]
                var type = (VolumeType)r.U8();
                return new VolumeLevelMessage(type, percent);
            }

            case CommandCode.PongResponse:
                return new PongMessage(r.Remaining > 0 ? r.U8() : (byte)0);

            case CommandCode.VpbRequest:
                return new VpbRequestMessage();

            case CommandCode.VpbClear:
                return new VpbClearMessage();

            case CommandCode.VpbGraphicResponseNg:
                return new VpbGraphicResponseNgMessage();

            case CommandCode.VpbSendGraphic:
            {
                var contentId = r.I32();
                var index = r.U8();
                var chunkSize = r.I32();
                var totalIndex = r.U8();
                var totalSize = r.I32();
                var crc = r.U32();
                return new VpbChunkMessage(contentId, index, totalIndex, totalSize, crc, r.Bytes(chunkSize));
            }

            default:
                return new UnknownMessage(cmd, data);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: all PASS. If `WeatherInfo_NegativeTemperature` fails: `0xFFC9` as `I16` is -55 → -5.5; check `I16()` uses `unchecked((short)U16())`.

- [ ] **Step 6: Commit**

```bash
git add CCU-Emulator/CCU-Emulator/CCU_Emulator/Protocol CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests
git commit -m "Add phone-to-CCU message models and decoder"
```

---

### Task 4: CCU→phone packet builders and DDT receiver

**Files:**
- Create: `EMU/CCU_Emulator/Protocol/CcuPackets.cs`, `EMU/CCU_Emulator/Protocol/DdtReceiver.cs`
- Test: `EMU/CCU_Emulator.Tests/Protocol/CcuPacketsTests.cs`, `EMU/CCU_Emulator.Tests/Protocol/DdtReceiverTests.cs`

**Interfaces:**
- Consumes: `PacketWriter`, `CommandCode` (Task 2).
- Produces:
  - `enum MusicAction : byte { Next = 1, Previous = 2, Play = 3, Pause = 4 }`
  - `enum VolumeAction : byte { PhoneUp = 1, PhoneDown = 2, MuteMicrophone = 3, MuteSpeaker = 4, MediaUp = 5, MediaDown = 6 }`
  - `enum CallAction : byte { Reject = 0, Answer = 1, HangUp = 2 }`
  - `enum ChunkResponse : byte { Ok = 1, Retry = 2, Abort = 3 }`
  - `static class CcuPackets`: `AuthenticationResponse(bool)`, `LibVersion()`, `LanguageRequest()`, `WeatherRequestCurrent()`, `WeatherRequestPoint(int lat, int lng)`, `DeviceTimeRequest()`, `HeadsetStateRequest()`, `VolumeControllableRequest()`, `CellSignalRequest()`, `Ping(byte seq)`, `MusicControl(MusicAction)`, `VolumeControl(VolumeAction)`, `CallControl(CallAction)`, `NotificationRemoved(int id)`, `VpbGraphicRequest()`, `VpbResponseNg()`, `VpbChunkAck(int contentId, byte index, ChunkResponse)`, `VpbClearOk()`, `VpbClearNg()`, `BleConnectInformation(string ccuId, string passKey)` — all return `byte[]`.
  - `class DdtReceiver`: events `StatusNotify(byte[])`, `MessageComplete(byte[])`, `Diagnostic(string)`; `OnControl(byte[])`, `OnData(byte[])`, `Reset()`, `bool Active`, `byte[] StatusValue`.

- [ ] **Step 1: Write the failing tests**

`EMU/CCU_Emulator.Tests/Protocol/CcuPacketsTests.cs`:

```csharp
using CCU_Emulator.Protocol;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Protocol;

public class CcuPacketsTests
{
    [Fact]
    public void ObservedFromARealCcu()
    {
        // Bytes seen in .local/YammiC Device Logs (NOTIFY on the main RX characteristic).
        Assert.Equal(H("01 02 01"), CcuPackets.AuthenticationResponse(true));
        Assert.Equal(H("01 02 00"), CcuPackets.AuthenticationResponse(false));
        Assert.Equal(H("00 01 03 01 01"), CcuPackets.LibVersion());
        Assert.Equal(H("00 0B"), CcuPackets.LanguageRequest());
        Assert.Equal(H("00 09"), CcuPackets.DeviceTimeRequest());
        Assert.Equal(H("01 29"), CcuPackets.VolumeControllableRequest());
        Assert.Equal(H("01 35 00"), CcuPackets.Ping(0));
        Assert.Equal(H("01 35 04"), CcuPackets.Ping(4));
        Assert.Equal(H("02 32"), CcuPackets.VpbResponseNg());
    }

    [Fact]
    public void Requests()
    {
        Assert.Equal(H("00 03 01"), CcuPackets.WeatherRequestCurrent());
        Assert.Equal(H("00 03 02 0001E240 FFFE1DC0"), CcuPackets.WeatherRequestPoint(123456, -123456));
        Assert.Equal(H("01 1B"), CcuPackets.HeadsetStateRequest());
        Assert.Equal(H("01 32"), CcuPackets.CellSignalRequest());
    }

    [Fact]
    public void ButtonCommands()
    {
        Assert.Equal(H("01 17 01"), CcuPackets.MusicControl(MusicAction.Next));
        Assert.Equal(H("01 17 02"), CcuPackets.MusicControl(MusicAction.Previous));
        Assert.Equal(H("01 17 03"), CcuPackets.MusicControl(MusicAction.Play));
        Assert.Equal(H("01 17 04"), CcuPackets.MusicControl(MusicAction.Pause));
        Assert.Equal(H("01 1D 05"), CcuPackets.VolumeControl(VolumeAction.MediaUp));
        Assert.Equal(H("01 1D 02"), CcuPackets.VolumeControl(VolumeAction.PhoneDown));
        Assert.Equal(H("01 19 00"), CcuPackets.CallControl(CallAction.Reject));
        Assert.Equal(H("01 19 01"), CcuPackets.CallControl(CallAction.Answer));
        Assert.Equal(H("01 19 02"), CcuPackets.CallControl(CallAction.HangUp));
        Assert.Equal(H("00 13 00000007"), CcuPackets.NotificationRemoved(7));
    }

    [Fact]
    public void VpbPackets()
    {
        Assert.Equal(H("02 33"), CcuPackets.VpbGraphicRequest());
        Assert.Equal(H("02 36 00004E85 03 01"), CcuPackets.VpbChunkAck(20101, 3, ChunkResponse.Ok));
        Assert.Equal(H("02 36 00004E85 00 02"), CcuPackets.VpbChunkAck(20101, 0, ChunkResponse.Retry));
        Assert.Equal(H("02 38"), CcuPackets.VpbClearOk());
        Assert.Equal(H("02 39"), CcuPackets.VpbClearNg());
    }

    [Fact]
    public void BleConnectInformationCarriesCredentials()
        => Assert.Equal(Cat(H("01 3A"), Ascii("01234567890123"), Ascii("012345")),
            CcuPackets.BleConnectInformation("01234567890123", "012345"));
}
```

`EMU/CCU_Emulator.Tests/Protocol/DdtReceiverTests.cs`:

```csharp
using CCU_Emulator.Protocol;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Protocol;

public class DdtReceiverTests
{
    private readonly List<string> _events = [];
    private readonly List<byte[]> _messages = [];
    private readonly DdtReceiver _ddt = new();

    public DdtReceiverTests()
    {
        _ddt.StatusNotify += s => _events.Add("status " + Convert.ToHexString(s));
        _ddt.MessageComplete += m => { _events.Add("message"); _messages.Add(m); };
    }

    [Fact]
    public void ReassemblesChunksAndSignalsInOrder()
    {
        _ddt.OnControl(H("05 000006"));
        _ddt.OnData(H("05 000000 AABBCC"));
        _ddt.OnData(H("05 000003 DDEEFF"));

        Assert.Equal(["status 0105", "message", "status 0000"], _events);
        Assert.Equal(H("AABBCCDDEEFF"), Assert.Single(_messages));
        Assert.False(_ddt.Active);
        Assert.Equal(H("0000"), _ddt.StatusValue);
    }

    [Fact]
    public void StatusValueShowsActiveSession()
    {
        _ddt.OnControl(H("07 000003"));
        Assert.True(_ddt.Active);
        Assert.Equal(H("0107"), _ddt.StatusValue);
    }

    [Fact]
    public void ChunksMayArriveOutOfOrder()
    {
        _ddt.OnControl(H("01 000004"));
        _ddt.OnData(H("01 000002 CCDD"));
        _ddt.OnData(H("01 000000 AABB"));
        Assert.Equal(H("AABBCCDD"), Assert.Single(_messages));
    }

    // Review Focus 2
    [Fact]
    public void DataWithNoSessionIsIgnored()
    {
        _ddt.OnData(H("05 000000 AABB"));
        Assert.Empty(_events);
    }

    [Fact]
    public void WrongRequestIdIsIgnored()
    {
        _ddt.OnControl(H("05 000002"));
        _ddt.OnData(H("06 000000 AABB"));
        Assert.Empty(_messages);
        Assert.True(_ddt.Active);
    }

    [Fact]
    public void ChunkPastDeclaredSizeIsIgnored()
    {
        _ddt.OnControl(H("05 000002"));
        _ddt.OnData(H("05 000001 AABBCC"));
        Assert.Empty(_messages);
        _ddt.OnData(H("05 000000 AABB"));
        Assert.Equal(H("AABB"), Assert.Single(_messages));
    }

    [Fact]
    public void DuplicateChunkDoesNotCompleteEarly()
    {
        _ddt.OnControl(H("05 000004"));
        _ddt.OnData(H("05 000000 AABB"));
        _ddt.OnData(H("05 000000 AABB"));
        Assert.Empty(_messages);
        _ddt.OnData(H("05 000002 CCDD"));
        Assert.Equal(H("AABBCCDD"), Assert.Single(_messages));
    }

    [Fact]
    public void NewSessionMidTransferDropsTheOldOne()
    {
        _ddt.OnControl(H("05 000004"));
        _ddt.OnData(H("05 000000 AABB"));
        _ddt.OnControl(H("06 000002"));
        _ddt.OnData(H("06 000000 1122"));
        Assert.Equal(H("1122"), Assert.Single(_messages));
    }

    [Theory]
    [InlineData("05")]
    [InlineData("05 000000")]       // zero size
    [InlineData("05 FFFFFF")]       // 16 MiB: over the cap
    public void BadControlWritesAreIgnored(string hex)
    {
        _ddt.OnControl(H(hex));
        Assert.False(_ddt.Active);
        Assert.Empty(_events);
    }

    [Fact]
    public void ShortDataWriteIsIgnored()
    {
        _ddt.OnControl(H("05 000002"));
        _ddt.OnData(H("05 00"));
        Assert.True(_ddt.Active);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: build FAIL — `CcuPackets`, `DdtReceiver` not found.

- [ ] **Step 3: Implement `CcuPackets`**

`EMU/CCU_Emulator/Protocol/CcuPackets.cs`:

```csharp
namespace CCU_Emulator.Protocol;

public enum MusicAction : byte { Next = 1, Previous = 2, Play = 3, Pause = 4 }
public enum VolumeAction : byte { PhoneUp = 1, PhoneDown = 2, MuteMicrophone = 3, MuteSpeaker = 4, MediaUp = 5, MediaDown = 6 }
public enum CallAction : byte { Reject = 0, Answer = 1, HangUp = 2 }
public enum ChunkResponse : byte { Ok = 1, Retry = 2, Abort = 3 }

/// <summary>Builders for packets the CCU sends to the phone (main RX notifications, or SPP payloads).</summary>
public static class CcuPackets
{
    private static byte[] Cmd(CommandCode code, params byte[] rest)
        => new PacketWriter().U16((ushort)code).Bytes(rest).ToArray();

    public static byte[] AuthenticationResponse(bool ok) => Cmd(CommandCode.AuthenticationResponse, (byte)(ok ? 1 : 0));

    /// <summary>Exactly what a real CCU sends right after auth (SDK version bytes).</summary>
    public static byte[] LibVersion() => Cmd(CommandCode.LibVersion, 0x03, 0x01, 0x01);

    public static byte[] LanguageRequest() => Cmd(CommandCode.LanguageRequest);
    public static byte[] DeviceTimeRequest() => Cmd(CommandCode.VehicleSettingRequest);
    public static byte[] HeadsetStateRequest() => Cmd(CommandCode.HeadsetStateRequest);
    public static byte[] VolumeControllableRequest() => Cmd(CommandCode.VolumeControllableRequest);
    public static byte[] CellSignalRequest() => Cmd(CommandCode.CellSignalRequest);

    public static byte[] WeatherRequestCurrent() => Cmd(CommandCode.WeatherRequest, 0x01);

    public static byte[] WeatherRequestPoint(int latitudeE6, int longitudeE6)
        => new PacketWriter().U16((ushort)CommandCode.WeatherRequest).U8(0x02).I32(latitudeE6).I32(longitudeE6).ToArray();

    /// <summary>Keepalive: <c>01 35 &lt;seq&gt;</c>, the counter starting at 0 (as observed).</summary>
    public static byte[] Ping(byte seq) => Cmd(CommandCode.PingRequest, seq);

    public static byte[] MusicControl(MusicAction a) => Cmd(CommandCode.MusicControl, (byte)a);
    public static byte[] VolumeControl(VolumeAction a) => Cmd(CommandCode.VolumeControl, (byte)a);
    public static byte[] CallControl(CallAction a) => Cmd(CommandCode.IncomingCallControl, (byte)a);

    public static byte[] NotificationRemoved(int id)
        => new PacketWriter().U16((ushort)CommandCode.NotificationRemoved).I32(id).ToArray();

    public static byte[] VpbGraphicRequest() => Cmd(CommandCode.VpbGraphicRequest);
    public static byte[] VpbResponseNg() => Cmd(CommandCode.VpbResponseNg);
    public static byte[] VpbClearOk() => Cmd(CommandCode.VpbClearOk);
    public static byte[] VpbClearNg() => Cmd(CommandCode.VpbClearNg);

    public static byte[] VpbChunkAck(int contentId, byte index, ChunkResponse response)
        => new PacketWriter().U16((ushort)CommandCode.VpbSendGraphicResponse).I32(contentId).U8(index).U8((byte)response).ToArray();

    /// <summary>First SPP frame payload: <c>[0x013A][ccuId 14B][passKey 6B]</c>.</summary>
    public static byte[] BleConnectInformation(string ccuId, string passKey)
        => new PacketWriter().U16((ushort)CommandCode.BleConnectInformation).Utf8(ccuId).Utf8(passKey).ToArray();
}
```

- [ ] **Step 4: Implement `DdtReceiver`**

`EMU/CCU_Emulator/Protocol/DdtReceiver.cs`:

```csharp
namespace CCU_Emulator.Protocol;

/// <summary>
/// CCU side of the phone→CCU DDT session. The phone writes <c>[reqId][size 3B]</c> to the control
/// characteristic, the CCU notifies status <c>[1][reqId]</c>, the phone writes
/// <c>[reqId][offset 3B][data]</c> chunks to the data characteristic, and when the declared size
/// is filled the CCU notifies status <c>[0][0]</c>.
/// </summary>
public sealed class DdtReceiver
{
    public const int MaxSize = 1_000_000;

    public event Action<byte[]>? StatusNotify;
    public event Action<byte[]>? MessageComplete;
    public event Action<string>? Diagnostic;

    private byte _reqId;
    private byte[]? _buf;
    private bool[]? _filled;
    private int _filledCount;

    public bool Active => _buf != null;

    /// <summary>Value of the (readable) status characteristic: idle <c>00 00</c>, active <c>01 reqId</c>.</summary>
    public byte[] StatusValue => Active ? [1, _reqId] : [0, 0];

    public void OnControl(byte[] value)
    {
        if (value.Length < 4) { Diagnostic?.Invoke($"DDT control write too short ({value.Length} bytes)"); return; }
        var size = (value[1] << 16) | (value[2] << 8) | value[3];
        if (size <= 0 || size > MaxSize) { Diagnostic?.Invoke($"DDT control write with bad size {size}"); return; }
        if (Active) Diagnostic?.Invoke("DDT session started while another was active; dropping the old one");

        _reqId = value[0];
        _buf = new byte[size];
        _filled = new bool[size];
        _filledCount = 0;
        StatusNotify?.Invoke([1, _reqId]);
    }

    public void OnData(byte[] value)
    {
        if (_buf is null || _filled is null) { Diagnostic?.Invoke("DDT data with no active session"); return; }
        if (value.Length < 4 || value[0] != _reqId) { Diagnostic?.Invoke("DDT data with wrong request id or too short"); return; }

        var offset = (value[1] << 16) | (value[2] << 8) | value[3];
        var n = value.Length - 4;
        if (offset + n > _buf.Length) { Diagnostic?.Invoke("DDT chunk exceeds the declared size"); return; }

        for (var i = 0; i < n; i++)
        {
            _buf[offset + i] = value[4 + i];
            if (!_filled[offset + i]) { _filled[offset + i] = true; _filledCount++; }
        }

        if (_filledCount == _buf.Length)
        {
            var message = _buf;
            Reset();
            MessageComplete?.Invoke(message);   // a real CCU answers (e.g. auth) before the completion status
            StatusNotify?.Invoke([0, 0]);
        }
    }

    public void Reset()
    {
        _buf = null;
        _filled = null;
        _filledCount = 0;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: all PASS. (`WeatherRequestPoint(123456, -123456)`: `0x0001E240` = 123456, and `-123456` = `0xFFFE1DC0`.)

- [ ] **Step 6: Commit**

```bash
git add CCU-Emulator/CCU-Emulator/CCU_Emulator/Protocol CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests
git commit -m "Add CCU packet builders and DDT receiver"
```

---

### Task 5: Settings, packet log, transport interface, loopback transport

**Files:**
- Create: `EMU/CCU_Emulator/Emulation/CcuSettings.cs`, `EMU/CCU_Emulator/Emulation/PacketLog.cs`
- Create: `EMU/CCU_Emulator/Transport/CcuChannel.cs`, `ICcuTransport.cs`, `LoopbackTcpTransport.cs`
- Create: `EMU/emulator.settings.example.json`
- Test: `EMU/CCU_Emulator.Tests/Emulation/CcuSettingsTests.cs`, `EMU/CCU_Emulator.Tests/Transport/LoopbackTcpTransportTests.cs`

**Interfaces:**
- Consumes: `CommunityToolkit.Mvvm` (already referenced by the core project).
- Produces:
  - `enum TransportKind { LoopbackTcp, WindowsBle }`
  - `partial class CcuBehavior : ObservableObject` with bool properties `SendLibVersion, SendLanguageRequest, SendWeatherRequest, SendDeviceTimeRequest, SendHeadsetStateRequest, SendVolumeControllableRequest, SendCellSignalRequest, SendPing` (all default `true`), `int PingIntervalSeconds = 10`, `bool RejectVpbRequests`, `bool ForceAuthFailure`.
  - `class CcuSettings`: `string CcuId, PassKey, Mac`, `TransportKind Transport`, `string TcpBindAddress = "127.0.0.1"`, `int TcpPort = 48800`, `bool SppEnabled = true`, `string PhoneBluetoothAddress`, `CcuBehavior Behavior`; `[JsonIgnore] string AdvertisedName`; `IReadOnlyList<string> Validate()`.
  - `static class SettingsStore { CcuSettings Load(string path); void Save(string path, CcuSettings s); }`
  - `enum Direction { Rx, Tx, Info }`, `record PacketLogEntry(DateTime Time, Direction Direction, string Channel, string Name, string Hex, string Detail)`, `class PacketLog` with `ObservableCollection<PacketLogEntry> Entries`, `Add(Direction, string channel, string name, byte[] bytes, string detail = "")`, `Info(string source, string message)`, `Clear()`, capped at 2000 entries.
  - `enum CcuChannel : byte { SimpleWrite = 1, MainRx = 2, DdtControl = 3, DdtStatus = 4, DdtData = 5, Spp = 6 }`
  - `interface ICcuTransport : IAsyncDisposable` and `LoopbackTcpTransport` with `int Port`.

- [ ] **Step 1: Write the failing tests**

`EMU/CCU_Emulator.Tests/Emulation/CcuSettingsTests.cs`:

```csharp
using CCU_Emulator.Emulation;
using Xunit;

namespace CCU_Emulator.Tests.Emulation;

public class CcuSettingsTests
{
    [Fact]
    public void AdvertisedNameIsNineteenCharacters()
        => Assert.Equal("YCCU_00000000000000", new CcuSettings().AdvertisedName);

    [Fact]
    public void ValidateFlagsBadIdentity()
    {
        var s = new CcuSettings { CcuId = "123", PassKey = "12345678" };
        var errors = s.Validate();
        Assert.Contains(errors, e => e.Contains("CcuId"));
        Assert.Contains(errors, e => e.Contains("PassKey"));
        Assert.Empty(new CcuSettings().Validate());
    }

    [Fact]
    public void LoadCreatesDefaultsWhenMissingAndRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccu-test-{Guid.NewGuid():N}", "emulator.settings.json");
        try
        {
            var created = SettingsStore.Load(path);
            Assert.True(File.Exists(path));
            Assert.True(created.Behavior.SendPing);

            created.CcuId = "01234567890123";
            created.Behavior.PingIntervalSeconds = 3;
            created.Transport = TransportKind.WindowsBle;
            SettingsStore.Save(path, created);

            var loaded = SettingsStore.Load(path);
            Assert.Equal("01234567890123", loaded.CcuId);
            Assert.Equal(3, loaded.Behavior.PingIntervalSeconds);
            Assert.Equal(TransportKind.WindowsBle, loaded.Transport);
            Assert.Contains("\"WindowsBle\"", File.ReadAllText(path)); // enums are written as names
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }
}
```

`EMU/CCU_Emulator.Tests/Transport/LoopbackTcpTransportTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using CCU_Emulator.Emulation;
using CCU_Emulator.Transport;
using Xunit;

namespace CCU_Emulator.Tests.Transport;

public class LoopbackTcpTransportTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static async Task<(LoopbackTcpTransport transport, TcpClient client)> ConnectedPairAsync()
    {
        var transport = new LoopbackTcpTransport();
        var connected = new TaskCompletionSource<bool>();
        transport.BleConnectionChanged += c => { if (c) connected.TrySetResult(true); };
        await transport.StartAsync(new CcuSettings { TcpPort = 0 });
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, transport.Port);
        await connected.Task.WaitAsync(Timeout);
        return (transport, client);
    }

    [Fact]
    public async Task FramesFlowBothWays()
    {
        var (transport, client) = await ConnectedPairAsync();
        await using var _ = transport;
        using var __ = client;
        var received = new TaskCompletionSource<(CcuChannel, byte[])>();
        transport.DataReceived += (ch, d) => received.TrySetResult((ch, d));

        var stream = client.GetStream();
        await stream.WriteAsync(new byte[] { 1, 0, 3, 9, 8, 7 });
        var (channel, data) = await received.Task.WaitAsync(Timeout);
        Assert.Equal(CcuChannel.SimpleWrite, channel);
        Assert.Equal(new byte[] { 9, 8, 7 }, data);

        await transport.NotifyAsync(CcuChannel.MainRx, new byte[] { 1, 2 });
        var buf = new byte[5];
        await stream.ReadExactlyAsync(buf).AsTask().WaitAsync(Timeout);
        Assert.Equal(new byte[] { 2, 0, 2, 1, 2 }, buf);
    }

    [Fact]
    public async Task SppSharesTheConnection()
    {
        var (transport, client) = await ConnectedPairAsync();
        await using var _ = transport;
        using var __ = client;
        var sppUp = new TaskCompletionSource<bool>();
        transport.SppConnectionChanged += up => { if (up) sppUp.TrySetResult(true); };

        await transport.ConnectSppAsync();
        await sppUp.Task.WaitAsync(Timeout);

        await transport.SendSppAsync(new byte[] { 0xAA });
        var buf = new byte[4];
        await client.GetStream().ReadExactlyAsync(buf).AsTask().WaitAsync(Timeout);
        Assert.Equal(new byte[] { 6, 0, 1, 0xAA }, buf);
    }

    [Fact]
    public async Task ClientDisconnectRaisesBleAndSppDown()
    {
        var (transport, client) = await ConnectedPairAsync();
        await using var _ = transport;
        await transport.ConnectSppAsync();
        var bleDown = new TaskCompletionSource<bool>();
        var sppDown = new TaskCompletionSource<bool>();
        transport.BleConnectionChanged += up => { if (!up) bleDown.TrySetResult(true); };
        transport.SppConnectionChanged += up => { if (!up) sppDown.TrySetResult(true); };

        client.Dispose();
        await bleDown.Task.WaitAsync(Timeout);
        await sppDown.Task.WaitAsync(Timeout);
    }

    [Fact]
    public async Task NotifyWithNoClientThrows()
    {
        await using var transport = new LoopbackTcpTransport();
        await transport.StartAsync(new CcuSettings { TcpPort = 0 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.NotifyAsync(CcuChannel.MainRx, [1]));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: build FAIL — `CcuSettings`, `SettingsStore`, `LoopbackTcpTransport` not found.

- [ ] **Step 3: Implement settings**

`EMU/CCU_Emulator/Emulation/CcuSettings.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CCU_Emulator.Emulation;

public enum TransportKind { LoopbackTcp, WindowsBle }

/// <summary>Live-editable switches for what the emulated CCU does. Bound directly by the Behavior tab.</summary>
public sealed partial class CcuBehavior : ObservableObject
{
    [ObservableProperty] public partial bool SendLibVersion { get; set; } = true;
    [ObservableProperty] public partial bool SendLanguageRequest { get; set; } = true;
    [ObservableProperty] public partial bool SendWeatherRequest { get; set; } = true;
    [ObservableProperty] public partial bool SendDeviceTimeRequest { get; set; } = true;
    [ObservableProperty] public partial bool SendHeadsetStateRequest { get; set; } = true;
    [ObservableProperty] public partial bool SendVolumeControllableRequest { get; set; } = true;
    [ObservableProperty] public partial bool SendCellSignalRequest { get; set; } = true;
    [ObservableProperty] public partial bool SendPing { get; set; } = true;
    [ObservableProperty] public partial int PingIntervalSeconds { get; set; } = 10;

    /// <summary>Answer VPB requests with 0x0232 (reject) instead of 0x0233.</summary>
    [ObservableProperty] public partial bool RejectVpbRequests { get; set; }

    /// <summary>Answer every authentication with failure.</summary>
    [ObservableProperty] public partial bool ForceAuthFailure { get; set; }
}

public sealed class CcuSettings
{
    /// <summary>14 ASCII characters.</summary>
    public string CcuId { get; set; } = "00000000000000";

    /// <summary>6 ASCII characters.</summary>
    public string PassKey { get; set; } = "000000";

    public string Mac { get; set; } = "00:00:00:00:00:00";

    public TransportKind Transport { get; set; } = TransportKind.LoopbackTcp;

    public string TcpBindAddress { get; set; } = "127.0.0.1";
    public int TcpPort { get; set; } = 48800;

    /// <summary>Connect the Classic SPP link after the BLE link comes up (needed for VPB images).</summary>
    public bool SppEnabled { get; set; } = true;

    /// <summary>Classic Bluetooth address of the phone, for the Windows SPP client. Empty = unset.</summary>
    public string PhoneBluetoothAddress { get; set; } = "";

    public CcuBehavior Behavior { get; set; } = new();

    [JsonIgnore]
    public string AdvertisedName => "YCCU_" + CcuId;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (CcuId.Length != 14 || !CcuId.All(char.IsAsciiLetterOrDigit)) errors.Add("CcuId must be 14 ASCII letters/digits.");
        if (PassKey.Length != 6 || !PassKey.All(char.IsAsciiLetterOrDigit)) errors.Add("PassKey must be 6 ASCII letters/digits.");
        return errors;
    }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Loads the settings, writing a default file first when none exists.</summary>
    public static CcuSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            var defaults = new CcuSettings();
            Save(path, defaults);
            return defaults;
        }
        return JsonSerializer.Deserialize<CcuSettings>(File.ReadAllText(path), Options) ?? new CcuSettings();
    }

    public static void Save(string path, CcuSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
```

`EMU/emulator.settings.example.json`:

```json
{
  "CcuId": "01234567890123",
  "PassKey": "012345",
  "Mac": "00:AA:BB:CC:DD:EE",
  "Transport": "LoopbackTcp",
  "TcpBindAddress": "127.0.0.1",
  "TcpPort": 48800,
  "SppEnabled": true,
  "PhoneBluetoothAddress": "",
  "Behavior": {
    "SendLibVersion": true,
    "SendLanguageRequest": true,
    "SendWeatherRequest": true,
    "SendDeviceTimeRequest": true,
    "SendHeadsetStateRequest": true,
    "SendVolumeControllableRequest": true,
    "SendCellSignalRequest": true,
    "SendPing": true,
    "PingIntervalSeconds": 10,
    "RejectVpbRequests": false,
    "ForceAuthFailure": false
  }
}
```

- [ ] **Step 4: Implement the packet log**

`EMU/CCU_Emulator/Emulation/PacketLog.cs`:

```csharp
using System.Collections.ObjectModel;

namespace CCU_Emulator.Emulation;

public enum Direction { Rx, Tx, Info }

public sealed record PacketLogEntry(DateTime Time, Direction Direction, string Channel, string Name, string Hex, string Detail);

/// <summary>Capped, observable packet log. Mutate only from the thread the UI binds on.</summary>
public sealed class PacketLog
{
    public const int Capacity = 2000;
    private const int MaxHexBytes = 96;

    public ObservableCollection<PacketLogEntry> Entries { get; } = [];

    public void Add(Direction direction, string channel, string name, byte[] bytes, string detail = "")
        => Append(new PacketLogEntry(DateTime.Now, direction, channel, name, ToHex(bytes), detail));

    public void Info(string source, string message)
        => Append(new PacketLogEntry(DateTime.Now, Direction.Info, source, "", "", message));

    public void Clear() => Entries.Clear();

    private void Append(PacketLogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > Capacity) Entries.RemoveAt(0);
    }

    public static string ToHex(byte[] bytes)
    {
        var shown = bytes.Length > MaxHexBytes ? bytes[..MaxHexBytes] : bytes;
        var hex = string.Join(' ', shown.Select(b => b.ToString("X2")));
        return bytes.Length > MaxHexBytes ? $"{hex} …({bytes.Length} bytes)" : hex;
    }
}
```

- [ ] **Step 5: Implement the transport interface and loopback transport**

`EMU/CCU_Emulator/Transport/CcuChannel.cs`:

```csharp
namespace CCU_Emulator.Transport;

/// <summary>The byte pipes between phone and CCU. Values are also the loopback frame role byte.</summary>
public enum CcuChannel : byte
{
    /// <summary>Phone writes (simple write characteristic B606C7F9).</summary>
    SimpleWrite = 1,
    /// <summary>CCU notifications (main RX characteristic 9C810D26).</summary>
    MainRx = 2,
    /// <summary>Phone writes (DDT control characteristic 56851826).</summary>
    DdtControl = 3,
    /// <summary>CCU notifications (DDT status characteristic 75AB2788).</summary>
    DdtStatus = 4,
    /// <summary>Phone writes (DDT data characteristic E998AEAB).</summary>
    DdtData = 5,
    /// <summary>Raw bytes on the Classic SPP socket, both directions.</summary>
    Spp = 6,
}
```

`EMU/CCU_Emulator/Transport/ICcuTransport.cs`:

```csharp
using CCU_Emulator.Emulation;

namespace CCU_Emulator.Transport;

/// <summary>
/// Bytes in, bytes out between the emulated CCU and a phone. Events may fire on any thread.
/// </summary>
public interface ICcuTransport : IAsyncDisposable
{
    /// <summary>The phone wrote to a characteristic (SimpleWrite, DdtControl, DdtData) or sent SPP bytes.</summary>
    event Action<CcuChannel, byte[]>? DataReceived;

    /// <summary>A phone connected to / disconnected from the BLE side.</summary>
    event Action<bool>? BleConnectionChanged;

    /// <summary>The Classic SPP link came up / went down.</summary>
    event Action<bool>? SppConnectionChanged;

    /// <summary>Transport-level log lines (advertising state, errors).</summary>
    event Action<string>? Diagnostic;

    bool IsRunning { get; }

    /// <summary>Start advertising / listening.</summary>
    Task StartAsync(CcuSettings settings, CancellationToken ct = default);

    Task StopAsync();

    /// <summary>Notify the phone on <see cref="CcuChannel.MainRx"/> or <see cref="CcuChannel.DdtStatus"/>.</summary>
    Task NotifyAsync(CcuChannel channel, byte[] data);

    /// <summary>The CCU opens the Classic link to the phone's SPP server. Raises <see cref="SppConnectionChanged"/>.</summary>
    Task ConnectSppAsync(CancellationToken ct = default);

    /// <summary>Send raw bytes (already SPP-framed) to the phone.</summary>
    Task SendSppAsync(byte[] data);

    /// <summary>Drop the current phone connection (fault injection).</summary>
    Task DisconnectAsync();
}
```

`EMU/CCU_Emulator/Transport/LoopbackTcpTransport.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using CCU_Emulator.Emulation;

namespace CCU_Emulator.Transport;

/// <summary>
/// Radio-free transport for tests and for driving the emulator from another process.
/// One TCP client at a time (the newest wins). Frame: <c>[channel 1B][length 2B BE][bytes]</c>.
/// SPP shares the same connection under <see cref="CcuChannel.Spp"/>.
/// </summary>
public sealed class LoopbackTcpTransport : ICcuTransport
{
    public event Action<CcuChannel, byte[]>? DataReceived;
    public event Action<bool>? BleConnectionChanged;
    public event Action<bool>? SppConnectionChanged;
    public event Action<string>? Diagnostic;

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private TcpClient? _client;
    private bool _sppUp;

    public int Port { get; private set; }
    public bool IsRunning => _listener != null;

    public Task StartAsync(CcuSettings settings, CancellationToken ct = default)
    {
        if (_listener != null) return Task.CompletedTask;
        var address = IPAddress.Parse(settings.TcpBindAddress);
        _listener = new TcpListener(address, settings.TcpPort);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Diagnostic?.Invoke($"loopback transport listening on {address}:{Port}");
        _ = AcceptLoopAsync(_listener, _cts.Token);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                OnClientGone(_client); // newest wins
                client.NoDelay = true;
                _client = client;
                Diagnostic?.Invoke("client connected");
                BleConnectionChanged?.Invoke(true);
                _ = ReadLoopAsync(client, ct);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { }
    }

    private async Task ReadLoopAsync(TcpClient client, CancellationToken ct)
    {
        var header = new byte[3];
        try
        {
            var stream = client.GetStream();
            while (true)
            {
                await stream.ReadExactlyAsync(header, ct);
                var payload = new byte[(header[1] << 8) | header[2]];
                await stream.ReadExactlyAsync(payload, ct);
                DataReceived?.Invoke((CcuChannel)header[0], payload);
            }
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or OperationCanceledException or ObjectDisposedException or SocketException) { }
        finally
        {
            OnClientGone(client);
        }
    }

    private void OnClientGone(TcpClient? client)
    {
        if (client is null) return;
        if (ReferenceEquals(_client, client))
        {
            _client = null;
            SetSpp(false);
            Diagnostic?.Invoke("client disconnected");
            BleConnectionChanged?.Invoke(false);
        }
        client.Dispose();
    }

    private void SetSpp(bool up)
    {
        if (_sppUp == up) return;
        _sppUp = up;
        SppConnectionChanged?.Invoke(up);
    }

    private async Task WriteFrameAsync(CcuChannel channel, byte[] data)
    {
        var client = _client ?? throw new InvalidOperationException("no client connected");
        if (data.Length > ushort.MaxValue) throw new ArgumentException("frame too large", nameof(data));
        var frame = new byte[3 + data.Length];
        frame[0] = (byte)channel;
        frame[1] = (byte)(data.Length >> 8);
        frame[2] = (byte)data.Length;
        data.CopyTo(frame, 3);
        await _writeLock.WaitAsync();
        try { await client.GetStream().WriteAsync(frame); }
        finally { _writeLock.Release(); }
    }

    public Task NotifyAsync(CcuChannel channel, byte[] data)
    {
        if (channel is not (CcuChannel.MainRx or CcuChannel.DdtStatus))
            throw new ArgumentException("only MainRx and DdtStatus are notified", nameof(channel));
        return WriteFrameAsync(channel, data);
    }

    public Task ConnectSppAsync(CancellationToken ct = default)
    {
        if (_client is null) throw new InvalidOperationException("no client connected");
        SetSpp(true);
        return Task.CompletedTask;
    }

    public Task SendSppAsync(byte[] data) => WriteFrameAsync(CcuChannel.Spp, data);

    public Task DisconnectAsync()
    {
        OnClientGone(_client);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
        await DisconnectAsync();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add CCU-Emulator/CCU-Emulator/CCU_Emulator CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests CCU-Emulator/CCU-Emulator/emulator.settings.example.json
git commit -m "Add settings, packet log, transport interface and loopback transport"
```

---

### Task 6: Emulated CCU state and the session core

**Files:**
- Create: `EMU/CCU_Emulator/Emulation/CcuState.cs`, `EMU/CCU_Emulator/Emulation/CcuSession.cs`
- Create (test support): `EMU/CCU_Emulator.Tests/Support/FakeTransport.cs`, `PhoneSimulator.cs`, `SessionFixture.cs`
- Test: `EMU/CCU_Emulator.Tests/Emulation/CcuSessionCoreTests.cs`

**Interfaces:**
- Consumes: `ICcuTransport`, `CcuChannel`, `CcuSettings`, `PacketLog` (Task 5); `PhoneMessageDecoder`, message records, `CcuPackets`, `DdtReceiver` (Tasks 3–4).
- Produces:
  - `record WeatherEntry(WeatherSlot Slot, WeatherType Type, double Temperature, double MaxTemperature, double MinTemperature, int PrecipitationPercent)`
  - `record NotificationEntry(int Id, int Category, DateTime? Timestamp, string Title, string SubTitle, string Detail)`
  - `partial class CcuState : ObservableObject` with: `bool BleConnected, SppConnected, Authenticated`; `int AppId`; `string MusicArtist, MusicAlbum, MusicSong`; `int MusicDurationSeconds, MusicElapsedSeconds`; `PlaybackState MusicPlayback`; `double MusicRate`; `string WeatherLocation`; `ObservableCollection<WeatherEntry> Weather`; `bool CallRinging, CallActive`; `string CallerName, CallerNumber`; `int MissedCalls`; `int Battery, PhoneVolume, MediaVolume`; `bool Charging, HeadsetConnected, OnHook`; `CellSignal Signal`; `ThermalState Thermal`; `DateTime? PhoneTime`; `bool[] NotificationCategories`; `ObservableCollection<NotificationEntry> Notifications` (newest first, max 50); `NotificationEntry? LatestNotification`; `byte[]? VpbImage`; `bool VpbReceiving`; `int VpbReceivedBytes, VpbTotalBytes`; methods `UpsertWeather(WeatherEntry)`, `AddNotification(NotificationEntry)`, `RemoveNotification(int id)`.
  - `sealed partial class CcuSession`: ctor `CcuSession(ICcuTransport transport, CcuSettings settings, Action<Action>? post = null)`; properties `Settings`, `State`, `Log`; private `Send(CcuChannel, byte[], string name)`, `HandleMessage(byte[], string via)`, `Apply(PhoneMessage)`, `ResetConnectionState()`. `post` marshals every transport event onto the owner thread (UI thread in the app; synchronous in tests).

- [ ] **Step 1: Write the test support**

`EMU/CCU_Emulator.Tests/Support/FakeTransport.cs`:

```csharp
using CCU_Emulator.Emulation;
using CCU_Emulator.Transport;

namespace CCU_Emulator.Tests.Support;

#pragma warning disable CS0067 // Diagnostic is part of the interface but never raised by the fake
internal sealed class FakeTransport : ICcuTransport
{
    public event Action<CcuChannel, byte[]>? DataReceived;
    public event Action<bool>? BleConnectionChanged;
    public event Action<bool>? SppConnectionChanged;
    public event Action<string>? Diagnostic;

    public List<(CcuChannel Channel, byte[] Data)> Notifications { get; } = [];
    public List<byte[]> SppSent { get; } = [];
    public int SppConnectCalls { get; private set; }
    public int DisconnectCalls { get; private set; }
    public bool IsRunning { get; private set; }

    /// <summary>Everything notified on the main RX characteristic, in order.</summary>
    public List<byte[]> MainRx => Notifications.Where(n => n.Channel == CcuChannel.MainRx).Select(n => n.Data).ToList();

    public List<byte[]> DdtStatus => Notifications.Where(n => n.Channel == CcuChannel.DdtStatus).Select(n => n.Data).ToList();

    public Task StartAsync(CcuSettings settings, CancellationToken ct = default) { IsRunning = true; return Task.CompletedTask; }
    public Task StopAsync() { IsRunning = false; return Task.CompletedTask; }
    public Task NotifyAsync(CcuChannel channel, byte[] data) { Notifications.Add((channel, data)); return Task.CompletedTask; }
    public Task ConnectSppAsync(CancellationToken ct = default) { SppConnectCalls++; return Task.CompletedTask; }
    public Task SendSppAsync(byte[] data) { SppSent.Add(data); return Task.CompletedTask; }
    public Task DisconnectAsync() { DisconnectCalls++; return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void RaiseData(CcuChannel channel, byte[] data) => DataReceived?.Invoke(channel, data);
    public void RaiseBle(bool up) => BleConnectionChanged?.Invoke(up);
    public void RaiseSpp(bool up) => SppConnectionChanged?.Invoke(up);
}
```

`EMU/CCU_Emulator.Tests/Support/PhoneSimulator.cs`:

```csharp
using CCU_Emulator.Protocol;
using CCU_Emulator.Transport;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Support;

/// <summary>Plays the phone side against a <see cref="FakeTransport"/>.</summary>
internal sealed class PhoneSimulator(FakeTransport transport)
{
    private byte _requestId = 1;

    public void Connect() => transport.RaiseBle(true);

    public void WriteSimple(byte[] packet) => transport.RaiseData(CcuChannel.SimpleWrite, packet);

    /// <summary>Sends a message through a full DDT session, in chunks of <paramref name="chunkSize"/> bytes.</summary>
    public void SendDdt(byte[] message, int chunkSize = 20)
    {
        var id = _requestId++;
        transport.RaiseData(CcuChannel.DdtControl, [id, (byte)(message.Length >> 16), (byte)(message.Length >> 8), (byte)message.Length]);
        for (var offset = 0; offset < message.Length; offset += chunkSize)
        {
            var n = Math.Min(chunkSize, message.Length - offset);
            var chunk = new byte[4 + n];
            chunk[0] = id;
            chunk[1] = (byte)(offset >> 16);
            chunk[2] = (byte)(offset >> 8);
            chunk[3] = (byte)offset;
            Array.Copy(message, offset, chunk, 4, n);
            transport.RaiseData(CcuChannel.DdtData, chunk);
        }
    }

    public void Authenticate(string ccuId, string passKey) => SendDdt(Cat(H("0101"), Ascii(ccuId), Ascii(passKey)));

    /// <summary>Sends one SPP-framed payload.</summary>
    public void SendSpp(byte[] payload) => transport.RaiseData(CcuChannel.Spp, SppFrame.Encode(payload));
}
```

`EMU/CCU_Emulator.Tests/Support/SessionFixture.cs`:

```csharp
using CCU_Emulator.Emulation;

namespace CCU_Emulator.Tests.Support;

internal sealed class SessionFixture
{
    public FakeTransport Transport { get; } = new();
    public CcuSettings Settings { get; } = new() { CcuId = "01234567890123", PassKey = "012345" };
    public CcuSession Session { get; }
    public PhoneSimulator Phone { get; }

    public SessionFixture()
    {
        Session = new CcuSession(Transport, Settings);
        Phone = new PhoneSimulator(Transport);
    }

    /// <summary>Connects the phone and authenticates with the right credentials.</summary>
    public void ConnectAndAuthenticate()
    {
        Phone.Connect();
        Phone.Authenticate(Settings.CcuId, Settings.PassKey);
    }
}
```

- [ ] **Step 2: Write the failing tests**

`EMU/CCU_Emulator.Tests/Emulation/CcuSessionCoreTests.cs`:

```csharp
using CCU_Emulator.Emulation;
using CCU_Emulator.Protocol;
using CCU_Emulator.Tests.Support;
using CCU_Emulator.Transport;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Emulation;

public class CcuSessionCoreTests
{
    private readonly SessionFixture _f = new();
    private CcuState State => _f.Session.State;

    [Fact]
    public void AuthSucceedsWithTheConfiguredIdentity()
    {
        _f.Settings.Behavior.SendLibVersion = false;
        _f.Settings.Behavior.SendLanguageRequest = false;
        _f.Settings.Behavior.SendWeatherRequest = false;
        _f.Settings.Behavior.SendDeviceTimeRequest = false;
        _f.Settings.Behavior.SendHeadsetStateRequest = false;
        _f.Settings.Behavior.SendVolumeControllableRequest = false;
        _f.Settings.Behavior.SendCellSignalRequest = false;
        _f.ConnectAndAuthenticate();

        Assert.True(State.BleConnected);
        Assert.True(State.Authenticated);
        Assert.Equal([H("0102 01")], _f.Transport.MainRx);
        // Status: active(reqId 1) first, completion last, like the real CCU.
        Assert.Equal([H("0101"), H("0000")], _f.Transport.DdtStatus);
    }

    [Theory]
    [InlineData("01234567890123", "999999")]
    [InlineData("99999999999999", "012345")]
    public void AuthFailsWithWrongCredentials(string ccuId, string passKey)
    {
        _f.Phone.Connect();
        _f.Phone.Authenticate(ccuId, passKey);
        Assert.False(State.Authenticated);
        Assert.Equal([H("0102 00")], _f.Transport.MainRx);
    }

    [Fact]
    public void ForcedAuthFailureRejectsCorrectCredentials()
    {
        _f.Settings.Behavior.ForceAuthFailure = true;
        _f.ConnectAndAuthenticate();
        Assert.False(State.Authenticated);
        Assert.Equal(H("0102 00"), Assert.Single(_f.Transport.MainRx));
    }

    [Fact]
    public void IdentifyRecordsTheAppId()
    {
        _f.Phone.WriteSimple(H("0002 01 00001234"));
        Assert.Equal(0x1234, State.AppId);
    }

    [Fact]
    public void MusicOverDdtUpdatesState()
    {
        _f.Phone.SendDdt(Cat(H("0116"), Str8("Test Artist"), Str8("Test Album"), Str8("Test Song"), H("0000D2 02 000A 00000F")));
        Assert.Equal("Test Artist", State.MusicArtist);
        Assert.Equal("Test Album", State.MusicAlbum);
        Assert.Equal("Test Song", State.MusicSong);
        Assert.Equal(210, State.MusicDurationSeconds);
        Assert.Equal(15, State.MusicElapsedSeconds);
        Assert.Equal(PlaybackState.Playing, State.MusicPlayback);
        Assert.Contains(_f.Session.Log.Entries, e => e.Name == "MusicMetaData" && e.Channel == "DDT");
    }

    [Fact]
    public void WeatherSlotsAreUpsertedAndSortedBySlot()
    {
        _f.Phone.WriteSimple(Cat(H("0004 01"), Ascii("Berlin")));
        _f.Phone.WriteSimple(H("0005 01 06 03 0064 0078 0050 04"));   // After1Hour, Rain
        _f.Phone.WriteSimple(H("0005 02 00 00 00FA 0118 00C8 02"));   // Current, Sunny
        _f.Phone.WriteSimple(H("0005 03 00 14 0105 0118 00C8 00"));   // Current again, Cloudy 26.1

        Assert.Equal("Berlin", State.WeatherLocation);
        Assert.Equal([WeatherSlot.Current, WeatherSlot.After1Hour], State.Weather.Select(w => w.Slot).ToArray());
        Assert.Equal(WeatherType.Cloudy, State.Weather[0].Type);
        Assert.Equal(26.1, State.Weather[0].Temperature, 1);
    }

    [Fact]
    public void NotificationsAreAddedReplacedAndRemoved()
    {
        byte[] Notification(int id, string title)
        {
            var proto = Cat(new PacketWriter().U8(0x18).U8(id).ToArray(), H("5A"), Str8(title));
            return Cat(new PacketWriter().U16(0x012F).U24(proto.Length).ToArray(), proto);
        }

        _f.Phone.SendDdt(Notification(1, "First"));
        _f.Phone.SendDdt(Notification(2, "Second"));
        Assert.Equal(["Second", "First"], State.Notifications.Select(n => n.Title).ToArray());
        Assert.Equal("Second", State.LatestNotification!.Title);

        _f.Phone.SendDdt(Notification(1, "First (edited)"));
        Assert.Equal(["First (edited)", "Second"], State.Notifications.Select(n => n.Title).ToArray());

        _f.Phone.WriteSimple(H("0012 05 00000001"));   // RemoveNotification(1)
        Assert.Equal(["Second"], State.Notifications.Select(n => n.Title).ToArray());
    }

    [Fact]
    public void CallLifecycle()
    {
        _f.Phone.SendDdt(Cat(H("0118"), Str8("Alice"), Str8("+15551234"), H("00")));
        Assert.True(State.CallRinging);
        Assert.Equal("Alice", State.CallerName);
        Assert.Equal("+15551234", State.CallerNumber);

        _f.Phone.WriteSimple(H("0134 01"));
        Assert.True(State.CallActive);
        Assert.False(State.CallRinging);

        _f.Phone.WriteSimple(H("0134 02"));
        Assert.False(State.CallActive);

        _f.Phone.SendDdt(Cat(H("0118"), Str8("Bob"), Str8("555"), H("01")));
        Assert.Equal(1, State.MissedCalls);
        Assert.False(State.CallRinging);
    }

    [Fact]
    public void PhoneStateMessagesUpdateState()
    {
        _f.Phone.WriteSimple(H("013C E4 B2 C8 47"));
        Assert.Equal(100, State.Battery);
        Assert.True(State.Charging);
        Assert.Equal(CellSignal.Lv3, State.Signal);
        Assert.True(State.HeadsetConnected);

        _f.Phone.WriteSimple(H("011A 02"));
        Assert.Equal(ThermalState.High, State.Thermal);
        _f.Phone.WriteSimple(H("0133 05"));
        Assert.Equal(CellSignal.Lv4, State.Signal);
        _f.Phone.WriteSimple(H("011E 21 00"));
        Assert.Equal(33, State.PhoneVolume);
        _f.Phone.WriteSimple(H("011E 40 01"));
        Assert.Equal(64, State.MediaVolume);
    }

    // Review Focus 1: garbage never escapes the session
    [Theory]
    [InlineData("0116 40 61")]          // string length past the end
    [InlineData("0101 3031")]           // truncated auth
    [InlineData("0235 00000001")]       // truncated VPB chunk
    [InlineData("7777 AABB")]           // unknown command
    [InlineData("00")]                  // shorter than a command code
    public void GarbageIsLoggedAndIgnored(string hex)
    {
        _f.Phone.WriteSimple(H(hex));
        Assert.Contains(_f.Session.Log.Entries, e => e.Direction == Direction.Rx);
    }

    [Fact]
    public void DisconnectResetsConnectionStateAndDropsAPartialDdtSession()
    {
        _f.ConnectAndAuthenticate();
        Assert.True(State.Authenticated);

        // Start a DDT session and never finish it.
        _f.Transport.RaiseData(CcuChannel.DdtControl, H("09 000004"));
        _f.Transport.RaiseBle(false);
        Assert.False(State.BleConnected);
        Assert.False(State.Authenticated);

        // After reconnecting, a fresh session works normally.
        _f.Phone.Connect();
        _f.Phone.SendDdt(Cat(H("0116"), Str8("A"), Str8("B"), Str8("C"), H("000000 01 000A 000000")));
        Assert.Equal("C", State.MusicSong);
    }

    [Fact]
    public void TransportEventsAreMarshalledThroughThePostDelegate()
    {
        var queue = new Queue<Action>();
        var transport = new FakeTransport();
        var session = new CcuSession(transport, new CcuSettings(), queue.Enqueue);

        transport.RaiseBle(true);
        Assert.False(session.State.BleConnected);   // nothing happened yet
        while (queue.Count > 0) queue.Dequeue()();
        Assert.True(session.State.BleConnected);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: build FAIL — `CcuSession`, `CcuState` not found.

- [ ] **Step 4: Implement `CcuState`**

`EMU/CCU_Emulator/Emulation/CcuState.cs`:

```csharp
using System.Collections.ObjectModel;
using CCU_Emulator.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CCU_Emulator.Emulation;

public sealed record WeatherEntry(
    WeatherSlot Slot, WeatherType Type, double Temperature, double MaxTemperature, double MinTemperature, int PrecipitationPercent);

public sealed record NotificationEntry(int Id, int Category, DateTime? Timestamp, string Title, string SubTitle, string Detail);

/// <summary>Everything the emulated CCU currently knows about the phone. Mutate only on the owner (UI) thread.</summary>
public sealed partial class CcuState : ObservableObject
{
    public const int MaxNotifications = 50;

    // Link
    [ObservableProperty] public partial bool BleConnected { get; set; }
    [ObservableProperty] public partial bool SppConnected { get; set; }
    [ObservableProperty] public partial bool Authenticated { get; set; }
    [ObservableProperty] public partial int AppId { get; set; }

    // Music
    [ObservableProperty] public partial string MusicArtist { get; set; } = "";
    [ObservableProperty] public partial string MusicAlbum { get; set; } = "";
    [ObservableProperty] public partial string MusicSong { get; set; } = "";
    [ObservableProperty] public partial int MusicDurationSeconds { get; set; }
    [ObservableProperty] public partial int MusicElapsedSeconds { get; set; }
    [ObservableProperty] public partial PlaybackState MusicPlayback { get; set; }
    [ObservableProperty] public partial double MusicRate { get; set; }

    // Weather
    [ObservableProperty] public partial string WeatherLocation { get; set; } = "";
    public ObservableCollection<WeatherEntry> Weather { get; } = [];

    // Calls
    [ObservableProperty] public partial bool CallRinging { get; set; }
    [ObservableProperty] public partial bool CallActive { get; set; }
    [ObservableProperty] public partial string CallerName { get; set; } = "";
    [ObservableProperty] public partial string CallerNumber { get; set; } = "";
    [ObservableProperty] public partial int MissedCalls { get; set; }

    // Phone
    [ObservableProperty] public partial int Battery { get; set; }
    [ObservableProperty] public partial bool Charging { get; set; }
    [ObservableProperty] public partial int PhoneVolume { get; set; }
    [ObservableProperty] public partial int MediaVolume { get; set; }
    [ObservableProperty] public partial CellSignal Signal { get; set; }
    [ObservableProperty] public partial ThermalState Thermal { get; set; }
    [ObservableProperty] public partial bool HeadsetConnected { get; set; }
    [ObservableProperty] public partial bool OnHook { get; set; }
    [ObservableProperty] public partial DateTime? PhoneTime { get; set; }

    // Notifications
    [ObservableProperty] public partial bool[] NotificationCategories { get; set; } = new bool[12];
    [ObservableProperty] public partial NotificationEntry? LatestNotification { get; set; }
    public ObservableCollection<NotificationEntry> Notifications { get; } = [];

    // Virtual Pit Board
    [ObservableProperty] public partial byte[]? VpbImage { get; set; }
    [ObservableProperty] public partial bool VpbReceiving { get; set; }
    [ObservableProperty] public partial int VpbReceivedBytes { get; set; }
    [ObservableProperty] public partial int VpbTotalBytes { get; set; }

    /// <summary>Inserts or replaces the entry for its slot, keeping the list ordered by slot value.</summary>
    public void UpsertWeather(WeatherEntry entry)
    {
        for (var i = 0; i < Weather.Count; i++)
        {
            if (Weather[i].Slot == entry.Slot) { Weather[i] = entry; return; }
            if ((byte)Weather[i].Slot > (byte)entry.Slot) { Weather.Insert(i, entry); return; }
        }
        Weather.Add(entry);
    }

    /// <summary>Adds (or replaces, by id) a notification at the top of the list.</summary>
    public void AddNotification(NotificationEntry entry)
    {
        RemoveNotification(entry.Id);
        Notifications.Insert(0, entry);
        while (Notifications.Count > MaxNotifications) Notifications.RemoveAt(Notifications.Count - 1);
        LatestNotification = entry;
    }

    public void RemoveNotification(int id)
    {
        for (var i = 0; i < Notifications.Count; i++)
        {
            if (Notifications[i].Id != id) continue;
            Notifications.RemoveAt(i);
            break;
        }
        if (LatestNotification?.Id == id) LatestNotification = Notifications.FirstOrDefault();
    }
}
```

- [ ] **Step 5: Implement the session core**

`EMU/CCU_Emulator/Emulation/CcuSession.cs`:

```csharp
using CCU_Emulator.Protocol;
using CCU_Emulator.Transport;

namespace CCU_Emulator.Emulation;

/// <summary>
/// The emulated CCU: wires a transport to the protocol codecs, keeps <see cref="CcuState"/>, and
/// logs every packet. Every transport event is handed to <c>post</c> so all state is touched on
/// one thread (the UI thread in the app, inline in tests).
/// </summary>
public sealed partial class CcuSession
{
    private readonly ICcuTransport _transport;
    private readonly Action<Action> _post;
    private readonly DdtReceiver _ddt = new();

    public CcuSettings Settings { get; }
    public CcuState State { get; } = new();
    public PacketLog Log { get; } = new();

    public CcuSession(ICcuTransport transport, CcuSettings settings, Action<Action>? post = null)
    {
        _transport = transport;
        Settings = settings;
        _post = post ?? (a => a());

        _transport.DataReceived += (channel, data) => _post(() => OnData(channel, data));
        _transport.BleConnectionChanged += up => _post(() => OnBleConnection(up));
        _transport.Diagnostic += message => _post(() => Log.Info("Transport", message));

        _ddt.StatusNotify += status => Send(CcuChannel.DdtStatus, status, "DdtStatus");
        _ddt.MessageComplete += message => HandleMessage(message, "DDT");
        _ddt.Diagnostic += message => Log.Info("DDT", message);
    }

    // ── Transport events ────────────────────────────────────────────────────────

    private void OnData(CcuChannel channel, byte[] value)
    {
        switch (channel)
        {
            case CcuChannel.SimpleWrite: HandleMessage(value, "SimpleWrite"); break;
            case CcuChannel.DdtControl: _ddt.OnControl(value); break;
            case CcuChannel.DdtData: _ddt.OnData(value); break;
            default: Log.Info("Transport", $"unexpected data on {channel}"); break;
        }
    }

    private void OnBleConnection(bool connected)
    {
        State.BleConnected = connected;
        Log.Info("BLE", connected ? "phone connected" : "phone disconnected");
        if (!connected) ResetConnectionState();
    }

    private void ResetConnectionState()
    {
        _ddt.Reset();
        State.Authenticated = false;
        State.AppId = 0;
        State.CallRinging = false;
        State.CallActive = false;
    }

    // ── Sending ─────────────────────────────────────────────────────────────────

    private void Send(CcuChannel channel, byte[] data, string name)
    {
        Log.Add(Direction.Tx, channel.ToString(), name, data);
        _ = SendAsync(channel, data);
    }

    private async Task SendAsync(CcuChannel channel, byte[] data)
    {
        try { await _transport.NotifyAsync(channel, data); }
        catch (Exception e) { _post(() => Log.Info("Transport", $"notify {channel} failed: {e.Message}")); }
    }

    // ── Receiving ───────────────────────────────────────────────────────────────

    private void HandleMessage(byte[] data, string via)
    {
        PhoneMessage message;
        try
        {
            message = PhoneMessageDecoder.Decode(data);
        }
        catch (MalformedPacketException e)
        {
            Log.Add(Direction.Rx, via, "Malformed", data, e.Message);
            return;
        }

        Log.Add(Direction.Rx, via, message.GetType().Name.Replace("Message", ""), data, message.ToString() ?? "");
        Apply(message);
    }

    private void Apply(PhoneMessage message)
    {
        switch (message)
        {
            case AuthenticationMessage a:
                HandleAuth(a);
                break;

            case IdentifyAppMessage i:
                State.AppId = i.AppId;
                break;

            case MusicMetaDataMessage m:
                State.MusicArtist = m.Artist;
                State.MusicAlbum = m.Album;
                State.MusicSong = m.Song;
                State.MusicDurationSeconds = m.DurationSeconds;
                State.MusicElapsedSeconds = m.ElapsedSeconds;
                State.MusicPlayback = m.State;
                State.MusicRate = m.Rate;
                break;

            case WeatherLocationMessage w:
                State.WeatherLocation = w.Name;
                break;

            case WeatherInfoMessage w:
                State.UpsertWeather(new WeatherEntry(w.Slot, w.Type, w.Temperature, w.MaxTemperature, w.MinTemperature, w.PrecipitationPercent));
                break;

            case DeviceTimeMessage t:
                State.PhoneTime = t.Time;
                break;

            case NotificationMessage n:
                State.AddNotification(new NotificationEntry(n.Id, n.Category, n.Timestamp, n.Title, n.SubTitle, n.Detail));
                break;

            case NotificationLifecycleMessage { Operation: CommandCode.RemoveNotification } l:
                State.RemoveNotification(l.Id);
                break;

            case NotificationCategoryMessage c:
                State.NotificationCategories = c.Enabled;
                break;

            case IncomingCallInfoMessage c when c.Missed:
                State.MissedCalls++;
                State.CallRinging = false;
                break;

            case IncomingCallInfoMessage c:
                State.CallerName = c.Name;
                State.CallerNumber = c.Number;
                State.CallRinging = true;
                break;

            case CallChangeMessage c:
                State.CallActive = c.Started;
                if (c.Started) State.CallRinging = false;
                break;

            case PhoneStateMessage p:
                State.Battery = p.Battery;
                State.Charging = p.Charging;
                State.PhoneVolume = p.PhoneVolume;
                State.MediaVolume = p.MediaVolume;
                State.Signal = p.Signal;
                State.Thermal = p.Thermal;
                State.HeadsetConnected = p.HeadsetConnected;
                State.OnHook = p.OnHook;
                break;

            case ThermalStateMessage t:
                State.Thermal = t.State;
                break;

            case CellSignalResponseMessage s:
                State.Signal = s.Signal;
                break;

            case HeadsetStateResponseMessage h:
                State.HeadsetConnected = h.Connected;
                break;

            case VolumeLevelMessage { Type: VolumeType.Phone } v:
                State.PhoneVolume = v.Percent;
                break;

            case VolumeLevelMessage v:
                State.MediaVolume = v.Percent;
                break;

            // Logged by HandleMessage; nothing to track.
            case LanguageResponseMessage:
            case VolumeControllableResponseMessage:
            case PongMessage:
            case UnknownMessage:
                break;
        }
    }

    private void HandleAuth(AuthenticationMessage a)
    {
        var ok = !Settings.Behavior.ForceAuthFailure && a.CcuId == Settings.CcuId && a.PassKey == Settings.PassKey;
        State.Authenticated = ok;
        Send(CcuChannel.MainRx, CcuPackets.AuthenticationResponse(ok), ok ? "AuthenticationResponse (ok)" : "AuthenticationResponse (failed)");
        Log.Info("Auth", ok ? "phone authenticated" : "rejected: wrong CCUID/passkey or forced failure");
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: all PASS except none. In `AuthSucceedsWithTheConfiguredIdentity` the bootstrap requests are off so only `01 02 01` is on main RX (bootstrap is added in Task 7). In `DisconnectResetsConnectionStateAndDropsAPartialDdtSession` the half-open `09 000004` session is dropped by `_ddt.Reset()`.

- [ ] **Step 7: Commit**

```bash
git add CCU-Emulator/CCU-Emulator/CCU_Emulator/Emulation CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests
git commit -m "Add emulated CCU state and session core"
```

---

### Task 7: CCU-initiated requests and the tick

**Files:**
- Create: `EMU/CCU_Emulator/Emulation/CcuSession.Requests.cs`
- Modify: `EMU/CCU_Emulator/Emulation/CcuSession.cs` (two edits)
- Test: `EMU/CCU_Emulator.Tests/Emulation/CcuSessionRequestsTests.cs`

**Interfaces:**
- Consumes: `CcuSession.Send`, `CcuBehavior` (Tasks 5–6), `CcuPackets` (Task 4).
- Produces: `void CcuSession.Tick(TimeSpan elapsed)` (called about once a second by the host); private `StartBootstrap()` and `SendCcu(byte[] packet, string name)` (sends on `CcuChannel.MainRx`), which Tasks 8 and 9 reuse.

- [ ] **Step 1: Write the failing tests**

`EMU/CCU_Emulator.Tests/Emulation/CcuSessionRequestsTests.cs`:

```csharp
using CCU_Emulator.Tests.Support;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Emulation;

public class CcuSessionRequestsTests
{
    private readonly SessionFixture _f = new();

    [Fact]
    public void AfterAuthTheCcuSendsItsStartupRequestsInOrder()
    {
        _f.ConnectAndAuthenticate();
        Assert.Equal(
            [
                H("0102 01"),      // auth ok
                H("0001 030101"),  // LibVersion
                H("000B"),         // LanguageRequest
                H("0009"),         // DeviceTimeRequest
                H("0129"),         // VolumeControllableRequest
                H("0003 01"),      // WeatherRequest (current)
                H("011B"),         // HeadsetStateRequest
                H("0132"),         // CellSignalRequest
            ],
            _f.Transport.MainRx);
    }

    [Fact]
    public void TogglesSuppressIndividualRequests()
    {
        var b = _f.Settings.Behavior;
        b.SendLibVersion = false;
        b.SendWeatherRequest = false;
        b.SendHeadsetStateRequest = false;
        b.SendCellSignalRequest = false;
        _f.ConnectAndAuthenticate();
        Assert.Equal([H("0102 01"), H("000B"), H("0009"), H("0129")], _f.Transport.MainRx);
    }

    [Fact]
    public void NoRequestsAreSentWhenAuthFails()
    {
        _f.Settings.Behavior.ForceAuthFailure = true;
        _f.ConnectAndAuthenticate();
        Assert.Equal([H("0102 00")], _f.Transport.MainRx);
    }

    [Fact]
    public void DeviceTimeRequestRepeatsEveryFiveSecondsUntilAnswered()
    {
        _f.ConnectAndAuthenticate();
        var baseline = _f.Transport.MainRx.Count;

        _f.Session.Tick(TimeSpan.FromSeconds(4));
        Assert.Equal(baseline, _f.Transport.MainRx.Count);
        _f.Session.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(H("0009"), _f.Transport.MainRx[^1]);
        Assert.Equal(baseline + 1, _f.Transport.MainRx.Count);

        _f.Phone.WriteSimple(H("000A 03 0000000069E8B5F0"));
        _f.Session.Tick(TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(_f.Transport.MainRx.Skip(baseline + 1), p => p.SequenceEqual(H("0009")));
    }

    [Fact]
    public void PingCarriesAnIncrementingCounterAtTheConfiguredInterval()
    {
        _f.Settings.Behavior.SendDeviceTimeRequest = false;
        _f.ConnectAndAuthenticate();
        var pings = () => _f.Transport.MainRx.Where(p => p.Length == 3 && p[0] == 0x01 && p[1] == 0x35).ToList();

        _f.Session.Tick(TimeSpan.FromSeconds(9));
        Assert.Empty(pings());
        _f.Session.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal([H("0135 00")], pings());
        _f.Session.Tick(TimeSpan.FromSeconds(10));
        Assert.Equal([H("0135 00"), H("0135 01")], pings());

        _f.Settings.Behavior.PingIntervalSeconds = 3;
        _f.Session.Tick(TimeSpan.FromSeconds(3));
        Assert.Equal(H("0135 02"), pings()[^1]);
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(true, 0)]
    public void PingDisabledByToggleOrZeroInterval(bool sendPing, int interval)
    {
        _f.Settings.Behavior.SendDeviceTimeRequest = false;
        _f.Settings.Behavior.SendPing = sendPing;
        _f.Settings.Behavior.PingIntervalSeconds = interval;
        _f.ConnectAndAuthenticate();
        _f.Session.Tick(TimeSpan.FromMinutes(5));
        Assert.DoesNotContain(_f.Transport.MainRx, p => p.Length == 3 && p[0] == 0x01 && p[1] == 0x35);
    }

    [Fact]
    public void TickDoesNothingBeforeAuth()
    {
        _f.Phone.Connect();
        _f.Session.Tick(TimeSpan.FromMinutes(5));
        Assert.Empty(_f.Transport.MainRx);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: build FAIL — `CcuSession.Tick` not found.

- [ ] **Step 3: Implement the requests partial**

`EMU/CCU_Emulator/Emulation/CcuSession.Requests.cs`:

```csharp
using CCU_Emulator.Protocol;
using CCU_Emulator.Transport;

namespace CCU_Emulator.Emulation;

public sealed partial class CcuSession
{
    private static readonly TimeSpan DeviceTimeRepeat = TimeSpan.FromSeconds(5);

    private TimeSpan _sinceDeviceTimeRequest;
    private TimeSpan _sincePing;
    private bool _deviceTimeAnswered;
    private byte _pingSeq;

    /// <summary>Sends <paramref name="packet"/> to the phone on the main RX characteristic.</summary>
    private void SendCcu(byte[] packet, string name) => Send(CcuChannel.MainRx, packet, name);

    /// <summary>What a real CCU sends right after a successful auth (order taken from a capture).</summary>
    private void StartBootstrap()
    {
        var b = Settings.Behavior;
        _deviceTimeAnswered = false;
        _sinceDeviceTimeRequest = TimeSpan.Zero;
        _sincePing = TimeSpan.Zero;
        _pingSeq = 0;

        if (b.SendLibVersion) SendCcu(CcuPackets.LibVersion(), "LibVersion");
        if (b.SendLanguageRequest) SendCcu(CcuPackets.LanguageRequest(), "LanguageRequest");
        if (b.SendDeviceTimeRequest) SendCcu(CcuPackets.DeviceTimeRequest(), "DeviceTimeRequest");
        if (b.SendVolumeControllableRequest) SendCcu(CcuPackets.VolumeControllableRequest(), "VolumeControllableRequest");
        if (b.SendWeatherRequest) SendCcu(CcuPackets.WeatherRequestCurrent(), "WeatherRequest");
        if (b.SendHeadsetStateRequest) SendCcu(CcuPackets.HeadsetStateRequest(), "HeadsetStateRequest");
        if (b.SendCellSignalRequest) SendCcu(CcuPackets.CellSignalRequest(), "CellSignalRequest");
    }

    /// <summary>Advances the session clock; the host calls this about once a second.</summary>
    public void Tick(TimeSpan elapsed)
    {
        if (!State.Authenticated) return;
        var b = Settings.Behavior;

        if (b.SendDeviceTimeRequest && !_deviceTimeAnswered)
        {
            _sinceDeviceTimeRequest += elapsed;
            if (_sinceDeviceTimeRequest >= DeviceTimeRepeat)
            {
                _sinceDeviceTimeRequest = TimeSpan.Zero;
                SendCcu(CcuPackets.DeviceTimeRequest(), "DeviceTimeRequest (repeat)");
            }
        }

        if (b.SendPing && b.PingIntervalSeconds > 0)
        {
            _sincePing += elapsed;
            if (_sincePing >= TimeSpan.FromSeconds(b.PingIntervalSeconds))
            {
                _sincePing = TimeSpan.Zero;
                SendCcu(CcuPackets.Ping(_pingSeq++), "PingRequest");
            }
        }
    }
}
```

- [ ] **Step 4: Hook it into the core**

In `EMU/CCU_Emulator/Emulation/CcuSession.cs`, replace

```csharp
        Log.Info("Auth", ok ? "phone authenticated" : "rejected: wrong CCUID/passkey or forced failure");
```

with

```csharp
        Log.Info("Auth", ok ? "phone authenticated" : "rejected: wrong CCUID/passkey or forced failure");
        if (ok) StartBootstrap();
```

and replace

```csharp
            case DeviceTimeMessage t:
                State.PhoneTime = t.Time;
                break;
```

with

```csharp
            case DeviceTimeMessage t:
                State.PhoneTime = t.Time;
                _deviceTimeAnswered = true;
                break;
```

- [ ] **Step 5: Fix the Task 6 auth test now that bootstrap exists**

`AuthSucceedsWithTheConfiguredIdentity` already disables every bootstrap toggle, so it still passes. Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add CCU-Emulator/CCU-Emulator/CCU_Emulator/Emulation CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests
git commit -m "Add CCU startup requests, device-time repeat and ping"
```

---

### Task 8: Action buttons

**Files:**
- Create: `EMU/CCU_Emulator/Emulation/CcuSession.Buttons.cs`
- Test: `EMU/CCU_Emulator.Tests/Emulation/CcuSessionButtonsTests.cs`

**Interfaces:**
- Consumes: `SendCcu`, `State`, `CcuPackets`, `MusicAction`, `VolumeAction`, `CallAction` (Tasks 4, 6, 7).
- Produces: `void Press(MusicAction)`, `void Press(VolumeAction)`, `bool Press(CallAction)` (false when no matching call), `void DismissNotification(int id)`, `Task DropConnectionAsync()`.

- [ ] **Step 1: Write the failing tests**

`EMU/CCU_Emulator.Tests/Emulation/CcuSessionButtonsTests.cs`:

```csharp
using CCU_Emulator.Protocol;
using CCU_Emulator.Tests.Support;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Emulation;

public class CcuSessionButtonsTests
{
    private readonly SessionFixture _f = new();

    [Theory]
    [InlineData(MusicAction.Next, "011701")]
    [InlineData(MusicAction.Previous, "011702")]
    [InlineData(MusicAction.Play, "011703")]
    [InlineData(MusicAction.Pause, "011704")]
    public void MusicButtons(MusicAction action, string expected)
    {
        _f.Session.Press(action);
        Assert.Equal(H(expected), Assert.Single(_f.Transport.MainRx));
    }

    [Theory]
    [InlineData(VolumeAction.PhoneUp, "011D01")]
    [InlineData(VolumeAction.PhoneDown, "011D02")]
    [InlineData(VolumeAction.MuteMicrophone, "011D03")]
    [InlineData(VolumeAction.MuteSpeaker, "011D04")]
    [InlineData(VolumeAction.MediaUp, "011D05")]
    [InlineData(VolumeAction.MediaDown, "011D06")]
    public void VolumeButtons(VolumeAction action, string expected)
    {
        _f.Session.Press(action);
        Assert.Equal(H(expected), Assert.Single(_f.Transport.MainRx));
    }

    [Fact]
    public void CallButtonsAreIgnoredWithNoCall()
    {
        Assert.False(_f.Session.Press(CallAction.Answer));
        Assert.False(_f.Session.Press(CallAction.Reject));
        Assert.False(_f.Session.Press(CallAction.HangUp));
        Assert.Empty(_f.Transport.MainRx);
    }

    [Fact]
    public void RingingCallCanBeAnsweredRejectedOrHungUp()
    {
        _f.Phone.SendDdt(Cat(H("0118"), Str8("Alice"), Str8("555"), H("00")));
        Assert.True(_f.Session.Press(CallAction.Answer));
        Assert.True(_f.Session.Press(CallAction.Reject));
        Assert.True(_f.Session.Press(CallAction.HangUp));
        Assert.Equal([H("011901"), H("011900"), H("011902")], _f.Transport.MainRx);
    }

    [Fact]
    public void ActiveCallAllowsOnlyHangUp()
    {
        _f.Phone.WriteSimple(H("0134 01"));
        Assert.False(_f.Session.Press(CallAction.Answer));
        Assert.False(_f.Session.Press(CallAction.Reject));
        Assert.True(_f.Session.Press(CallAction.HangUp));
        Assert.Equal([H("011902")], _f.Transport.MainRx);
    }

    [Fact]
    public void DismissingANotificationNotifiesThePhoneAndRemovesItLocally()
    {
        var proto = Cat(H("18 07 5A"), Str8("Hi"));
        _f.Phone.SendDdt(Cat(new PacketWriter().U16(0x012F).U24(proto.Length).ToArray(), proto));
        Assert.Single(_f.Session.State.Notifications);

        _f.Session.DismissNotification(7);
        Assert.Equal(H("0013 00000007"), Assert.Single(_f.Transport.MainRx));
        Assert.Empty(_f.Session.State.Notifications);
    }

    [Fact]
    public async Task DropConnectionDisconnectsTheTransport()
    {
        await _f.Session.DropConnectionAsync();
        Assert.Equal(1, _f.Transport.DisconnectCalls);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: build FAIL — `Press`, `DismissNotification`, `DropConnectionAsync` not found.

- [ ] **Step 3: Implement the buttons partial**

`EMU/CCU_Emulator/Emulation/CcuSession.Buttons.cs`:

```csharp
using CCU_Emulator.Protocol;

namespace CCU_Emulator.Emulation;

public sealed partial class CcuSession
{
    public void Press(MusicAction action) => SendCcu(CcuPackets.MusicControl(action), $"MusicControl ({action})");

    public void Press(VolumeAction action) => SendCcu(CcuPackets.VolumeControl(action), $"VolumeControl ({action})");

    /// <summary>Answer and Reject need a ringing call; HangUp needs a ringing or active call.</summary>
    public bool Press(CallAction action)
    {
        var allowed = action == CallAction.HangUp ? State.CallActive || State.CallRinging : State.CallRinging;
        if (!allowed)
        {
            Log.Info("Buttons", $"{action} ignored: no matching call");
            return false;
        }
        SendCcu(CcuPackets.CallControl(action), $"IncomingCallControl ({action})");
        return true;
    }

    /// <summary>The rider dismisses a notification on the dash: tell the phone and drop it locally.</summary>
    public void DismissNotification(int id)
    {
        SendCcu(CcuPackets.NotificationRemoved(id), "NotificationRemoved");
        State.RemoveNotification(id);
    }

    public Task DropConnectionAsync() => _transport.DisconnectAsync();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add CCU-Emulator/CCU-Emulator/CCU_Emulator/Emulation CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests
git commit -m "Add CCU action buttons"
```

---

### Task 9: Virtual Pit Board over the SPP link

**Files:**
- Create: `EMU/CCU_Emulator/Emulation/VpbReceiver.cs`, `EMU/CCU_Emulator/Emulation/CcuSession.Vpb.cs`
- Modify: `EMU/CCU_Emulator/Protocol/SppFrame.cs` (add `Reset`), `EMU/CCU_Emulator/Emulation/CcuSession.cs` (edits below)
- Test: `EMU/CCU_Emulator.Tests/Emulation/VpbReceiverTests.cs`, `EMU/CCU_Emulator.Tests/Emulation/CcuSessionVpbTests.cs`

**Interfaces:**
- Consumes: `VpbChunkMessage`, `VpbCrc32`, `ChunkResponse`, `SppFrame`, `SppFrameReader`, `SendCcu`, `CcuPackets` (Tasks 2–4, 7).
- Produces: `class VpbReceiver { (ChunkResponse Response, byte[]? Image) Accept(VpbChunkMessage chunk); void Reset(); bool InProgress; int ReceivedBytes; int TotalSize; }`; `SppFrameReader.Reset()`; session behavior: BLE connect → `ConnectSppAsync` (when `SppEnabled`); SPP up → sends the `0x013A` frame; VPB request/chunks/clear handled.

- [ ] **Step 1: Write the failing tests**

`EMU/CCU_Emulator.Tests/Emulation/VpbReceiverTests.cs`:

```csharp
using CCU_Emulator.Emulation;
using CCU_Emulator.Protocol;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Emulation;

public class VpbReceiverTests
{
    private readonly VpbReceiver _vpb = new();

    private static VpbChunkMessage Chunk(int contentId, int index, int totalIndex, int totalSize, byte[] data, bool badCrc = false)
        => new(contentId, (byte)index, (byte)totalIndex, totalSize, badCrc ? VpbCrc32.Compute(data) + 1 : VpbCrc32.Compute(data), data);

    [Fact]
    public void SingleChunkImageCompletes()
    {
        var (response, image) = _vpb.Accept(Chunk(1, 0, 0, 3, H("FFD8FF")));
        Assert.Equal(ChunkResponse.Ok, response);
        Assert.Equal(H("FFD8FF"), image);
        Assert.False(_vpb.InProgress);
    }

    [Fact]
    public void MultiChunkImageIsConcatenatedInOrder()
    {
        Assert.Equal((ChunkResponse.Ok, (byte[]?)null), _vpb.Accept(Chunk(1, 0, 2, 6, H("AABB"))));
        Assert.True(_vpb.InProgress);
        Assert.Equal(2, _vpb.ReceivedBytes);
        Assert.Equal(6, _vpb.TotalSize);
        Assert.Equal((ChunkResponse.Ok, (byte[]?)null), _vpb.Accept(Chunk(1, 1, 2, 6, H("CCDD"))));
        var (response, image) = _vpb.Accept(Chunk(1, 2, 2, 6, H("EEFF")));
        Assert.Equal(ChunkResponse.Ok, response);
        Assert.Equal(H("AABBCCDDEEFF"), image);
    }

    [Fact]
    public void BadCrcAsksForARetryAndKeepsState()
    {
        _vpb.Accept(Chunk(1, 0, 1, 4, H("AABB")));
        Assert.Equal(ChunkResponse.Retry, _vpb.Accept(Chunk(1, 1, 1, 4, H("CCDD"), badCrc: true)).Response);
        var (response, image) = _vpb.Accept(Chunk(1, 1, 1, 4, H("CCDD")));
        Assert.Equal(ChunkResponse.Ok, response);
        Assert.Equal(H("AABBCCDD"), image);
    }

    [Fact]
    public void ResendOfThePreviousChunkIsAcknowledgedWithoutDuplicatingData()
    {
        _vpb.Accept(Chunk(1, 0, 1, 4, H("AABB")));
        Assert.Equal(ChunkResponse.Ok, _vpb.Accept(Chunk(1, 0, 1, 4, H("AABB"))).Response);
        Assert.Equal(H("AABBCCDD"), _vpb.Accept(Chunk(1, 1, 1, 4, H("CCDD"))).Image);
    }

    [Fact]
    public void SkippedChunkAborts()
    {
        _vpb.Accept(Chunk(1, 0, 2, 6, H("AABB")));
        Assert.Equal(ChunkResponse.Abort, _vpb.Accept(Chunk(1, 2, 2, 6, H("EEFF"))).Response);
        Assert.False(_vpb.InProgress);
    }

    [Fact]
    public void ChunkOfAnUnknownImageThatIsNotTheFirstAborts()
        => Assert.Equal(ChunkResponse.Abort, _vpb.Accept(Chunk(9, 1, 2, 6, H("AABB"))).Response);

    [Fact]
    public void DataBeyondTheDeclaredTotalAborts()
    {
        _vpb.Accept(Chunk(1, 0, 1, 3, H("AABB")));
        Assert.Equal(ChunkResponse.Abort, _vpb.Accept(Chunk(1, 1, 1, 3, H("CCDD"))).Response);
    }

    [Fact]
    public void LastChunkWithTheWrongTotalAborts()
        => Assert.Equal(ChunkResponse.Abort, _vpb.Accept(Chunk(1, 0, 0, 5, H("AABB"))).Response);

    [Fact]
    public void NewContentIdRestartsTheTransfer()
    {
        _vpb.Accept(Chunk(1, 0, 2, 6, H("AABB")));
        var (response, image) = _vpb.Accept(Chunk(2, 0, 0, 2, H("1122")));
        Assert.Equal(ChunkResponse.Ok, response);
        Assert.Equal(H("1122"), image);
    }
}
```

`EMU/CCU_Emulator.Tests/Emulation/CcuSessionVpbTests.cs`:

```csharp
using CCU_Emulator.Protocol;
using CCU_Emulator.Tests.Support;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Emulation;

public class CcuSessionVpbTests
{
    private readonly SessionFixture _f = new();

    private static byte[] ChunkPacket(int contentId, int index, int totalIndex, int totalSize, byte[] data, bool badCrc = false)
        => new PacketWriter().U16(0x0235).I32(contentId).U8(index).I32(data.Length).U8(totalIndex).I32(totalSize)
            .U32(badCrc ? 0xDEADBEEF : VpbCrc32.Compute(data)).Bytes(data).ToArray();

    [Fact]
    public void BleConnectStartsTheSppLinkWhenEnabled()
    {
        _f.Phone.Connect();
        Assert.Equal(1, _f.Transport.SppConnectCalls);
    }

    [Fact]
    public void SppLinkIsNotStartedWhenDisabled()
    {
        _f.Settings.SppEnabled = false;
        _f.Phone.Connect();
        Assert.Equal(0, _f.Transport.SppConnectCalls);
    }

    [Fact]
    public void SppUpSendsTheBleConnectInformationFrame()
    {
        _f.Phone.Connect();
        _f.Transport.RaiseSpp(true);
        Assert.True(_f.Session.State.SppConnected);
        var expected = SppFrame.Encode(Cat(H("013A"), Ascii("01234567890123"), Ascii("012345")));
        Assert.Equal(expected, Assert.Single(_f.Transport.SppSent));
    }

    [Fact]
    public void RequestIsAcceptedWith0233()
    {
        _f.Phone.WriteSimple(H("0231"));
        Assert.Equal(H("0233"), Assert.Single(_f.Transport.MainRx));
        Assert.True(_f.Session.State.VpbReceiving);
    }

    [Fact]
    public void RequestIsRejectedWith0232WhenToggled()
    {
        _f.Settings.Behavior.RejectVpbRequests = true;
        _f.Phone.WriteSimple(H("0231"));
        Assert.Equal(H("0232"), Assert.Single(_f.Transport.MainRx));
        Assert.False(_f.Session.State.VpbReceiving);
    }

    [Fact]
    public void ChunksOverSppAreAckedOverBleAndTheImageIsStored()
    {
        _f.Phone.WriteSimple(H("0231"));
        _f.Phone.SendSpp(ChunkPacket(20101, 0, 1, 4, H("FFD8")));
        Assert.Equal(H("0236 00004E85 00 01"), _f.Transport.MainRx[^1]);
        Assert.Null(_f.Session.State.VpbImage);
        Assert.Equal(2, _f.Session.State.VpbReceivedBytes);
        Assert.Equal(4, _f.Session.State.VpbTotalBytes);

        _f.Phone.SendSpp(ChunkPacket(20101, 1, 1, 4, H("FFD9")));
        Assert.Equal(H("0236 00004E85 01 01"), _f.Transport.MainRx[^1]);
        Assert.Equal(H("FFD8FFD9"), _f.Session.State.VpbImage);
        Assert.False(_f.Session.State.VpbReceiving);
    }

    [Fact]
    public void BadCrcChunkGetsARetryAck()
    {
        _f.Phone.SendSpp(ChunkPacket(1, 0, 0, 2, H("FFD8"), badCrc: true));
        Assert.Equal(H("0236 00000001 00 02"), _f.Transport.MainRx[^1]);
        Assert.Null(_f.Session.State.VpbImage);
    }

    [Fact]
    public void SppFramesSplitAcrossReadsAreReassembled()
    {
        var framed = SppFrame.Encode(ChunkPacket(1, 0, 0, 2, H("FFD8")));
        foreach (var b in framed) _f.Transport.RaiseData(CCU_Emulator.Transport.CcuChannel.Spp, [b]);
        Assert.Equal(H("FFD8"), _f.Session.State.VpbImage);
    }

    [Fact]
    public void ClearRemovesTheImageAndAnswers0238()
    {
        _f.Phone.SendSpp(ChunkPacket(1, 0, 0, 2, H("FFD8")));
        _f.Phone.WriteSimple(H("0237"));
        Assert.Null(_f.Session.State.VpbImage);
        Assert.Equal(H("0238"), _f.Transport.MainRx[^1]);
    }

    // Review Focus 3: a corrupt header on the SPP stream must not hang or allocate the claimed size.
    [Fact]
    public void CorruptSppHeaderIsSkipped()
    {
        _f.Transport.RaiseData(CCU_Emulator.Transport.CcuChannel.Spp, H("01 FFFFFFFF"));
        _f.Phone.SendSpp(ChunkPacket(1, 0, 0, 2, H("FFD8")));
        Assert.Equal(H("FFD8"), _f.Session.State.VpbImage);
    }

    // Review Focus 5: dropping the link mid-image starts the next one clean.
    [Fact]
    public void SppDropMidImageResetsTheTransfer()
    {
        _f.Phone.SendSpp(ChunkPacket(1, 0, 2, 6, H("AABB")));
        Assert.True(_f.Session.State.VpbReceiving);

        _f.Transport.RaiseSpp(false);
        Assert.False(_f.Session.State.VpbReceiving);

        _f.Phone.SendSpp(ChunkPacket(1, 1, 2, 6, H("CCDD")));
        Assert.Equal(H("0236 00000001 01 03"), _f.Transport.MainRx[^1]);   // abort: this image never started
    }

    [Fact]
    public void BleDropMidImageResetsTheTransfer()
    {
        _f.Phone.Connect();
        _f.Phone.SendSpp(ChunkPacket(1, 0, 2, 6, H("AABB")));
        _f.Transport.RaiseBle(false);
        Assert.False(_f.Session.State.VpbReceiving);
        Assert.Equal(0, _f.Session.State.VpbReceivedBytes);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: build FAIL — `VpbReceiver` not found.

- [ ] **Step 3: Implement `VpbReceiver`**

`EMU/CCU_Emulator/Emulation/VpbReceiver.cs`:

```csharp
using CCU_Emulator.Protocol;

namespace CCU_Emulator.Emulation;

/// <summary>Reassembles a Virtual Pit Board image from CRC-checked, in-order chunks.</summary>
public sealed class VpbReceiver
{
    private MemoryStream? _image;
    private int _contentId;
    private int _expectedIndex;
    private int _totalSize;

    public bool InProgress => _image != null;
    public int ReceivedBytes => (int)(_image?.Length ?? 0);
    public int TotalSize => _totalSize;

    public void Reset()
    {
        _image = null;
        _expectedIndex = 0;
        _totalSize = 0;
    }

    /// <summary>Returns the ack to send and, when the last chunk completes a valid image, the image bytes.</summary>
    public (ChunkResponse Response, byte[]? Image) Accept(VpbChunkMessage chunk)
    {
        if (!chunk.CrcOk) return (ChunkResponse.Retry, null);

        if (_image is null || chunk.ContentId != _contentId)
        {
            if (chunk.Index != 0) { Reset(); return (ChunkResponse.Abort, null); }
            _image = new MemoryStream();
            _contentId = chunk.ContentId;
            _expectedIndex = 0;
            _totalSize = chunk.TotalSize;
        }

        if (chunk.TotalSize != _totalSize) { Reset(); return (ChunkResponse.Abort, null); }
        if (chunk.Index == _expectedIndex - 1) return (ChunkResponse.Ok, null);   // resend of the last chunk
        if (chunk.Index != _expectedIndex) { Reset(); return (ChunkResponse.Abort, null); }
        if (_image.Length + chunk.Data.Length > _totalSize) { Reset(); return (ChunkResponse.Abort, null); }

        _image.Write(chunk.Data);
        _expectedIndex++;

        if (chunk.Index != chunk.TotalIndex) return (ChunkResponse.Ok, null);

        var complete = _image.Length == _totalSize;
        var image = _image.ToArray();
        Reset();
        return complete ? (ChunkResponse.Ok, image) : (ChunkResponse.Abort, null);
    }
}
```

- [ ] **Step 4: Add `Reset` to the SPP frame reader**

In `EMU/CCU_Emulator/Protocol/SppFrame.cs`, inside `SppFrameReader`, directly above `public List<byte[]> Feed(`, add:

```csharp
    /// <summary>Discards any partial frame (link dropped).</summary>
    public void Reset() => _buf.Clear();

```

- [ ] **Step 5: Implement the VPB/SPP partial**

`EMU/CCU_Emulator/Emulation/CcuSession.Vpb.cs`:

```csharp
using CCU_Emulator.Protocol;

namespace CCU_Emulator.Emulation;

public sealed partial class CcuSession
{
    private readonly SppFrameReader _spp = new();
    private readonly VpbReceiver _vpb = new();

    private void OnSppConnection(bool up)
    {
        State.SppConnected = up;
        Log.Info("SPP", up ? "classic link up" : "classic link down");
        _spp.Reset();
        if (up)
        {
            SendSpp(SppFrame.Encode(CcuPackets.BleConnectInformation(Settings.CcuId, Settings.PassKey)), "BleConnectInformation");
        }
        else
        {
            ResetVpb();
        }
    }

    private async Task ConnectSppAsync()
    {
        try { await _transport.ConnectSppAsync(); }
        catch (Exception e) { _post(() => Log.Info("SPP", $"connect failed: {e.Message}")); }
    }

    private void SendSpp(byte[] framed, string name)
    {
        Log.Add(Direction.Tx, "Spp", name, framed);
        _ = SendSppAsync(framed);
    }

    private async Task SendSppAsync(byte[] framed)
    {
        try { await _transport.SendSppAsync(framed); }
        catch (Exception e) { _post(() => Log.Info("SPP", $"send failed: {e.Message}")); }
    }

    private void ResetVpb()
    {
        _vpb.Reset();
        State.VpbReceiving = false;
        State.VpbReceivedBytes = 0;
        State.VpbTotalBytes = 0;
    }

    private void HandleVpbRequest()
    {
        if (Settings.Behavior.RejectVpbRequests)
        {
            SendCcu(CcuPackets.VpbResponseNg(), "VpbResponseNg");
            return;
        }
        ResetVpb();
        State.VpbReceiving = true;
        SendCcu(CcuPackets.VpbGraphicRequest(), "VpbGraphicRequest");
    }

    private void HandleVpbChunk(VpbChunkMessage chunk)
    {
        var (response, image) = _vpb.Accept(chunk);
        SendCcu(CcuPackets.VpbChunkAck(chunk.ContentId, chunk.Index, response), $"VpbChunkAck ({response})");

        State.VpbReceiving = _vpb.InProgress;
        State.VpbReceivedBytes = _vpb.ReceivedBytes;
        State.VpbTotalBytes = _vpb.TotalSize;
        if (image is not null)
        {
            State.VpbImage = image;
            Log.Info("VPB", $"image complete ({image.Length} bytes)");
        }
    }

    private void HandleVpbClear()
    {
        ResetVpb();
        State.VpbImage = null;
        SendCcu(CcuPackets.VpbClearOk(), "VpbClearOk");
    }
}
```

- [ ] **Step 6: Hook it into the core**

In `EMU/CCU_Emulator/Emulation/CcuSession.cs`:

(a) In the constructor, directly after the line `_transport.BleConnectionChanged += up => _post(() => OnBleConnection(up));` add:

```csharp
        _transport.SppConnectionChanged += up => _post(() => OnSppConnection(up));
```

(b) In `OnData`, replace the `default:` line with:

```csharp
            case CcuChannel.Spp:
                foreach (var frame in _spp.Feed(value)) HandleMessage(frame, "SPP");
                break;
            default: Log.Info("Transport", $"unexpected data on {channel}"); break;
```

(c) In `OnBleConnection`, replace `if (!connected) ResetConnectionState();` with:

```csharp
        if (connected)
        {
            if (Settings.SppEnabled) _ = ConnectSppAsync();
        }
        else
        {
            ResetConnectionState();
        }
```

(d) In `ResetConnectionState`, after `State.CallActive = false;` add:

```csharp
        ResetVpb();
        _spp.Reset();
```

(e) In `Apply`, before the `// Logged by HandleMessage; nothing to track.` comment add:

```csharp
            case VpbRequestMessage:
                HandleVpbRequest();
                break;

            case VpbChunkMessage c:
                HandleVpbChunk(c);
                break;

            case VpbClearMessage:
                HandleVpbClear();
                break;

```

and add `case VpbGraphicResponseNgMessage:` to the "nothing to track" group.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: all PASS. Check `GarbageIsLoggedAndIgnored("0235 00000001")` from Task 6 still passes (truncated VPB chunk is Malformed).

- [ ] **Step 8: Commit**

```bash
git add CCU-Emulator/CCU-Emulator/CCU_Emulator CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests
git commit -m "Add Virtual Pit Board handling over the SPP link"
```

---

### Task 10: Emulator host, transport factory, loopback end-to-end test

**Files:**
- Create: `EMU/CCU_Emulator/Transport/TransportFactory.cs`, `EMU/CCU_Emulator/Emulation/EmulatorHost.cs`
- Test: `EMU/CCU_Emulator.Tests/Emulation/EmulatorHostTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - `static class TransportFactory { static bool IsAvailable(TransportKind); static ICcuTransport Create(TransportKind); }`
  - `class EmulatorHost : IAsyncDisposable`: ctor `EmulatorHost(CcuSettings settings, string settingsPath, Action<Action>? post = null, Func<TransportKind, ICcuTransport>? transportFactory = null)`; `Settings`, `Transport`, `Session`, `bool IsRunning`, `string? LastError`; `Task StartAsync()`, `Task StopAsync()`, `void SaveSettings()`. While running it calls `Session.Tick(1s)` once a second through `post`.

- [ ] **Step 1: Write the failing test**

`EMU/CCU_Emulator.Tests/Emulation/EmulatorHostTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using CCU_Emulator.Emulation;
using CCU_Emulator.Protocol;
using CCU_Emulator.Transport;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.Emulation;

public class EmulatorHostTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static CcuSettings Settings() => new() { CcuId = "01234567890123", PassKey = "012345", TcpPort = 0 };

    /// <summary>Tiny scripted phone speaking the loopback framing.</summary>
    private sealed class ScriptedPhone(TcpClient client)
    {
        private readonly NetworkStream _stream = client.GetStream();
        private byte _reqId = 1;

        public async Task WriteAsync(CcuChannel role, byte[] data)
            => await _stream.WriteAsync(Cat([(byte)role, (byte)(data.Length >> 8), (byte)data.Length], data));

        public async Task SendDdtAsync(byte[] message)
        {
            var id = _reqId++;
            await WriteAsync(CcuChannel.DdtControl, [id, 0, (byte)(message.Length >> 8), (byte)message.Length]);
            await WriteAsync(CcuChannel.DdtData, Cat([id, 0, 0, 0], message));
        }

        public async Task<(CcuChannel Role, byte[] Data)> ReadFrameAsync()
        {
            var header = new byte[3];
            await _stream.ReadExactlyAsync(header).AsTask().WaitAsync(Timeout);
            var data = new byte[(header[1] << 8) | header[2]];
            await _stream.ReadExactlyAsync(data).AsTask().WaitAsync(Timeout);
            return ((CcuChannel)header[0], data);
        }

        public async Task<byte[]> ReadMainRxAsync()
        {
            while (true)
            {
                var (role, data) = await ReadFrameAsync();
                if (role == CcuChannel.MainRx) return data;
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task ScriptedPhoneAuthenticatesAndDrivesTheEmulatorOverLoopback()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccu-host-{Guid.NewGuid():N}.json");
        await using var host = new EmulatorHost(Settings(), path);
        await host.StartAsync();
        Assert.True(host.IsRunning);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((LoopbackTcpTransport)host.Transport).Port);
        var phone = new ScriptedPhone(client);

        await phone.SendDdtAsync(Cat(H("0101"), Ascii("01234567890123"), Ascii("012345")));
        Assert.Equal(H("0102 01"), await phone.ReadMainRxAsync());
        Assert.Equal(H("0001 030101"), await phone.ReadMainRxAsync());   // LibVersion follows auth

        await phone.SendDdtAsync(Cat(H("0116"), Str8("Artist"), Str8("Album"), Str8("Song"), H("0000D2 02 000A 00000F")));
        await WaitUntilAsync(() => host.Session.State.MusicSong == "Song");

        host.Session.Press(MusicAction.Next);
        // Drain until the music-control frame appears (startup requests arrive first).
        byte[] frame;
        do { frame = await phone.ReadMainRxAsync(); } while (!frame.SequenceEqual(H("0117 01")));
    }

    [Fact]
    public async Task DisconnectMidSessionResetsAuthentication()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccu-host-{Guid.NewGuid():N}.json");
        await using var host = new EmulatorHost(Settings(), path);
        await host.StartAsync();

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, ((LoopbackTcpTransport)host.Transport).Port);
            var phone = new ScriptedPhone(client);
            await phone.SendDdtAsync(Cat(H("0101"), Ascii("01234567890123"), Ascii("012345")));
            await WaitUntilAsync(() => host.Session.State.Authenticated);
        }

        await WaitUntilAsync(() => !host.Session.State.BleConnected);
        Assert.False(host.Session.State.Authenticated);
    }

    [Fact]
    public async Task InvalidIdentityRefusesToStart()
    {
        var settings = Settings();
        settings.CcuId = "short";
        await using var host = new EmulatorHost(settings, Path.GetTempFileName());
        await host.StartAsync();
        Assert.False(host.IsRunning);
        Assert.Contains("CcuId", host.LastError);
    }

    [Fact]
    public void LoopbackTransportIsAlwaysAvailable()
    {
        Assert.True(TransportFactory.IsAvailable(TransportKind.LoopbackTcp));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: build FAIL — `EmulatorHost`, `TransportFactory` not found.

- [ ] **Step 3: Implement the factory and host**

`EMU/CCU_Emulator/Transport/TransportFactory.cs`:

```csharp
using CCU_Emulator.Emulation;

namespace CCU_Emulator.Transport;

public static class TransportFactory
{
    public static bool IsAvailable(TransportKind kind) => kind switch
    {
        TransportKind.LoopbackTcp => true,
        _ => false,
    };

    public static ICcuTransport Create(TransportKind kind) => kind switch
    {
        TransportKind.LoopbackTcp => new LoopbackTcpTransport(),
        _ => throw new PlatformNotSupportedException($"Transport {kind} is not available in this build."),
    };
}
```

`EMU/CCU_Emulator/Emulation/EmulatorHost.cs`:

```csharp
using CCU_Emulator.Transport;

namespace CCU_Emulator.Emulation;

/// <summary>Owns the settings, the chosen transport and the session, and drives the session clock.</summary>
public sealed class EmulatorHost : IAsyncDisposable
{
    private readonly Action<Action> _post;
    private CancellationTokenSource? _tickCts;

    public string SettingsPath { get; }
    public CcuSettings Settings { get; }
    public ICcuTransport Transport { get; }
    public CcuSession Session { get; }
    public bool IsRunning => Transport.IsRunning;

    /// <summary>Why the last <see cref="StartAsync"/> did not start (null when it did).</summary>
    public string? LastError { get; private set; }

    public EmulatorHost(
        CcuSettings settings,
        string settingsPath,
        Action<Action>? post = null,
        Func<TransportKind, ICcuTransport>? transportFactory = null)
    {
        Settings = settings;
        SettingsPath = settingsPath;
        _post = post ?? (a => a());
        Transport = (transportFactory ?? TransportFactory.Create)(settings.Transport);
        Session = new CcuSession(Transport, settings, _post);
    }

    public async Task StartAsync()
    {
        LastError = null;
        var errors = Settings.Validate();
        if (errors.Count > 0)
        {
            LastError = string.Join(" ", errors);
            _post(() => Session.Log.Info("Host", "not started: " + LastError));
            return;
        }

        try
        {
            await Transport.StartAsync(Settings);
        }
        catch (Exception e)
        {
            LastError = e.Message;
            _post(() => Session.Log.Info("Host", "transport failed to start: " + e.Message));
            return;
        }

        _tickCts = new CancellationTokenSource();
        _ = TickLoopAsync(_tickCts.Token);
        _post(() => Session.Log.Info("Host", $"started as {Settings.AdvertisedName} on {Settings.Transport}"));
    }

    public async Task StopAsync()
    {
        _tickCts?.Cancel();
        _tickCts = null;
        await Transport.StopAsync();
        _post(() => Session.Log.Info("Host", "stopped"));
    }

    public void SaveSettings() => SettingsStore.Save(SettingsPath, Settings);

    private async Task TickLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                _post(() => Session.Tick(TimeSpan.FromSeconds(1)));
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await Transport.DisposeAsync();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add CCU-Emulator/CCU-Emulator/CCU_Emulator CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests
git commit -m "Add emulator host, transport factory and loopback end-to-end test"
```

---

### Task 11: View models and app wiring

**Files:**
- Create: `EMU/CCU_Emulator/ViewModels/DashViewModel.cs`, `ConnectionViewModel.cs`, `ButtonsViewModel.cs`, `BehaviorViewModel.cs`, `NotificationsViewModel.cs`, `LogViewModel.cs`
- Modify (replace): `EMU/CCU_Emulator/ViewModels/MainViewModel.cs`, `EMU/CCU_Emulator/Views/MainView.axaml`, `EMU/CCU_Emulator/Views/MainWindow.axaml`, `EMU/CCU_Emulator/App.axaml.cs`
- Test: `EMU/CCU_Emulator.Tests/ViewModels/DashViewModelTests.cs`, `ButtonsViewModelTests.cs`, `LogViewModelTests.cs`, `ConnectionViewModelTests.cs`

**Interfaces:**
- Consumes: `CcuState`, `CcuSession` (`Press`, `DismissNotification`, `DropConnectionAsync`), `EmulatorHost`, `PacketLog`, `CcuBehavior`, `TransportFactory` (Tasks 5–10). `ViewModelBase` is the template's `ObservableObject` subclass.
- Produces (all in `CCU_Emulator.ViewModels`, the `Views/` task binds to these names):
  - `DashViewModel(CcuState)`: `bool ShowCall, ShowVpb, ShowIdle`; `byte[]? VpbImageBytes`; `string ClockText, StatusText, PlaybackText, MusicTitle, MusicSubtitle, WeatherText, CallTitle, CallerLine, VpbProgressText, BannerTitle, BannerBody`; `double MusicProgress` (0–100); `bool VpbReceiving`; `bool BannerVisible` (settable); `static TimeSpan BannerDuration` (default 8 s); `DismissBannerCommand`.
  - `ConnectionViewModel(EmulatorHost)`: `AdvertisedName, TransportText, LinkText, SppText, LastError`; `bool Running`; `StartCommand`, `StopCommand` (async), `DropCommand`.
  - `ButtonsViewModel(CcuSession)`: `bool CanAnswerOrReject, CanHangUp`; commands `PreviousCommand, NextCommand, PlayCommand, PauseCommand, PhoneVolumeUpCommand, PhoneVolumeDownCommand, MediaVolumeUpCommand, MediaVolumeDownCommand, MuteMicrophoneCommand, MuteSpeakerCommand, AnswerCommand, RejectCommand, HangUpCommand`.
  - `BehaviorViewModel(CcuBehavior)`: `CcuBehavior Behavior`.
  - `NotificationsViewModel(CcuState, CcuSession)`: `ObservableCollection<NotificationEntry> Items`; `DismissCommand` (parameter `NotificationEntry`).
  - `LogViewModel(PacketLog)`: `string FilterText`; `ObservableCollection<PacketLogEntry> Visible`; `ClearCommand`; `string ExportText()`.
  - `MainViewModel(EmulatorHost)`: `Dash, Connection, Buttons, Behavior, Notifications, Log`; `string Title`.

- [ ] **Step 1: Write the failing tests**

`EMU/CCU_Emulator.Tests/ViewModels/DashViewModelTests.cs`:

```csharp
using CCU_Emulator.Emulation;
using CCU_Emulator.Protocol;
using CCU_Emulator.ViewModels;
using Xunit;

namespace CCU_Emulator.Tests.ViewModels;

public class DashViewModelTests
{
    private readonly CcuState _state = new();
    private DashViewModel Create() => new(_state);

    [Fact]
    public void StartsOnTheIdleScreen()
    {
        var vm = Create();
        Assert.True(vm.ShowIdle);
        Assert.False(vm.ShowCall);
        Assert.False(vm.ShowVpb);
        Assert.Equal("No music", vm.MusicTitle);
        Assert.Equal("No weather", vm.WeatherText);
        Assert.Equal("--:--", vm.ClockText);
    }

    [Fact]
    public void ScreenPriorityIsCallThenVpbThenIdle()
    {
        var vm = Create();
        _state.VpbImage = [1, 2, 3];
        Assert.True(vm.ShowVpb);
        Assert.False(vm.ShowIdle);

        _state.CallRinging = true;
        Assert.True(vm.ShowCall);
        Assert.False(vm.ShowVpb);

        _state.CallRinging = false;
        _state.CallActive = true;
        Assert.True(vm.ShowCall);

        _state.CallActive = false;
        _state.VpbImage = null;
        Assert.True(vm.ShowIdle);
    }

    [Fact]
    public void MusicAndWeatherAndClockText()
    {
        var vm = Create();
        _state.MusicSong = "Song";
        _state.MusicArtist = "Artist";
        _state.MusicAlbum = "Album";
        _state.MusicDurationSeconds = 200;
        _state.MusicElapsedSeconds = 50;
        _state.MusicPlayback = PlaybackState.Playing;
        _state.UpsertWeather(new WeatherEntry(WeatherSlot.Current, WeatherType.Rain, 12.5, 14, 9, 40));
        _state.PhoneTime = new DateTime(2026, 10, 10, 9, 5, 0);

        Assert.Equal("Song", vm.MusicTitle);
        Assert.Equal("Artist — Album", vm.MusicSubtitle);
        Assert.Equal(25.0, vm.MusicProgress);
        Assert.Equal("▶", vm.PlaybackText);
        Assert.Contains("12.5", vm.WeatherText);
        Assert.Contains("Rain", vm.WeatherText);
        Assert.Equal("09:05", vm.ClockText);
    }

    [Fact]
    public void ProgressIsClampedAndSafeWithNoDuration()
    {
        var vm = Create();
        _state.MusicDurationSeconds = 0;
        _state.MusicElapsedSeconds = 30;
        Assert.Equal(0, vm.MusicProgress);
        _state.MusicDurationSeconds = 10;
        Assert.Equal(100, vm.MusicProgress);
    }

    [Fact]
    public void ChangeNotificationsFireWhenStateChanges()
    {
        var vm = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        _state.CallRinging = true;
        Assert.True(changed.Contains(nameof(DashViewModel.ShowCall)) || changed.Contains(string.Empty));
    }

    [Fact]
    public async Task BannerShowsForANewNotificationThenHides()
    {
        DashViewModel.BannerDuration = TimeSpan.FromMilliseconds(30);
        try
        {
            var vm = Create();
            _state.AddNotification(new NotificationEntry(1, 0, null, "Title", "", "Body"));
            Assert.True(vm.BannerVisible);
            Assert.Equal("Title", vm.BannerTitle);
            Assert.Equal("Body", vm.BannerBody);

            await Task.Delay(300);
            Assert.False(vm.BannerVisible);
        }
        finally
        {
            DashViewModel.BannerDuration = TimeSpan.FromSeconds(8);
        }
    }

    [Fact]
    public void BannerCanBeDismissed()
    {
        var vm = Create();
        _state.AddNotification(new NotificationEntry(1, 0, null, "T", "", "B"));
        vm.DismissBannerCommand.Execute(null);
        Assert.False(vm.BannerVisible);
    }
}
```

`EMU/CCU_Emulator.Tests/ViewModels/ButtonsViewModelTests.cs`:

```csharp
using CCU_Emulator.Tests.Support;
using CCU_Emulator.ViewModels;
using Xunit;
using static CCU_Emulator.Tests.Support.Bytes;

namespace CCU_Emulator.Tests.ViewModels;

public class ButtonsViewModelTests
{
    private readonly SessionFixture _f = new();

    [Fact]
    public void MusicAndVolumeCommandsSendTheirPackets()
    {
        var vm = new ButtonsViewModel(_f.Session);
        vm.NextCommand.Execute(null);
        vm.PreviousCommand.Execute(null);
        vm.PlayCommand.Execute(null);
        vm.PauseCommand.Execute(null);
        vm.MediaVolumeUpCommand.Execute(null);
        vm.MuteSpeakerCommand.Execute(null);
        Assert.Equal(
            [H("011701"), H("011702"), H("011703"), H("011704"), H("011D05"), H("011D04")],
            _f.Transport.MainRx);
    }

    [Fact]
    public void CallButtonsFollowTheCallState()
    {
        var vm = new ButtonsViewModel(_f.Session);
        Assert.False(vm.CanAnswerOrReject);
        Assert.False(vm.CanHangUp);

        _f.Phone.SendDdt(Cat(H("0118"), Str8("Alice"), Str8("555"), H("00")));
        Assert.True(vm.CanAnswerOrReject);
        Assert.True(vm.CanHangUp);

        vm.AnswerCommand.Execute(null);
        Assert.Equal(H("011901"), _f.Transport.MainRx[^1]);

        _f.Phone.WriteSimple(H("0134 01"));
        Assert.False(vm.CanAnswerOrReject);
        Assert.True(vm.CanHangUp);
    }
}
```

`EMU/CCU_Emulator.Tests/ViewModels/LogViewModelTests.cs`:

```csharp
using CCU_Emulator.Emulation;
using CCU_Emulator.ViewModels;
using Xunit;

namespace CCU_Emulator.Tests.ViewModels;

public class LogViewModelTests
{
    [Fact]
    public void ShowsAddedEntriesAndFiltersCaseInsensitively()
    {
        var log = new PacketLog();
        var vm = new LogViewModel(log);

        log.Add(Direction.Rx, "DDT", "MusicMetaData", [1, 2], "Song = Hello");
        log.Add(Direction.Tx, "MainRx", "LanguageRequest", [0, 0x0B]);
        Assert.Equal(2, vm.Visible.Count);

        vm.FilterText = "hello";
        Assert.Single(vm.Visible);
        Assert.Equal("MusicMetaData", vm.Visible[0].Name);

        log.Add(Direction.Rx, "DDT", "Other", [3], "no match");
        Assert.Single(vm.Visible);
        log.Add(Direction.Rx, "DDT", "Another", [3], "HELLO again");
        Assert.Equal(2, vm.Visible.Count);

        vm.FilterText = "";
        Assert.Equal(4, vm.Visible.Count);
    }

    [Fact]
    public void ClearEmptiesBothLists()
    {
        var log = new PacketLog();
        var vm = new LogViewModel(log);
        log.Info("Host", "started");
        vm.ClearCommand.Execute(null);
        Assert.Empty(log.Entries);
        Assert.Empty(vm.Visible);
    }

    [Fact]
    public void ExportTextHasOneLinePerVisibleEntry()
    {
        var log = new PacketLog();
        var vm = new LogViewModel(log);
        log.Add(Direction.Tx, "MainRx", "Ping", [1, 0x35, 0]);
        log.Info("Host", "note");
        var lines = vm.ExportText().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("01 35 00", lines[0]);
    }

    [Fact]
    public void TrimmedEntriesDisappearFromTheVisibleList()
    {
        var log = new PacketLog();
        var vm = new LogViewModel(log);
        for (var i = 0; i < PacketLog.Capacity + 5; i++) log.Info("t", i.ToString());
        Assert.Equal(PacketLog.Capacity, log.Entries.Count);
        Assert.Equal(PacketLog.Capacity, vm.Visible.Count);
    }
}
```

`EMU/CCU_Emulator.Tests/ViewModels/ConnectionViewModelTests.cs`:

```csharp
using CCU_Emulator.Emulation;
using CCU_Emulator.Tests.Support;
using CCU_Emulator.ViewModels;
using Xunit;

namespace CCU_Emulator.Tests.ViewModels;

public class ConnectionViewModelTests
{
    private static (EmulatorHost host, FakeTransport transport) CreateHost(CcuSettings? settings = null)
    {
        var transport = new FakeTransport();
        var host = new EmulatorHost(settings ?? new CcuSettings(), Path.GetTempFileName(), null, _ => transport);
        return (host, transport);
    }

    [Fact]
    public async Task StartAndStopToggleRunning()
    {
        var (host, transport) = CreateHost();
        var vm = new ConnectionViewModel(host);
        Assert.Equal("Stopped", vm.LinkText);

        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.Running);
        Assert.True(transport.IsRunning);
        Assert.Equal("Advertising / waiting for a phone", vm.LinkText);

        transport.RaiseBle(true);
        Assert.Equal("Phone connected (not authenticated)", vm.LinkText);

        await vm.StopCommand.ExecuteAsync(null);
        Assert.False(vm.Running);
    }

    [Fact]
    public async Task StartWithAnInvalidIdentityShowsTheError()
    {
        var (host, _) = CreateHost(new CcuSettings { CcuId = "bad" });
        var vm = new ConnectionViewModel(host);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.False(vm.Running);
        Assert.Contains("CcuId", vm.LastError);
    }

    [Fact]
    public async Task DropDisconnectsThePhone()
    {
        var (host, transport) = CreateHost();
        var vm = new ConnectionViewModel(host);
        await vm.DropCommand.ExecuteAsync(null);
        Assert.Equal(1, transport.DisconnectCalls);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: build FAIL — the view model types do not exist (and `MainViewModel` has no matching constructor).

- [ ] **Step 3: Implement the dash view model**

`EMU/CCU_Emulator/ViewModels/DashViewModel.cs`:

```csharp
using System.Globalization;
using CCU_Emulator.Emulation;
using CCU_Emulator.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCU_Emulator.ViewModels;

/// <summary>What the emulated dash shows. Priority: call, then VPB image, then the idle screen.</summary>
public sealed partial class DashViewModel : ViewModelBase
{
    /// <summary>How long a notification banner stays on screen.</summary>
    public static TimeSpan BannerDuration { get; set; } = TimeSpan.FromSeconds(8);

    private int _bannerToken;

    public CcuState State { get; }

    [ObservableProperty] public partial bool BannerVisible { get; set; }

    public DashViewModel(CcuState state)
    {
        State = state;
        State.PropertyChanged += (_, e) =>
        {
            OnPropertyChanged(string.Empty);   // every derived property reads State; refresh them all
            if (e.PropertyName == nameof(CcuState.LatestNotification) && State.LatestNotification is not null)
            {
                BannerVisible = true;
                _ = HideBannerLaterAsync();
            }
        };
        State.Weather.CollectionChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    private async Task HideBannerLaterAsync()
    {
        var token = ++_bannerToken;
        await Task.Delay(BannerDuration);
        if (token == _bannerToken) BannerVisible = false;
    }

    [RelayCommand]
    private void DismissBanner()
    {
        _bannerToken++;
        BannerVisible = false;
    }

    // Screen selection
    public bool ShowCall => State.CallRinging || State.CallActive;
    public bool ShowVpb => !ShowCall && State.VpbImage is not null;
    public bool ShowIdle => !ShowCall && State.VpbImage is null;
    public byte[]? VpbImageBytes => State.VpbImage;
    public bool VpbReceiving => State.VpbReceiving;
    public string VpbProgressText => State.VpbReceiving ? $"Receiving image {State.VpbReceivedBytes}/{State.VpbTotalBytes} bytes" : "";

    // Call
    public string CallTitle => State.CallRinging ? "Incoming call" : "In call";
    public string CallerLine => !string.IsNullOrEmpty(State.CallerName) ? $"{State.CallerName}  {State.CallerNumber}".Trim() : State.CallerNumber;

    // Idle screen
    public string ClockText => State.PhoneTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "--:--";

    public string StatusText
        => $"{State.Signal} · {(State.HeadsetConnected ? "Headset" : "No headset")} · {State.Battery}%{(State.Charging ? " ⚡" : "")}";

    public string PlaybackText => State.MusicPlayback switch
    {
        PlaybackState.Playing => "▶",
        PlaybackState.Paused => "⏸",
        _ => "■",
    };

    public string MusicTitle => string.IsNullOrEmpty(State.MusicSong) ? "No music" : State.MusicSong;

    public string MusicSubtitle => string.IsNullOrEmpty(State.MusicSong) ? "" : $"{State.MusicArtist} — {State.MusicAlbum}";

    public double MusicProgress
        => State.MusicDurationSeconds <= 0 ? 0 : Math.Clamp(100.0 * State.MusicElapsedSeconds / State.MusicDurationSeconds, 0, 100);

    public string WeatherText
    {
        get
        {
            var current = State.Weather.FirstOrDefault(w => w.Slot == WeatherSlot.Current);
            if (current is null) return "No weather";
            var place = string.IsNullOrEmpty(State.WeatherLocation) ? "" : $"{State.WeatherLocation}: ";
            return $"{place}{current.Temperature.ToString("0.#", CultureInfo.InvariantCulture)}°C {current.Type} ({current.PrecipitationPercent}%)";
        }
    }

    // Notification banner
    public string BannerTitle => State.LatestNotification?.Title ?? "";
    public string BannerBody => State.LatestNotification?.Detail ?? "";
}
```

- [ ] **Step 4: Implement the remaining view models**

`EMU/CCU_Emulator/ViewModels/ConnectionViewModel.cs`:

```csharp
using CCU_Emulator.Emulation;
using CCU_Emulator.Transport;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCU_Emulator.ViewModels;

public sealed partial class ConnectionViewModel : ViewModelBase
{
    private readonly EmulatorHost _host;

    public ConnectionViewModel(EmulatorHost host)
    {
        _host = host;
        host.Session.State.PropertyChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    private CcuState State => _host.Session.State;

    public string AdvertisedName => _host.Settings.AdvertisedName;

    public string TransportText
        => _host.Settings.Transport + (TransportFactory.IsAvailable(_host.Settings.Transport) ? "" : " (not available in this build)");

    [ObservableProperty] public partial bool Running { get; set; }
    [ObservableProperty] public partial string LastError { get; set; } = "";

    partial void OnRunningChanged(bool value) => OnPropertyChanged(nameof(LinkText));

    public string LinkText
        => !Running ? "Stopped"
        : !State.BleConnected ? "Advertising / waiting for a phone"
        : State.Authenticated ? "Phone connected and authenticated"
        : "Phone connected (not authenticated)";

    public string SppText => State.SppConnected ? "Classic link up" : "Classic link down";

    [RelayCommand]
    private async Task StartAsync()
    {
        await _host.StartAsync();
        Running = _host.IsRunning;
        LastError = _host.LastError ?? "";
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await _host.StopAsync();
        Running = _host.IsRunning;
    }

    [RelayCommand]
    private Task DropAsync() => _host.Session.DropConnectionAsync();
}
```

`EMU/CCU_Emulator/ViewModels/ButtonsViewModel.cs`:

```csharp
using CCU_Emulator.Emulation;
using CCU_Emulator.Protocol;
using CommunityToolkit.Mvvm.Input;

namespace CCU_Emulator.ViewModels;

/// <summary>The action-specific "handlebar" buttons. Each one sends one CCU→phone command.</summary>
public sealed partial class ButtonsViewModel : ViewModelBase
{
    private readonly CcuSession _session;

    public ButtonsViewModel(CcuSession session)
    {
        _session = session;
        session.State.PropertyChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    public bool CanAnswerOrReject => _session.State.CallRinging;
    public bool CanHangUp => _session.State.CallRinging || _session.State.CallActive;

    [RelayCommand] private void Previous() => _session.Press(MusicAction.Previous);
    [RelayCommand] private void Next() => _session.Press(MusicAction.Next);
    [RelayCommand] private void Play() => _session.Press(MusicAction.Play);
    [RelayCommand] private void Pause() => _session.Press(MusicAction.Pause);

    [RelayCommand] private void PhoneVolumeUp() => _session.Press(VolumeAction.PhoneUp);
    [RelayCommand] private void PhoneVolumeDown() => _session.Press(VolumeAction.PhoneDown);
    [RelayCommand] private void MediaVolumeUp() => _session.Press(VolumeAction.MediaUp);
    [RelayCommand] private void MediaVolumeDown() => _session.Press(VolumeAction.MediaDown);
    [RelayCommand] private void MuteMicrophone() => _session.Press(VolumeAction.MuteMicrophone);
    [RelayCommand] private void MuteSpeaker() => _session.Press(VolumeAction.MuteSpeaker);

    [RelayCommand] private void Answer() => _session.Press(CallAction.Answer);
    [RelayCommand] private void Reject() => _session.Press(CallAction.Reject);
    [RelayCommand] private void HangUp() => _session.Press(CallAction.HangUp);
}
```

`EMU/CCU_Emulator/ViewModels/BehaviorViewModel.cs`:

```csharp
using CCU_Emulator.Emulation;

namespace CCU_Emulator.ViewModels;

public sealed class BehaviorViewModel(CcuBehavior behavior) : ViewModelBase
{
    public CcuBehavior Behavior { get; } = behavior;
}
```

`EMU/CCU_Emulator/ViewModels/NotificationsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CCU_Emulator.Emulation;
using CommunityToolkit.Mvvm.Input;

namespace CCU_Emulator.ViewModels;

public sealed partial class NotificationsViewModel(CcuState state, CcuSession session) : ViewModelBase
{
    public ObservableCollection<NotificationEntry> Items => state.Notifications;

    /// <summary>The rider dismisses the notification on the dash.</summary>
    [RelayCommand]
    private void Dismiss(NotificationEntry? entry)
    {
        if (entry is not null) session.DismissNotification(entry.Id);
    }
}
```

`EMU/CCU_Emulator/ViewModels/LogViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using CCU_Emulator.Emulation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CCU_Emulator.ViewModels;

public sealed partial class LogViewModel : ViewModelBase
{
    private readonly PacketLog _log;

    [ObservableProperty] public partial string FilterText { get; set; } = "";

    public ObservableCollection<PacketLogEntry> Visible { get; } = [];

    public LogViewModel(PacketLog log)
    {
        _log = log;
        log.Entries.CollectionChanged += OnEntriesChanged;
        Rebuild();
    }

    partial void OnFilterTextChanged(string value) => Rebuild();

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { } added)
        {
            foreach (PacketLogEntry entry in added)
                if (Matches(entry)) Visible.Add(entry);
        }
        else
        {
            Rebuild();   // trimmed or cleared
        }
    }

    private bool Matches(PacketLogEntry e)
        => FilterText.Length == 0
           || e.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
           || e.Channel.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
           || e.Detail.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
           || e.Hex.Contains(FilterText, StringComparison.OrdinalIgnoreCase);

    private void Rebuild()
    {
        Visible.Clear();
        foreach (var entry in _log.Entries)
            if (Matches(entry)) Visible.Add(entry);
    }

    [RelayCommand]
    private void Clear() => _log.Clear();

    public string ExportText()
    {
        var sb = new StringBuilder();
        foreach (var e in Visible)
            sb.Append(e.Time.ToString("HH:mm:ss.fff")).Append(' ').Append(e.Direction.ToString().PadRight(4)).Append(' ')
              .Append(e.Channel.PadRight(12)).Append(' ').Append(e.Name).Append(' ').Append(e.Hex).Append(' ').Append(e.Detail).Append('\n');
        return sb.ToString();
    }
}
```

`EMU/CCU_Emulator/ViewModels/MainViewModel.cs` (replace the whole file):

```csharp
using CCU_Emulator.Emulation;

namespace CCU_Emulator.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    public EmulatorHost Host { get; }
    public DashViewModel Dash { get; }
    public ConnectionViewModel Connection { get; }
    public ButtonsViewModel Buttons { get; }
    public BehaviorViewModel Behavior { get; }
    public NotificationsViewModel Notifications { get; }
    public LogViewModel Log { get; }

    public string Title => $"CCU Emulator — {Host.Settings.AdvertisedName}";

    public MainViewModel(EmulatorHost host)
    {
        Host = host;
        var session = host.Session;
        Dash = new DashViewModel(session.State);
        Connection = new ConnectionViewModel(host);
        Buttons = new ButtonsViewModel(session);
        Behavior = new BehaviorViewModel(host.Settings.Behavior);
        Notifications = new NotificationsViewModel(session.State, session);
        Log = new LogViewModel(session.Log);
    }
}
```

- [ ] **Step 5: Replace the main view and window (so the app compiles with the new `MainViewModel`)**

`EMU/CCU_Emulator/Views/MainView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:CCU_Emulator.ViewModels"
             x:Class="CCU_Emulator.Views.MainView"
             x:DataType="vm:MainViewModel">
    <Grid ColumnDefinitions="*,440" ColumnSpacing="12" Margin="12">
        <ContentControl Grid.Column="0" Content="{Binding Dash}" />
        <TabControl Grid.Column="1">
            <TabItem Header="Connection"><ContentControl Content="{Binding Connection}" /></TabItem>
            <TabItem Header="Buttons"><ContentControl Content="{Binding Buttons}" /></TabItem>
            <TabItem Header="Behavior"><ContentControl Content="{Binding Behavior}" /></TabItem>
            <TabItem Header="Notifications"><ContentControl Content="{Binding Notifications}" /></TabItem>
            <TabItem Header="Log"><ContentControl Content="{Binding Log}" /></TabItem>
        </TabControl>
    </Grid>
</UserControl>
```

`EMU/CCU_Emulator/Views/MainWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:CCU_Emulator.ViewModels"
        xmlns:views="using:CCU_Emulator.Views"
        x:Class="CCU_Emulator.Views.MainWindow"
        x:DataType="vm:MainViewModel"
        Icon="/Assets/avalonia-logo.ico"
        Width="1180" Height="720" MinWidth="900" MinHeight="560"
        Title="{Binding Title}">
    <views:MainView />
</Window>
```

- [ ] **Step 6: App wiring**

`EMU/CCU_Emulator/App.axaml.cs` (replace the whole file):

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CCU_Emulator.Emulation;
using CCU_Emulator.Transport;
using CCU_Emulator.ViewModels;
using CCU_Emulator.Views;

namespace CCU_Emulator;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "emulator.settings.json");
            var notes = new List<string>();

            CcuSettings settings;
            try
            {
                settings = SettingsStore.Load(path);
            }
            catch (Exception e)
            {
                settings = new CcuSettings();   // a broken file is left untouched; run with defaults
                notes.Add($"Could not read {path}: {e.Message}. Using defaults.");
            }

            if (!TransportFactory.IsAvailable(settings.Transport))
            {
                notes.Add($"Transport {settings.Transport} is not available in this build; using LoopbackTcp.");
                settings.Transport = TransportKind.LoopbackTcp;
            }

            var host = new EmulatorHost(settings, path, a => Dispatcher.UIThread.Post(a));
            foreach (var note in notes) host.Session.Log.Info("Host", note);

            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel(host) };
            desktop.Exit += (_, _) => host.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

The mobile and browser lifetimes are intentionally dropped (out of scope for the MVP).

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: all PASS. (`ChangeNotificationsFireWhenStateChanges` accepts either the named property or the "all properties" empty-string notification; the VM raises the latter.)

- [ ] **Step 8: Commit**

```bash
git add CCU-Emulator/CCU-Emulator/CCU_Emulator CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests
git commit -m "Add emulator view models and app wiring"
```

---

### Task 12: Views

**Files:**
- Create: `EMU/CCU_Emulator/Views/DashView.axaml` (+`.cs`), `ConnectionView.axaml` (+`.cs`), `ButtonsView.axaml` (+`.cs`), `BehaviorView.axaml` (+`.cs`), `NotificationsView.axaml` (+`.cs`), `LogView.axaml` (+`.cs`)

**Interfaces:**
- Consumes: the view models from Task 11. The template's `ViewLocator` resolves `XxxViewModel` → `CCU_Emulator.Views.XxxView`, so `ContentControl Content="{Binding Dash}"` instantiates `DashView`.
- Produces: a runnable Desktop app. No automated tests (UI); verified manually in Step 6.

- [ ] **Step 1: The dash view**

`EMU/CCU_Emulator/Views/DashView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:CCU_Emulator.ViewModels"
             x:Class="CCU_Emulator.Views.DashView"
             x:DataType="vm:DashViewModel">
    <!-- The VPB canvas is 600x306 (docs/vpb.md); the whole dash scales from that size. -->
    <Viewbox Stretch="Uniform">
        <Border Width="600" Height="306" Background="#0F1318" CornerRadius="10" ClipToBounds="True">
            <Grid>
                <!-- Idle -->
                <Grid IsVisible="{Binding ShowIdle}" Margin="24" RowDefinitions="Auto,*,Auto">
                    <Grid ColumnDefinitions="*,Auto">
                        <TextBlock Text="{Binding ClockText}" FontSize="40" Foreground="White" />
                        <TextBlock Grid.Column="1" Text="{Binding StatusText}" Foreground="#9AA4AF" VerticalAlignment="Center" />
                    </Grid>
                    <StackPanel Grid.Row="1" VerticalAlignment="Center" Spacing="6">
                        <TextBlock Text="{Binding PlaybackText}" Foreground="#9AA4AF" FontSize="18" />
                        <TextBlock Text="{Binding MusicTitle}" FontSize="26" Foreground="White" TextTrimming="CharacterEllipsis" />
                        <TextBlock Text="{Binding MusicSubtitle}" Foreground="#9AA4AF" TextTrimming="CharacterEllipsis" />
                        <ProgressBar Value="{Binding MusicProgress}" Minimum="0" Maximum="100" Height="6" />
                    </StackPanel>
                    <TextBlock Grid.Row="2" Text="{Binding WeatherText}" Foreground="White" FontSize="20" />
                </Grid>

                <!-- Call -->
                <StackPanel IsVisible="{Binding ShowCall}" VerticalAlignment="Center" HorizontalAlignment="Center" Spacing="10">
                    <TextBlock Text="{Binding CallTitle}" FontSize="22" Foreground="#7FD18B" HorizontalAlignment="Center" />
                    <TextBlock Text="{Binding CallerLine}" FontSize="32" Foreground="White" HorizontalAlignment="Center" />
                </StackPanel>

                <!-- Virtual Pit Board image -->
                <Grid IsVisible="{Binding ShowVpb}">
                    <Image x:Name="VpbImage" Stretch="Uniform" />
                    <TextBlock x:Name="VpbError" IsVisible="False" Foreground="#FF8A80" TextWrapping="Wrap"
                               Margin="24" VerticalAlignment="Center" HorizontalAlignment="Center" />
                </Grid>

                <!-- Image transfer progress -->
                <TextBlock IsVisible="{Binding VpbReceiving}" Text="{Binding VpbProgressText}"
                           Foreground="#FFD54F" Margin="12" VerticalAlignment="Bottom" HorizontalAlignment="Center" />

                <!-- Notification banner -->
                <Border IsVisible="{Binding BannerVisible}" VerticalAlignment="Top" Margin="12" Padding="12"
                        Background="#F2FFFFFF" CornerRadius="8">
                    <Grid ColumnDefinitions="*,Auto">
                        <StackPanel>
                            <TextBlock Text="{Binding BannerTitle}" FontWeight="Bold" Foreground="#101418" TextTrimming="CharacterEllipsis" />
                            <TextBlock Text="{Binding BannerBody}" Foreground="#2A323A" TextTrimming="CharacterEllipsis" />
                        </StackPanel>
                        <Button Grid.Column="1" Content="✕" Command="{Binding DismissBannerCommand}" VerticalAlignment="Top" />
                    </Grid>
                </Border>
            </Grid>
        </Border>
    </Viewbox>
</UserControl>
```

`EMU/CCU_Emulator/Views/DashView.axaml.cs`:

```csharp
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CCU_Emulator.ViewModels;

namespace CCU_Emulator.Views;

public partial class DashView : UserControl
{
    private DashViewModel? _vm;

    public DashView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        if (_vm is not null) _vm.PropertyChanged -= OnViewModelChanged;
        _vm = DataContext as DashViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnViewModelChanged;
        ShowImage();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The VM raises an empty name for "everything changed", so the image is refreshed on any state change
        // that touches it; decoding is cheap relative to how rarely a VPB image changes.
        if (e.PropertyName is null or "" or nameof(DashViewModel.VpbImageBytes)) ShowImage();
    }

    private byte[]? _shown;

    private void ShowImage()
    {
        var bytes = _vm?.VpbImageBytes;
        if (ReferenceEquals(bytes, _shown)) return;
        _shown = bytes;

        VpbError.IsVisible = false;
        if (bytes is null)
        {
            VpbImage.Source = null;
            return;
        }

        // Review Focus 5: an undecodable image must show a message, never crash the dash.
        try
        {
            using var stream = new MemoryStream(bytes);
            VpbImage.Source = new Bitmap(stream);
        }
        catch (Exception e)
        {
            VpbImage.Source = null;
            VpbError.Text = $"Could not decode the VPB image ({bytes.Length} bytes): {e.Message}";
            VpbError.IsVisible = true;
        }
    }
}
```

- [ ] **Step 2: The connection, buttons and behavior views**

`EMU/CCU_Emulator/Views/ConnectionView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:CCU_Emulator.ViewModels"
             x:Class="CCU_Emulator.Views.ConnectionView"
             x:DataType="vm:ConnectionViewModel">
    <StackPanel Spacing="10" Margin="12">
        <TextBlock Text="{Binding AdvertisedName}" FontSize="18" FontWeight="SemiBold" />
        <TextBlock Text="{Binding TransportText}" />
        <TextBlock Text="{Binding LinkText}" FontSize="16" />
        <TextBlock Text="{Binding SppText}" />
        <StackPanel Orientation="Horizontal" Spacing="8">
            <Button Content="Start" Command="{Binding StartCommand}" />
            <Button Content="Stop" Command="{Binding StopCommand}" />
            <Button Content="Drop connection" Command="{Binding DropCommand}" />
        </StackPanel>
        <TextBlock Text="{Binding LastError}" Foreground="#E57373" TextWrapping="Wrap" />
    </StackPanel>
</UserControl>
```

`EMU/CCU_Emulator/Views/ButtonsView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:CCU_Emulator.ViewModels"
             x:Class="CCU_Emulator.Views.ButtonsView"
             x:DataType="vm:ButtonsViewModel">
    <ScrollViewer>
        <StackPanel Spacing="14" Margin="12">
            <TextBlock Text="Media" FontWeight="SemiBold" />
            <WrapPanel>
                <Button Content="⏮ Previous" Command="{Binding PreviousCommand}" Margin="0,0,8,8" />
                <Button Content="▶ Play" Command="{Binding PlayCommand}" Margin="0,0,8,8" />
                <Button Content="⏸ Pause" Command="{Binding PauseCommand}" Margin="0,0,8,8" />
                <Button Content="⏭ Next" Command="{Binding NextCommand}" Margin="0,0,8,8" />
            </WrapPanel>

            <TextBlock Text="Volume" FontWeight="SemiBold" />
            <WrapPanel>
                <Button Content="Media +" Command="{Binding MediaVolumeUpCommand}" Margin="0,0,8,8" />
                <Button Content="Media −" Command="{Binding MediaVolumeDownCommand}" Margin="0,0,8,8" />
                <Button Content="Call +" Command="{Binding PhoneVolumeUpCommand}" Margin="0,0,8,8" />
                <Button Content="Call −" Command="{Binding PhoneVolumeDownCommand}" Margin="0,0,8,8" />
            </WrapPanel>

            <TextBlock Text="Mute" FontWeight="SemiBold" />
            <WrapPanel>
                <Button Content="Mute mic" Command="{Binding MuteMicrophoneCommand}" Margin="0,0,8,8" />
                <Button Content="Mute speaker" Command="{Binding MuteSpeakerCommand}" Margin="0,0,8,8" />
            </WrapPanel>

            <TextBlock Text="Call (enabled while a call is ringing or active)" FontWeight="SemiBold" />
            <WrapPanel>
                <Button Content="Accept" Command="{Binding AnswerCommand}" IsEnabled="{Binding CanAnswerOrReject}" Margin="0,0,8,8" />
                <Button Content="Reject" Command="{Binding RejectCommand}" IsEnabled="{Binding CanAnswerOrReject}" Margin="0,0,8,8" />
                <Button Content="Hang up" Command="{Binding HangUpCommand}" IsEnabled="{Binding CanHangUp}" Margin="0,0,8,8" />
            </WrapPanel>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

`EMU/CCU_Emulator/Views/BehaviorView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:CCU_Emulator.ViewModels"
             x:Class="CCU_Emulator.Views.BehaviorView"
             x:DataType="vm:BehaviorViewModel">
    <ScrollViewer>
        <StackPanel Spacing="6" Margin="12">
            <TextBlock Text="Requests sent after the phone authenticates" FontWeight="SemiBold" />
            <CheckBox Content="LibVersion" IsChecked="{Binding Behavior.SendLibVersion}" />
            <CheckBox Content="Language request" IsChecked="{Binding Behavior.SendLanguageRequest}" />
            <CheckBox Content="Weather request" IsChecked="{Binding Behavior.SendWeatherRequest}" />
            <CheckBox Content="Device time request (repeats every 5 s until answered)" IsChecked="{Binding Behavior.SendDeviceTimeRequest}" />
            <CheckBox Content="Bluetooth headset state request" IsChecked="{Binding Behavior.SendHeadsetStateRequest}" />
            <CheckBox Content="Volume-controllable request" IsChecked="{Binding Behavior.SendVolumeControllableRequest}" />
            <CheckBox Content="Cell signal level request" IsChecked="{Binding Behavior.SendCellSignalRequest}" />
            <CheckBox Content="Periodic ping" IsChecked="{Binding Behavior.SendPing}" />
            <StackPanel Orientation="Horizontal" Spacing="8">
                <TextBlock Text="Ping interval (s)" VerticalAlignment="Center" />
                <NumericUpDown Minimum="0" Maximum="600" Increment="1" FormatString="0" Value="{Binding Behavior.PingIntervalSeconds}" />
            </StackPanel>

            <TextBlock Text="Fault injection" FontWeight="SemiBold" Margin="0,12,0,0" />
            <CheckBox Content="Reject Virtual Pit Board requests (0x0232)" IsChecked="{Binding Behavior.RejectVpbRequests}" />
            <CheckBox Content="Fail every authentication" IsChecked="{Binding Behavior.ForceAuthFailure}" />
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

For each of `ConnectionView`, `ButtonsView`, `BehaviorView` create the matching `.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace CCU_Emulator.Views;

public partial class ConnectionView : UserControl   // ButtonsView / BehaviorView likewise
{
    public ConnectionView() => InitializeComponent();
}
```

Note: `NumericUpDown.Value` is `decimal?`; binding it to the `int PingIntervalSeconds` needs a converter in Avalonia. If the build reports a type mismatch, replace the `NumericUpDown` with `<Slider Minimum="0" Maximum="60" Value="{Binding Behavior.PingIntervalSeconds}" Width="200" />` (`Slider.Value` is `double`, which Avalonia converts to `int` for the binding).

- [ ] **Step 3: The notifications and log views**

`EMU/CCU_Emulator/Views/NotificationsView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:CCU_Emulator.ViewModels"
             xmlns:em="using:CCU_Emulator.Emulation"
             x:Class="CCU_Emulator.Views.NotificationsView"
             x:DataType="vm:NotificationsViewModel">
    <ListBox ItemsSource="{Binding Items}" Margin="12">
        <ListBox.ItemTemplate>
            <DataTemplate x:DataType="em:NotificationEntry">
                <Grid ColumnDefinitions="*,Auto" Margin="0,4">
                    <StackPanel>
                        <TextBlock Text="{Binding Title}" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" />
                        <TextBlock Text="{Binding SubTitle}" Foreground="Gray" TextTrimming="CharacterEllipsis" />
                        <TextBlock Text="{Binding Detail}" TextWrapping="Wrap" />
                    </StackPanel>
                    <Button Grid.Column="1" Content="Dismiss on dash" VerticalAlignment="Top"
                            Command="{Binding $parent[UserControl].((vm:NotificationsViewModel)DataContext).DismissCommand}"
                            CommandParameter="{Binding}" />
                </Grid>
            </DataTemplate>
        </ListBox.ItemTemplate>
    </ListBox>
</UserControl>
```

`EMU/CCU_Emulator/Views/LogView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:CCU_Emulator.ViewModels"
             xmlns:em="using:CCU_Emulator.Emulation"
             x:Class="CCU_Emulator.Views.LogView"
             x:DataType="vm:LogViewModel">
    <Grid RowDefinitions="Auto,*" Margin="12" RowSpacing="8">
        <Grid ColumnDefinitions="*,Auto,Auto" ColumnSpacing="8">
            <TextBox Text="{Binding FilterText}" PlaceholderText="Filter (name, channel, hex, detail)" />
            <Button Grid.Column="1" Content="Copy" Click="OnCopyClicked" />
            <Button Grid.Column="2" Content="Clear" Command="{Binding ClearCommand}" />
        </Grid>
        <ListBox Grid.Row="1" x:Name="LogList" ItemsSource="{Binding Visible}">
            <ListBox.ItemTemplate>
                <DataTemplate x:DataType="em:PacketLogEntry">
                    <StackPanel Margin="0,2">
                        <TextBlock FontFamily="Consolas,Menlo,monospace" FontSize="12">
                            <Run Text="{Binding Direction}" FontWeight="Bold" />
                            <Run Text="{Binding Channel}" />
                            <Run Text="{Binding Name}" FontWeight="SemiBold" />
                        </TextBlock>
                        <TextBlock Text="{Binding Hex}" FontFamily="Consolas,Menlo,monospace" FontSize="11" Foreground="Gray" TextWrapping="Wrap" />
                        <TextBlock Text="{Binding Detail}" FontSize="11" TextWrapping="Wrap" />
                    </StackPanel>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
    </Grid>
</UserControl>
```

`EMU/CCU_Emulator/Views/LogView.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CCU_Emulator.ViewModels;

namespace CCU_Emulator.Views;

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
        // Keep the newest entry in view.
        LogList.PropertyChanged += (_, e) =>
        {
            if (e.Property == ItemsControl.ItemCountProperty && LogList.ItemCount > 0)
                LogList.ScrollIntoView(LogList.ItemCount - 1);
        };
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LogViewModel vm && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(vm.ExportText());
    }
}
```

and `NotificationsView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace CCU_Emulator.Views;

public partial class NotificationsView : UserControl
{
    public NotificationsView() => InitializeComponent();
}
```

- [ ] **Step 4: Build**

Run: `dotnet build CCU-Emulator/CCU-Emulator.slnx`
Expected: Build succeeded, 0 errors, for both target frameworks. Typical fixes if it does not: add a missing `using`; an Avalonia 12 API rename in `LogView.axaml.cs` (`ScrollIntoView`, `Clipboard.SetTextAsync`) — check the Avalonia 12 docs rather than guessing, and keep the fix minimal.

- [ ] **Step 5: Run all tests**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: all PASS.

- [ ] **Step 6: Manual smoke test**

Run: `dotnet run --project CCU-Emulator/CCU-Emulator/CCU_Emulator.Desktop -f net10.0`
Expected: a window with the dash on the left (idle: `--:--`, "No music", "No weather") and five tabs on the right. On the Connection tab press **Start**: it should read "Advertising / waiting for a phone" and the Log tab should show `Host started as YCCU_00000000000000 on LoopbackTcp`. `emulator.settings.json` appears next to the built `.exe` with placeholder identity values. Then continue with Task 13's phone simulator to see real traffic. Close the window.

- [ ] **Step 7: Commit**

```bash
git add CCU-Emulator/CCU-Emulator/CCU_Emulator
git commit -m "Add emulator views"
```

---

### Task 13: Scripted phone simulator for manual testing

**Files:**
- Create: `tools/PhoneSim/PhoneSim.cs` (a .NET 10 file-based app)

**Interfaces:**
- Consumes: the loopback frame format and the `PacketWriter`, `SppFrame`, `VpbCrc32` types from the core project (referenced with `#:project`).
- Produces: a command-line phone that connects to the loopback transport and plays a scripted session: auth, identify, language/time answers, music, weather, a notification, an incoming call, phone state, and (optionally) a VPB image from a JPEG file you supply.

- [ ] **Step 1: Write the simulator**

`tools/PhoneSim/PhoneSim.cs`:

```csharp
#:project ../../CCU-Emulator/CCU-Emulator/CCU_Emulator/CCU_Emulator.csproj

// Usage:  dotnet run tools/PhoneSim/PhoneSim.cs -- <ccuId 14> <passKey 6> [port=48800] [image.jpg]
// Plays a phone against the emulator's loopback transport and prints every frame the CCU sends back.

using System.Net.Sockets;
using System.Text;
using CCU_Emulator.Protocol;

if (args.Length < 2) { Console.Error.WriteLine("usage: PhoneSim <ccuId> <passKey> [port] [image.jpg]"); return 1; }
var ccuId = args[0];
var passKey = args[1];
var port = args.Length > 2 ? int.Parse(args[2]) : 48800;
var imagePath = args.Length > 3 ? args[3] : null;

using var client = new TcpClient();
await client.ConnectAsync("127.0.0.1", port);
var stream = client.GetStream();
byte requestId = 1;
Console.WriteLine($"connected to 127.0.0.1:{port}");

// CCU → phone frames, printed as they arrive.
_ = Task.Run(async () =>
{
    var header = new byte[3];
    try
    {
        while (true)
        {
            await stream.ReadExactlyAsync(header);
            var data = new byte[(header[1] << 8) | header[2]];
            await stream.ReadExactlyAsync(data);
            Console.WriteLine($"  CCU → phone  role {header[0]}  {Convert.ToHexString(data)}");
        }
    }
    catch (Exception) { Console.WriteLine("  (connection closed)"); }
});

async Task WriteFrame(byte role, byte[] data)
{
    var frame = new byte[3 + data.Length];
    frame[0] = role; frame[1] = (byte)(data.Length >> 8); frame[2] = (byte)data.Length;
    data.CopyTo(frame, 3);
    await stream.WriteAsync(frame);
}

Task Simple(byte[] packet) { Console.WriteLine($"phone → CCU  simple  {Convert.ToHexString(packet)}"); return WriteFrame(1, packet); }

async Task Ddt(byte[] message)
{
    Console.WriteLine($"phone → CCU  ddt     {Convert.ToHexString(message)}");
    var id = requestId++;
    await WriteFrame(3, [id, (byte)(message.Length >> 16), (byte)(message.Length >> 8), (byte)message.Length]);
    for (var offset = 0; offset < message.Length; offset += 160)
    {
        var n = Math.Min(160, message.Length - offset);
        var chunk = new byte[4 + n];
        chunk[0] = id; chunk[1] = (byte)(offset >> 16); chunk[2] = (byte)(offset >> 8); chunk[3] = (byte)offset;
        Array.Copy(message, offset, chunk, 4, n);
        await WriteFrame(5, chunk);
    }
}

byte[] Str8(string s) { var b = Encoding.UTF8.GetBytes(s); return [(byte)b.Length, .. b]; }
async Task Pause(int ms = 600) => await Task.Delay(ms);

await Ddt(new PacketWriter().U16(0x0101).Utf8(ccuId).Utf8(passKey).ToArray());       // auth
await Pause();
await Simple(new PacketWriter().U16(0x0002).U8(1).I32(0x1234).ToArray());            // identify
await Simple(new PacketWriter().U16(0x000C).U8(2).U8(0).ToArray());                  // language: English
var phoneTime = DateTimeOffset.Now.ToUnixTimeSeconds() + (long)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalSeconds;
await Simple(new PacketWriter().U16(0x000A).U8(3).I32((int)(phoneTime >> 32)).I32((int)phoneTime).ToArray());   // device time
await Simple(new PacketWriter().U16(0x013C).U8(0xE4).U8(0xB2).U8(0xC8).U8(0x47).ToArray());   // phone state
await Pause();

await Ddt([.. new PacketWriter().U16(0x0116).ToArray(), .. Str8("Test Artist"), .. Str8("Test Album"), .. Str8("Test Song"),
           .. new PacketWriter().U24(210).U8(2).U16(10).U24(15).ToArray()]);          // music, playing, 210 s, 15 s in
await Simple([.. new PacketWriter().U16(0x0004).U8(4).ToArray(), .. Encoding.UTF8.GetBytes("Berlin")]);   // weather location
await Simple(new PacketWriter().U16(0x0005).U8(5).U8(0).U8(3).U16(125).U16(140).U16(90).U8(8).ToArray());  // current: rain 12.5°C, 40%
await Pause();

var proto = new PacketWriter().U8(0x18).U8(1).U8(0x20).U8(0).U8(0x5A).U8(5).Utf8("Hello").U8(0x6A).U8(12).Utf8("From PhoneSim").ToArray();
await Ddt([.. new PacketWriter().U16(0x012F).U24(proto.Length).ToArray(), .. proto]);   // notification
await Pause(2000);

await Ddt([.. new PacketWriter().U16(0x0118).ToArray(), .. Str8("Alice"), .. Str8("+15551234"), 0]);   // incoming call
await Pause(3000);

if (imagePath is not null)
{
    var image = File.ReadAllBytes(imagePath);
    await Simple(new PacketWriter().U16(0x0231).ToArray());                                  // VPB request
    await Pause();
    const int chunkSize = 6144;
    var chunks = (image.Length + chunkSize - 1) / chunkSize;
    for (var i = 0; i < chunks; i++)
    {
        var data = image[(i * chunkSize)..Math.Min(image.Length, (i + 1) * chunkSize)];
        var payload = new PacketWriter().U16(0x0235).I32(20101).U8(i).I32(data.Length).U8(chunks - 1).I32(image.Length)
            .U32(VpbCrc32.Compute(data)).Bytes(data).ToArray();
        await WriteFrame(6, SppFrame.Encode(payload));
        Console.WriteLine($"phone → CCU  spp     chunk {i + 1}/{chunks} ({data.Length} bytes)");
        await Pause(300);
    }
}

Console.WriteLine("script finished; waiting 15 s for CCU traffic (pings), Ctrl+C to quit");
await Task.Delay(15000);
return 0;
```

- [ ] **Step 2: Run it against the running emulator**

Run the emulator (Task 12 Step 6) and press **Start**; copy the `CcuId`/`PassKey` from `emulator.settings.json` next to the built exe (placeholder zeros by default: `00000000000000` / `000000`). Then in another terminal:

`dotnet run tools/PhoneSim/PhoneSim.cs -- 00000000000000 000000 48800 path\to\any.jpg`

Expected: PhoneSim prints `CCU → phone` frames starting with `role 2  010201` (auth ok) and `role 2  0001030101` (LibVersion); the dash shows the music, weather, the "Hello" banner, then the incoming call for Alice, then (with a JPEG argument) the image with the transfer progress line. The Connection tab shows "Phone connected and authenticated". Pressing **Accept** while the call rings makes PhoneSim print `role 2  011901`.

If the `#:project` directive is rejected by your SDK, run the same file with a short `PhoneSim.csproj` next to it that references the core project instead (only the directive line changes).

- [ ] **Step 3: Commit**

```bash
git add tools/PhoneSim
git commit -m "Add scripted phone simulator for manual testing"
```

---

### Task 14: Capture dump tool and wire-format cross-check

**Files:**
- Create: `tools/BtsnoopDump/BtsnoopDump.cs` (file-based app)
- Modify (only if the cross-check finds a mismatch): `EMU/CCU_Emulator/Protocol/CcuPackets.cs`, `EMU/CCU_Emulator.Tests/Protocol/CcuPacketsTests.cs`

**Interfaces:**
- Produces: a console tool that lists the ATT traffic in an Android `btsnoop_hci.log`, with characteristic UUIDs resolved from the GATT discovery in the capture.

- [ ] **Step 1: Write the tool**

`tools/BtsnoopDump/BtsnoopDump.cs`:

```csharp
// Usage: dotnet run tools/BtsnoopDump/BtsnoopDump.cs -- "<path>/btsnoop_hci.log" > capture.txt
// Prints ATT writes, notifications and indications, with UUIDs resolved from characteristic discovery.
// Output contains device traffic: never commit it.

using System.Buffers.Binary;
using System.Text;

if (args.Length < 1) { Console.Error.WriteLine("usage: BtsnoopDump <btsnoop_hci.log>"); return 1; }
var data = File.ReadAllBytes(args[0]);
if (data.Length < 16 || Encoding.ASCII.GetString(data, 0, 7) != "btsnoop") { Console.Error.WriteLine("not a btsnoop file"); return 1; }
var datalink = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12));
if (datalink != 1002) { Console.Error.WriteLine($"datalink {datalink} not supported (need 1002, HCI UART/H4)"); return 1; }

const long UnixEpochMicros = 0x00dcddb30f2f8000L;
var uuidByHandle = new Dictionary<int, (string Uuid, int Props)>();
var pos = 16;
while (pos + 24 <= data.Length)
{
    var inclLen = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4));
    var flags = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 8));
    var micros = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(pos + 16));
    pos += 24;
    if (inclLen < 0 || pos + inclLen > data.Length) break;
    var packet = data.AsSpan(pos, inclLen);
    pos += inclLen;

    var att = AttPayload(packet);
    if (att.IsEmpty) continue;

    var time = DateTime.UnixEpoch.AddTicks((micros - UnixEpochMicros) * 10).ToLocalTime();
    var received = (flags & 1) != 0;                      // received = CCU → phone, sent = phone → CCU
    var opcode = att[0];

    if (opcode == 0x09 && att.Length > 2 && att[1] == 21)  // Read By Type response, 128-bit characteristic declarations
    {
        for (var i = 2; i + 21 <= att.Length; i += 21)
        {
            var props = att[i + 2];
            var valueHandle = BinaryPrimitives.ReadUInt16LittleEndian(att[(i + 3)..]);
            uuidByHandle[valueHandle] = (Uuid(att.Slice(i + 5, 16)), props);
        }
        continue;
    }

    string? kind = opcode switch { 0x12 => "write-req", 0x52 => "write-cmd", 0x1B => "notify", 0x1D => "indicate", 0x0B => "read-rsp", _ => null };
    if (kind is null || att.Length < 3) continue;
    var handle = BinaryPrimitives.ReadUInt16LittleEndian(att[1..]);
    var value = opcode == 0x0B ? att[1..] : att[3..];
    var name = uuidByHandle.TryGetValue(handle, out var info) ? info.Uuid : $"handle 0x{handle:X4}";
    Console.WriteLine($"{time:HH:mm:ss.fff} {(received ? "CCU→phone" : "phone→CCU")} {kind,-9} {name} {Convert.ToHexString(value)}");
}

Console.WriteLine();
Console.WriteLine("# Characteristics seen during discovery (properties bit field: 0x02 read, 0x04 write-no-rsp, 0x08 write, 0x10 notify, 0x20 indicate)");
foreach (var (handle, (uuid, props)) in uuidByHandle.OrderBy(kv => kv.Key))
    Console.WriteLine($"# 0x{handle:X4} {uuid} props=0x{props:X2}");
return 0;

static ReadOnlySpan<byte> AttPayload(ReadOnlySpan<byte> packet)
{
    if (packet.Length < 1 + 4 + 4 || packet[0] != 0x02) return default;          // H4 ACL data only
    var acl = packet[1..];
    var handleFlags = BinaryPrimitives.ReadUInt16LittleEndian(acl);
    if (((handleFlags >> 12) & 0x3) == 0x1) return default;                      // continuation fragment
    var l2cap = acl[4..];
    if (l2cap.Length < 4 || BinaryPrimitives.ReadUInt16LittleEndian(l2cap[2..]) != 0x0004) return default;   // ATT channel
    var length = BinaryPrimitives.ReadUInt16LittleEndian(l2cap);
    var payload = l2cap[4..];
    return payload.Length >= length && length > 0 ? payload[..length] : default;
}

static string Uuid(ReadOnlySpan<byte> littleEndian)
{
    var b = littleEndian.ToArray();
    Array.Reverse(b);
    return new Guid(Convert.ToHexString(b)[..8] + "-" + Convert.ToHexString(b)[8..12] + "-" + Convert.ToHexString(b)[12..16]
        + "-" + Convert.ToHexString(b)[16..20] + "-" + Convert.ToHexString(b)[20..]).ToString().ToUpperInvariant();
}
```

- [ ] **Step 2: Run it on the capture**

Run: `dotnet run tools/BtsnoopDump/BtsnoopDump.cs -- ".local/YammiC Device Logs/btsnoop_hci.log" > "$env:TEMP\capture.txt"` (PowerShell) and open the file.
Expected: lines like `… CCU→phone notify 9C810D26-B605-4306-8C1C-755A1BA3066C 010201` and a trailing `# 0x… <uuid> props=0x..` block listing the CCU's characteristics. If the capture has no discovery (no `# ` lines), the UUIDs stay as `handle 0x…`; match them by the order the first writes appear.

- [ ] **Step 3: Cross-check against the emulator**

Compare, and fix `CcuPackets` plus its test when different (the capture is ground truth):
1. Main RX notifications after auth: `010201`, `0001030101`, `000B`, `0009`, `0129`, and the ping `0135NN`. Confirm the order and that no required packet is missing (note any other main-RX packets, e.g. the `0121…` and the `77808451` notification `01000008` seen in the YammiC log; keep them for Task 19 Step 1 as "observed, not emulated").
2. Status characteristic notifications: `0101`/`0000` pairs around each DDT write, and which request ID the CCU echoes.
3. The `props=` of each characteristic. Task 16 uses them for the Windows GATT layout.
4. Whether the weather, headset or cell-signal requests appear at all, and when. Keep the answer for Task 19 Step 1 (it goes into `docs/emulator.md`); adjust the defaults of `CcuBehavior` if they never appear.

- [ ] **Step 4: Commit**

```bash
git add tools/BtsnoopDump
git commit -m "Add btsnoop dump tool"
```

(Include `CcuPackets.cs` and its test in the commit only if Step 3 changed them.)

---

## Milestone 2 — Windows Bluetooth transports

### Task 15: Advertising spike (go/no-go)

**Files:**
- Create (throwaway, do not keep in the final tree): `tools/AdvSpike/AdvSpike.csproj`, `tools/AdvSpike/Program.cs`

**Interfaces:**
- Produces: an answer, recorded as a short note appended to `docs/superpowers/specs/2026-10-10-ccu-emulator-design.md` under "Open risks" item 4. This task decides how Task 16 advertises. It builds nothing that later tasks depend on.

**Why:** `GattServiceProvider` cannot set the advertised local name; Windows advertises the PC's Bluetooth name, and a computer name is limited to 15 characters while `YCCU_<ccuId>` is 19. A client that scans by name (YammiC does) must be able to see `YCCU_…`. The real CCU advertises the name in a non-connectable advertisement first, then becomes connectable (see the `connectable=false` / `connectable=true` scan results in the YammiC log).

- [ ] **Step 1: Create the spike**

`tools/AdvSpike/AdvSpike.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

`tools/AdvSpike/Program.cs`:

```csharp
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

// args[0] = local name for the non-connectable advertisement (experiment 2); omit to skip it.
var adapter = await BluetoothAdapter.GetDefaultAsync();
Console.WriteLine($"adapter: peripheral={adapter.IsPeripheralRoleSupported} offload={adapter.IsAdvertisementOffloadSupported} machine='{Environment.MachineName}'");

var created = await GattServiceProvider.CreateAsync(Guid.Parse("AFA2CDF4-ECCF-46A7-A5EA-9DA428C0157A"));
if (created.Error != BluetoothError.Success) { Console.WriteLine($"CreateAsync failed: {created.Error}"); return; }
var provider = created.ServiceProvider;

var characteristic = await provider.Service.CreateCharacteristicAsync(
    Guid.Parse("9C810D26-B605-4306-8C1C-755A1BA3066C"),
    new GattLocalCharacteristicParameters { CharacteristicProperties = GattCharacteristicProperties.Notify });
if (characteristic.Error != BluetoothError.Success) { Console.WriteLine($"CreateCharacteristic failed: {characteristic.Error}"); return; }

provider.AdvertisementStatusChanged += (_, e) => Console.WriteLine($"advertisement status: {e.Status}");
provider.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = true });

BluetoothLEAdvertisementPublisher? publisher = null;
if (args.Length > 0)
{
    var advertisement = new BluetoothLEAdvertisement { LocalName = args[0] };
    publisher = new BluetoothLEAdvertisementPublisher(advertisement);
    publisher.StatusChanged += (_, e) => Console.WriteLine($"publisher status: {e.Status} error={e.Error}");
    publisher.Start();
}

Console.WriteLine("Advertising. On a phone, scan with nRF Connect (or YammiC). Record the name(s) shown and whether the entry is connectable. Press Enter to stop.");
Console.ReadLine();
publisher?.Stop();
provider.StopAdvertising();
```

- [ ] **Step 2: Run the three experiments, note the result of each**

1. `dotnet run --project tools/AdvSpike` — On the phone, does the scan list the service `AFA2CDF4-…`, and under what **name**? Is it connectable?
2. `dotnet run --project tools/AdvSpike -- YCCU_01234567890123` — Does a **second** entry (or the same address) now show the `YCCU_…` name? Does the scan result stay connectable/discoverable? (`publisher status: Aborted` with an error means Windows refused two advertisements at once.)
3. If neither shows `YCCU_…` on the same address: try renaming the radio. Windows takes the Bluetooth name from the computer name (15-character limit through the normal UI), so test only whether a longer DNS host name changes it (`Settings → System → About → Rename` for the short name; the long-name route is registry-based, so describe what you tried and stop if it needs a risky system change; do not edit the registry without telling the user).

- [ ] **Step 3: Decide and record**

Write the outcome (which experiment worked, exact name seen, connectability) into the spec's "Open risks" item 4 and tell the user. Decision table:
- Experiment 1 or 2 yields `YCCU_…` on a connectable device → **go**: Task 16 uses that advertising recipe.
- Nothing shows the name → **stop and ask the user**. Fallbacks to put in front of them: a BLE advertiser on other hardware (they have Espressif tooling installed), a Linux/BlueZ host for the transport, or loosening the client scan to match by service UUID for emulator testing only.

- [ ] **Step 4: Remove the spike from the tree**

```bash
rm -rf tools/AdvSpike
git add docs/superpowers/specs/2026-10-10-ccu-emulator-design.md
git commit -m "Record Windows advertising spike result"
```

---

### Task 16: Windows BLE GATT server transport

**Files:**
- Create: `EMU/CCU_Emulator/Transport/Platforms/Windows/WindowsBleTransport.cs`
- Modify: `EMU/CCU_Emulator/Transport/TransportFactory.cs`
- Test: manual (needs a radio); `EMU/CCU_Emulator.Tests` gets one compile-level test that runs only on the Windows TFM is not possible because the test project targets `net10.0`; verification is Step 5.

**Interfaces:**
- Consumes: `ICcuTransport`, `CcuChannel`, `CcuSettings` (Task 5); the advertising decision from Task 15.
- Produces: `WindowsBleTransport : ICcuTransport` (public parameterless constructor); `TransportFactory` returns it for `TransportKind.WindowsBle` on the Windows target framework. `ConnectSppAsync`/`SendSppAsync` are delegated to `WindowsSppClient` (Task 17); until then they throw `NotSupportedException`.

- [ ] **Step 1: Implement the transport**

`EMU/CCU_Emulator/Transport/Platforms/Windows/WindowsBleTransport.cs`:

```csharp
#if WINDOWS_BLUETOOTH
using System.Runtime.InteropServices.WindowsRuntime;
using CCU_Emulator.Emulation;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace CCU_Emulator.Transport.Platforms.Windows;

/// <summary>
/// The CCU's BLE side on Windows: a GATT server with the characteristics from AGENTS.md.
/// Phone writes become <see cref="DataReceived"/>; <see cref="NotifyAsync"/> notifies subscribed phones.
/// </summary>
public sealed class WindowsBleTransport : ICcuTransport
{
    private sealed record Layout(string Uuid, GattCharacteristicProperties Properties, CcuChannel? Channel);

    // Properties follow what a CCU exposes as far as known; adjust from the tools/BtsnoopDump output (Task 14, step 3).
    private static readonly Layout[] Characteristics =
    [
        new("B606C7F9-E5A1-4E75-B313-2A920054A8EB", GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse, CcuChannel.SimpleWrite),
        new("9C810D26-B605-4306-8C1C-755A1BA3066C", GattCharacteristicProperties.Notify, CcuChannel.MainRx),
        new("56851826-A090-4F6A-A5DF-D1776520F74A", GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse, CcuChannel.DdtControl),
        new("75AB2788-87F6-4F61-BA9C-6F85A12600D1", GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify, CcuChannel.DdtStatus),
        new("E998AEAB-EA8F-4063-83AA-2017C1A44484", GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse, CcuChannel.DdtData),
        new("AB8D233A-CC58-4932-B397-A3751911E021", GattCharacteristicProperties.Notify, null),   // YRC settings RX
        new("77808451-E42F-4F6B-83ED-3AB44AA3D16A", GattCharacteristicProperties.Notify, null),   // DDT hi-bandwidth
        new("347456EE-A588-4DD5-BD98-62BE3672E27A", GattCharacteristicProperties.Notify | GattCharacteristicProperties.Write, null),   // DDT large
        new("E974B9A1-FA31-43C6-91AE-09931F3F6EF1", GattCharacteristicProperties.Notify, null),   // DDT v2
        new("56DC2D58-CBE1-4BA9-A104-E9B8EB4DA95C", GattCharacteristicProperties.Indicate, null), // DDT v3
        new("7690EEF4-F3A1-11EC-B939-0242AC120002", GattCharacteristicProperties.Notify, null),   // YRC response RX
    ];

    private const string ServiceUuid = "AFA2CDF4-ECCF-46A7-A5EA-9DA428C0157A";

    public event Action<CcuChannel, byte[]>? DataReceived;
    public event Action<bool>? BleConnectionChanged;
    public event Action<bool>? SppConnectionChanged;
    public event Action<string>? Diagnostic;

    private readonly Dictionary<CcuChannel, GattLocalCharacteristic> _notifyChars = [];
    private readonly List<GattLocalCharacteristic> _allChars = [];
    private GattServiceProvider? _provider;
    private CcuSettings? _settings;
    private WindowsSppClient? _spp;
    private byte[] _lastStatus = [0, 0];
    private bool _connected;

    public bool IsRunning => _provider != null;

    public async Task StartAsync(CcuSettings settings, CancellationToken ct = default)
    {
        if (_provider != null) return;
        _settings = settings;

        var adapter = await BluetoothAdapter.GetDefaultAsync();
        if (adapter is null) throw new InvalidOperationException("No Bluetooth adapter found.");
        if (!adapter.IsPeripheralRoleSupported) throw new InvalidOperationException("This Bluetooth adapter does not support the peripheral role.");

        var created = await GattServiceProvider.CreateAsync(Guid.Parse(ServiceUuid));
        if (created.Error != BluetoothError.Success) throw new InvalidOperationException($"GATT service creation failed: {created.Error}");
        var provider = created.ServiceProvider;

        foreach (var layout in Characteristics)
        {
            var parameters = new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = layout.Properties,
                ReadProtectionLevel = GattProtectionLevel.Plain,
                WriteProtectionLevel = GattProtectionLevel.Plain,
            };
            var result = await provider.Service.CreateCharacteristicAsync(Guid.Parse(layout.Uuid), parameters);
            if (result.Error != BluetoothError.Success) throw new InvalidOperationException($"Characteristic {layout.Uuid} failed: {result.Error}");

            var characteristic = result.Characteristic;
            _allChars.Add(characteristic);
            if (layout.Channel is { } channel)
            {
                if (layout.Properties.HasFlag(GattCharacteristicProperties.Notify)) _notifyChars[channel] = characteristic;
                if (layout.Properties.HasFlag(GattCharacteristicProperties.Write))
                    characteristic.WriteRequested += (s, e) => OnWrite(e, channel);
                if (layout.Properties.HasFlag(GattCharacteristicProperties.Read))
                    characteristic.ReadRequested += (s, e) => OnRead(e);
            }
            characteristic.SubscribedClientsChanged += (s, _) => OnSubscribersChanged();
        }

        provider.AdvertisementStatusChanged += (_, e) => Diagnostic?.Invoke($"advertisement: {e.Status}");
        provider.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = true });
        _provider = provider;
        Diagnostic?.Invoke($"GATT server started; the advertised name is {settings.AdvertisedName} (see Task 15 for how Windows exposes it)");
    }

    private async void OnWrite(GattWriteRequestedEventArgs args, CcuChannel channel)
    {
        var deferral = args.GetDeferral();
        try
        {
            var request = await args.GetRequestAsync();
            if (request is null) return;
            var bytes = request.Value.ToArray();
            if (request.Option == GattWriteOption.WriteWithResponse) request.Respond();
            DataReceived?.Invoke(channel, bytes);
        }
        catch (Exception e)
        {
            Diagnostic?.Invoke($"write on {channel} failed: {e.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnRead(GattReadRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var request = await args.GetRequestAsync();
            request?.RespondWithValue(_lastStatus.AsBuffer());   // the only readable characteristic is DDT status
        }
        catch (Exception e)
        {
            Diagnostic?.Invoke($"read failed: {e.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>The phone counts as connected while any characteristic has a subscriber.</summary>
    private void OnSubscribersChanged()
    {
        var any = _allChars.Any(c => c.SubscribedClients.Count > 0);
        if (any == _connected) return;
        _connected = any;
        Diagnostic?.Invoke(any ? "phone subscribed" : "phone unsubscribed / disconnected");
        BleConnectionChanged?.Invoke(any);
    }

    public async Task NotifyAsync(CcuChannel channel, byte[] data)
    {
        if (channel == CcuChannel.DdtStatus) _lastStatus = data;
        if (!_notifyChars.TryGetValue(channel, out var characteristic))
            throw new ArgumentException($"{channel} is not a notify characteristic", nameof(channel));

        var results = await characteristic.NotifyValueAsync(data.AsBuffer());
        if (results.Count == 0) throw new InvalidOperationException("no subscribed client");
        foreach (var result in results)
            if (result.Status != GattCommunicationStatus.Success)
                throw new InvalidOperationException($"notify failed: {result.Status}");
    }

    public async Task ConnectSppAsync(CancellationToken ct = default)
    {
        if (_settings is null || string.IsNullOrWhiteSpace(_settings.PhoneBluetoothAddress))
            throw new InvalidOperationException("PhoneBluetoothAddress is not set in the settings.");
        _spp?.Dispose();
        _spp = new WindowsSppClient();
        _spp.DataReceived += data => DataReceived?.Invoke(CcuChannel.Spp, data);
        _spp.ConnectionChanged += up => SppConnectionChanged?.Invoke(up);
        _spp.Diagnostic += message => Diagnostic?.Invoke(message);
        await _spp.ConnectAsync(_settings.PhoneBluetoothAddress, ct);
    }

    public Task SendSppAsync(byte[] data)
        => _spp?.WriteAsync(data) ?? throw new InvalidOperationException("SPP link is not connected");

    /// <summary>
    /// Windows cannot force-disconnect a central. Best effort: close SPP, then tear the GATT service
    /// down and rebuild it, which drops the existing link. Verify on hardware (Task 18).
    /// </summary>
    public async Task DisconnectAsync()
    {
        _spp?.Dispose();
        _spp = null;
        if (_settings is null || _provider is null) return;
        var settings = _settings;
        await StopAsync();
        await StartAsync(settings);
    }

    public Task StopAsync()
    {
        _spp?.Dispose();
        _spp = null;
        _provider?.StopAdvertising();
        _provider = null;
        _notifyChars.Clear();
        _allChars.Clear();
        if (_connected)
        {
            _connected = false;
            BleConnectionChanged?.Invoke(false);
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
#endif
```

- [ ] **Step 1b: Name advertisement (only if Task 15 found that experiment 2 was needed)**

In `WindowsBleTransport.cs` add `using Windows.Devices.Bluetooth.Advertisement;`, the field

```csharp
    private BluetoothLEAdvertisementPublisher? _namePublisher;
```

directly after the `StartAdvertising(...)` call in `StartAsync`:

```csharp
        var nameAdvertisement = new BluetoothLEAdvertisement { LocalName = settings.AdvertisedName };
        _namePublisher = new BluetoothLEAdvertisementPublisher(nameAdvertisement);
        _namePublisher.StatusChanged += (_, e) => Diagnostic?.Invoke($"name advertisement: {e.Status} {e.Error}");
        _namePublisher.Start();
```

and in `StopAsync`, before `_provider?.StopAdvertising();`:

```csharp
        _namePublisher?.Stop();
        _namePublisher = null;
```

Skip this step when experiment 1 alone already showed `YCCU_…`.

- [ ] **Step 2: Register it in the factory**

Replace `EMU/CCU_Emulator/Transport/TransportFactory.cs` with:

```csharp
using CCU_Emulator.Emulation;

namespace CCU_Emulator.Transport;

public static class TransportFactory
{
    public static bool IsAvailable(TransportKind kind) => kind switch
    {
        TransportKind.LoopbackTcp => true,
#if WINDOWS_BLUETOOTH
        TransportKind.WindowsBle => true,
#endif
        _ => false,
    };

    public static ICcuTransport Create(TransportKind kind) => kind switch
    {
        TransportKind.LoopbackTcp => new LoopbackTcpTransport(),
#if WINDOWS_BLUETOOTH
        TransportKind.WindowsBle => new Platforms.Windows.WindowsBleTransport(),
#endif
        _ => throw new PlatformNotSupportedException($"Transport {kind} is not available in this build."),
    };
}
```

- [ ] **Step 3: Build, expecting the missing SPP client**

Run: `dotnet build CCU-Emulator/CCU-Emulator.slnx`
Expected: FAIL on the Windows target only — `WindowsSppClient` does not exist yet. Do Task 17 Step 1 before building again (the two files are one deliverable; they are split for review size only).

- [ ] **Step 4: (after Task 17) Run tests**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: all PASS (the `net10.0` test build never compiles the Windows code).

- [ ] **Step 5: Manual check without a phone app**

Set `"Transport": "WindowsBle"` in the built `emulator.settings.json`, run the Desktop app, press **Start**. Expected: the Log shows `GATT server started`, then `advertisement: Started`. On a phone, nRF Connect shows the device and the service `AFA2CDF4-…` with the eleven characteristics; subscribing to `9C810D26…` makes the Connection tab read "Phone connected (not authenticated)". Writing hex `01 01` + the 20 ASCII bytes of CCUID+passkey via DDT (control `56851826…` ← `01 00 00 16`, data `E998AEAB…` ← `01 00 00 00` + the 22 bytes) makes `01 02 01` arrive as a notification on `9C810D26…`.

- [ ] **Step 6: Commit (after Task 17)**

Commit together with Task 17.

---

### Task 17: Windows SPP (RFCOMM) client

**Files:**
- Create: `EMU/CCU_Emulator/Transport/Platforms/Windows/WindowsSppClient.cs`

**Interfaces:**
- Consumes: `CcuSettings.PhoneBluetoothAddress` (Task 5).
- Produces: `WindowsSppClient : IDisposable` with `event Action<byte[]> DataReceived`, `event Action<bool> ConnectionChanged`, `event Action<string> Diagnostic`, `Task ConnectAsync(string phoneAddress, CancellationToken ct)`, `Task WriteAsync(byte[] data)`. The CCU is the Classic **client**; the phone listens on service UUID `00008911-0000-1000-8000-00805f9b34fb`.

- [ ] **Step 1: Implement the client**

`EMU/CCU_Emulator/Transport/Platforms/Windows/WindowsSppClient.cs`:

```csharp
#if WINDOWS_BLUETOOTH
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace CCU_Emulator.Transport.Platforms.Windows;

/// <summary>
/// The CCU's Classic Bluetooth link: connects to the phone's SPP server (UUID 00008911-…). The phone
/// must be paired with this PC in Windows Bluetooth settings first, and the phone app must be listening.
/// </summary>
internal sealed class WindowsSppClient : IDisposable
{
    public static readonly Guid SppServiceUuid = Guid.Parse("00008911-0000-1000-8000-00805f9b34fb");

    public event Action<byte[]>? DataReceived;
    public event Action<bool>? ConnectionChanged;
    public event Action<string>? Diagnostic;

    private BluetoothDevice? _device;
    private StreamSocket? _socket;
    private DataWriter? _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _up;

    public async Task ConnectAsync(string phoneAddress, CancellationToken ct)
    {
        var address = ParseAddress(phoneAddress);
        _device = await BluetoothDevice.FromBluetoothAddressAsync(address)
            ?? throw new InvalidOperationException($"Bluetooth device {phoneAddress} not found. Pair the phone with this PC in Windows settings first.");

        var services = await _device.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(SppServiceUuid), BluetoothCacheMode.Uncached);
        if (services.Error != BluetoothError.Success || services.Services.Count == 0)
            throw new InvalidOperationException($"The phone does not expose the SPP service ({services.Error}). Is the phone app running and listening?");

        var service = services.Services[0];
        _socket = new StreamSocket();
        await _socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName).AsTask(ct);
        _writer = new DataWriter(_socket.OutputStream);
        SetUp(true);
        Diagnostic?.Invoke($"SPP connected to {phoneAddress}");
        _ = ReadLoopAsync(_socket);
    }

    private async Task ReadLoopAsync(StreamSocket socket)
    {
        try
        {
            using var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
            while (true)
            {
                var loaded = await reader.LoadAsync(4096);
                if (loaded == 0) break;
                var buffer = new byte[loaded];
                reader.ReadBytes(buffer);
                DataReceived?.Invoke(buffer);
            }
        }
        catch (Exception e)
        {
            Diagnostic?.Invoke($"SPP read ended: {e.Message}");
        }
        finally
        {
            SetUp(false);
        }
    }

    public async Task WriteAsync(byte[] data)
    {
        var writer = _writer ?? throw new InvalidOperationException("SPP link is not connected");
        await _writeLock.WaitAsync();
        try
        {
            writer.WriteBytes(data);
            await writer.StoreAsync();
            await writer.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void SetUp(bool up)
    {
        if (_up == up) return;
        _up = up;
        ConnectionChanged?.Invoke(up);
    }

    private static ulong ParseAddress(string text)
    {
        try { return Convert.ToUInt64(text.Replace(":", "").Replace("-", ""), 16); }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            throw new InvalidOperationException($"'{text}' is not a Bluetooth address (expected AA:BB:CC:DD:EE:FF).");
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _socket?.Dispose();
        _device?.Dispose();
        _writer = null;
        _socket = null;
        _device = null;
        SetUp(false);
    }
}
#endif
```

- [ ] **Step 2: Build everything**

Run: `dotnet build CCU-Emulator/CCU-Emulator.slnx`
Expected: Build succeeded for `net10.0` and `net10.0-windows10.0.19041.0`. If WinRT `IAsyncOperation` awaiting fails to resolve, confirm the Windows target framework is `net10.0-windows10.0.19041.0` (it provides the projection).

- [ ] **Step 3: Run tests**

Run: `dotnet test CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests`
Expected: all PASS.

- [ ] **Step 4: Commit Tasks 16 and 17 together**

```bash
git add CCU-Emulator/CCU-Emulator/CCU_Emulator
git commit -m "Add Windows BLE GATT server and RFCOMM client transports"
```

---

### Task 18: Hardware verification with YammiC and the official app

**Files:**
- Modify (only if findings require it): `EMU/CCU_Emulator/Transport/Platforms/Windows/*.cs`, `EMU/CCU_Emulator/Protocol/CcuPackets.cs`

**Interfaces:**
- Produces: a go/no-go answer per client, with the failing layer named (advertise, bond, subscribe, auth, identify, SPP). No code unless a finding needs a fix.

Prerequisites: the real bike is off or out of Bluetooth range; the emulator's `emulator.settings.json` holds the bike's CCUID, passkey and (for SPP) the phone's Classic address; the phone is paired with this PC in Windows settings.

- [ ] **Step 1: YammiC (Kotlin app) against the emulator**

Run the emulator with `WindowsBle`, press **Start**. Start YammiC's connection (it scans for `YCCU_<ccuId>`). Walk the log top to bottom and note the first thing that fails:
1. Scan result for the name: seen / not seen (Task 15's recipe).
2. Bond: YammiC bonds if needed. If Windows or Android raises a pairing prompt, record it; if the bond is refused, set `ReadProtectionLevel`/`WriteProtectionLevel` to `EncryptionRequired` in `WindowsBleTransport` and retry once.
3. GATT connect, MTU 183, service discovery (11 characteristics), notifications subscribed (7).
4. SPP: the emulator connects to YammiC's `00008911-…` server and sends the `013A` frame; YammiC logs `SPP: received ccuId=… passKey=***`.
5. Auth: the emulator log shows `AuthenticationResponse (ok)`; YammiC proceeds to identify.
6. Startup requests answered: language, device time, volume-controllable, weather.
7. Music, notification, call, VPB image appear on the dash; buttons reach the phone.

- [ ] **Step 2: The official Y-Connect app against the emulator**

Repeat with the official app (already paired to the bike in its own storage, so no pairing flow). Note the first failing layer. Expected unknowns: whether it accepts a Windows GATT server, the required security level, the MAC it may have stored. If it fails before auth, capture what it did (the emulator Log tab, Windows Bluetooth event log) and stop; report to the user rather than guessing.

- [ ] **Step 3: Record findings**

Keep the outcome of both steps (date, client, which steps passed, the failing layer and any workaround needed) for Task 19 Step 1, which writes it under "Verified against" in `docs/emulator.md`.

- [ ] **Step 4: Commit any fixes**

```bash
git add -A CCU-Emulator/CCU-Emulator/CCU_Emulator CCU-Emulator/CCU-Emulator/CCU_Emulator.Tests
git commit -m "Fix issues found in hardware verification"
```

(Skip when Steps 1–2 found nothing to change.)

---

### Task 19: Documentation

**Files:**
- Create: `docs/emulator.md`
- Modify: `AGENTS.md`

- [ ] **Step 1: Write the emulator doc**

`docs/emulator.md`:

```markdown
# CCU Emulator

A C# / Avalonia emulator of a Yamaha CCU, used to develop and test phone-side clients without the bike.
Source: `CCU-Emulator/CCU-Emulator/CCU_Emulator`. Design: `docs/superpowers/specs/2026-10-10-ccu-emulator-design.md`.

## Run

    dotnet run --project CCU-Emulator/CCU-Emulator/CCU_Emulator.Desktop -f net10.0-windows10.0.19041.0   # Windows, with Bluetooth
    dotnet run --project CCU-Emulator/CCU-Emulator/CCU_Emulator.Desktop -f net10.0                       # any OS, loopback only

On first start `emulator.settings.json` is written next to the executable. It is gitignored; see
`CCU-Emulator/CCU-Emulator/emulator.settings.example.json` for the fields. Put the CCUID and passkey of the bike you are
emulating there. Keep the real bike off or out of range while testing.

## Transports

- `LoopbackTcp` (all platforms): frames `[channel 1B][length 2B BE][bytes]` on `127.0.0.1:48800`. Drive it with
  `dotnet run tools/PhoneSim/PhoneSim.cs -- <ccuId> <passKey> [port] [image.jpg]` or from your own client.
- `WindowsBle` (Windows builds only): a GATT server plus a Classic RFCOMM client. Needs a Bluetooth adapter with the
  peripheral role, and, for the VPB image link, the phone's Classic address in `PhoneBluetoothAddress` and a Windows
  pairing with the phone.

## What it emulates

Auth (`0x0101`/`0x0102`), identify, the CCU's startup requests (LibVersion, language, device time, volume-controllable,
weather, headset state, cell signal) and the `01 35 <seq>` ping; music, weather, notifications, call and phone state;
Virtual Pit Board (request/ack over BLE, image chunks over the SPP link); handlebar buttons (media, volume, mute, call
accept/reject/hang up).

## Observed on a real CCU, not emulated

None recorded yet.

## Verified against

None recorded yet.

## Dev tools

- `tools/PhoneSim/PhoneSim.cs`: scripted phone for the loopback transport.
- `tools/BtsnoopDump/BtsnoopDump.cs`: lists ATT traffic in an Android `btsnoop_hci.log`. Its output contains device
  traffic; do not commit it.
```

Before saving, replace each `None recorded yet.` with what Task 14 Step 3 (packets a real CCU sends that the emulator does not, for example the `0x0121` main-RX packet and the `77808451` notification `01 00 00 08`) and Task 18 Steps 1–2 (date, client, result, failing layer) found. Where a task found nothing to report, keep the sentence "None recorded yet." for that section.

- [ ] **Step 2: Add the emulator to `AGENTS.md`**

In `AGENTS.md`, under "## Workspace Layout" add:

```markdown
- `CCU-Emulator/` — C# / Avalonia CCU emulator for developing clients without the bike; see `docs/emulator.md`.
```

- [ ] **Step 3: Commit**

```bash
git add docs/emulator.md AGENTS.md
git commit -m "Document the CCU emulator"
```

---

## Out of scope for this plan (milestone 3)

GPS / navigation display: NaviLite over a second RFCOMM role, handshake/auth, and a JPEG frame viewer. It gets its own spec and plan once the MVP connects.
