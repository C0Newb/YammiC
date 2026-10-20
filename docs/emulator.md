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

From `.local/YammiC Device Logs/btsnoop_hci.log` (cross-checked with `tools/BtsnoopDump`): the `0x0121` main-RX packet right after auth (it carries a device serial, so it is deliberately not copied), a hi-bandwidth notification `01 00 00 08`, and large telemetry streams on the DDT v2 / v3 characteristics (vehicle data). The packets the emulator does send match the capture byte for byte (`01 02 01`, `00 01 03 01 01`, `00 0B`, `00 09`, `01 29`, `02 38`; status notifications echo the request id). Weather, headset-state and cell-signal requests do not appear in that capture; they are on by default and each has a toggle.

## Verified against

- 2026-10-10, Windows 11, this PC's adapter (peripheral role supported): the GATT server builds all eleven characteristics and `StartAsync` succeeds. **Advertising does not work as designed**: see "Known limitation" below. No phone client has been tested yet (the YammiC / official-app checks are still to do).

## Known limitation: advertised name on Windows

Probed on this machine without a phone:

- `GattServiceProvider` with `IsConnectable` + `IsDiscoverable` and the 128-bit service UUID `AFA2CDF4-…` ends in `Aborted`. The same call with a 16-bit UUID (`0x180F`) reaches `Started`. The likely cause: Windows puts the PC name (here `CNEWB-NZXT11`, 12 characters) in the same advertisement as the 18-byte UUID, which overflows the 31-byte limit. A PC name of about 8 characters or fewer would probably fit.
- `BluetoothLEAdvertisementPublisher` rejects `LocalName` (`ArgumentException`) and raw name data sections (types 0x08 and 0x09, `UnauthorizedAccessException`). Manufacturer data is accepted and reaches `Started`.
- Computer names are limited to 15 characters, while `YCCU_<ccuId>` is 19. So Windows cannot advertise the CCU's name with these APIs, and a client that scans by name will not find the emulator over Windows BLE.

The loopback transport is unaffected. Options for real-radio testing: a client that scans by service UUID for emulator use, other advertising hardware (for example an ESP32), or a Linux/BlueZ host.

## Dev tools

- `tools/PhoneSim/PhoneSim.cs`: scripted phone for the loopback transport.
- `tools/BtsnoopDump/BtsnoopDump.cs`: lists ATT traffic in an Android `btsnoop_hci.log`. Its output contains device
  traffic; do not commit it.
