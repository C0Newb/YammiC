# Media Control

## Overview

The CCU sends media commands to the phone when the rider presses handlebar buttons. The phone executes the action on the active media session and optionally reports back the new volume level.

---

## BluetoothMusicControl (cmd 0x0117, CCU → phone)

Arrives as a notification on the main RX characteristic (`9C810D26-...`).

**Wire format (3 bytes total):**
```
bytes[0..1] = 0x01 0x17
bytes[2]    = action byte
```

**Action byte values:**

| Byte | Action |
|------|--------|
| 0x01 | NextTrack |
| 0x02 | PreviousTrack |
| 0x03 | Play |
| 0x04 | Pause |

Other values → ignored.

**Phone response:** execute the action via `AudioManager.dispatchMediaKeyEvent()`. No reply packet is sent.

---

## PhoneVolumeControllableRequest (cmd 0x0129, CCU → phone)

The CCU asks whether the phone supports remote volume control. Must be answered before the CCU will send any `PhoneVolumeControl` commands.

**Wire format (2–3 bytes):**
```
bytes[0..1] = 0x01 0x29
```

**Phone response:** send `PhoneVolumeControllableResponse` (cmd 0x012A).

---

## PhoneVolumeControllableResponse (cmd 0x012A, phone → CCU)

**Wire format (4 bytes):**
```
bytes[0..1] = 0x01 0x2A
bytes[2]    = requestId
bytes[3]    = 0x01 (can control) or 0x00 (cannot)
```

Send `true` to enable subsequent `PhoneVolumeControl` commands.

---

## PhoneVolumeControl (cmd 0x011D, CCU → phone)

Arrives after the phone has sent `PhoneVolumeControllableResponse(true)`.

**Wire format (3 bytes):**
```
bytes[0..1] = 0x01 0x1D
bytes[2]    = action byte
```

**Action byte values:**

| Byte | Action |
|------|--------|
| 0x01 | VolumeUp (phone call volume) |
| 0x02 | VolumeDown (phone call volume) |
| 0x03 | MuteMicrophone |
| 0x04 | MuteSpeaker |
| 0x05 | MediaVolumeUp |
| 0x06 | MediaVolumeDown |

**Phone response:** execute the volume change via `AudioManager.adjustStreamVolume()`, then send `PhoneVolumeLevel` with the new volume.

---

## PhoneVolumeLevel (cmd 0x011E, phone → CCU)

Reports the current phone volume so the CCU display stays in sync.

**Wire format (5 bytes):**
```
bytes[0..1] = 0x01 0x1E
bytes[2]    = requestId
bytes[3]    = type: 0x00 = Phone, 0x01 = Media
bytes[4]    = volume percentage (0–100)
```

Send after handling a `VolumeControl` command, and optionally on connection setup.

---

## Android Implementation

**Transport controls** — no permissions needed:
```kotlin
audioManager.dispatchMediaKeyEvent(KeyEvent(KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_MEDIA_NEXT))
audioManager.dispatchMediaKeyEvent(KeyEvent(KeyEvent.ACTION_UP,   KeyEvent.KEYCODE_MEDIA_NEXT))
```

This routes to whichever app currently holds the active `MediaSession`. Works with Spotify, YouTube Music, Podcast apps, etc.

**Volume** — no permissions needed:
```kotlin
audioManager.adjustStreamVolume(
    AudioManager.STREAM_MUSIC,   // or STREAM_VOICE_CALL for phone volume
    AudioManager.ADJUST_RAISE,   // or ADJUST_LOWER
    AudioManager.FLAG_SHOW_UI,   // shows the system volume toast
)
```

**Reading current volume (0–100%):**
```kotlin
val max = audioManager.getStreamMaxVolume(AudioManager.STREAM_MUSIC)
val cur = audioManager.getStreamVolume(AudioManager.STREAM_MUSIC)
val pct = if (max > 0) (cur * 100) / max else 0
```

---

## Full Sequence

```
CCU: PhoneVolumeControllableRequest (0x0129)
Phone: PhoneVolumeControllableResponse(true) (0x012A)
Phone: PhoneVolumeLevel(Media, currentPct) (0x011E)   ← optional, initialises CCU display

[rider presses next-track button]
CCU: BluetoothMusicControl(NextTrack=0x01) (0x0117)
Phone: → dispatchMediaKeyEvent(KEYCODE_MEDIA_NEXT)

[rider presses volume up button]
CCU: PhoneVolumeControl(MediaVolumeUp=0x05) (0x011D)
Phone: → adjustStreamVolume(STREAM_MUSIC, ADJUST_RAISE)
Phone: PhoneVolumeLevel(Media, newPct) (0x011E)
```

---

## Mute Actions

`MuteMicrophone` and `MuteSpeaker` are defined in the enum but not implemented — muting requires `AudioManager.setMicrophoneMute()` (no special permission for mic) and `AudioManager.adjustStreamVolume(STREAM_MUSIC, ADJUST_MUTE)` (requires `MODIFY_AUDIO_SETTINGS`). Add to `AndroidManifest.xml` if needed:

```xml
<uses-permission android:name="android.permission.MODIFY_AUDIO_SETTINGS" />
```
