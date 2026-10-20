# YammiC XML UI Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace YammiC's single-screen Jetpack Compose UI with an XML-layout, Fragment-based, multi-page UI (Dashboard / Settings / Pairing / Navigation settings) navigated via Jetpack Navigation Component, editable in Android Studio's graphical Layout Editor.

**Architecture:** `MainActivity` becomes a thin `AppCompatActivity` shell hosting a `NavHostFragment`. An Activity-scoped `BikeViewModel` (`AndroidViewModel`) owns the `BikeForegroundService` binding and exposes connection/feature state as `LiveData`, which each Fragment observes via `activityViewModels()`. `BikeForegroundService` and the `ymcsdk` package are otherwise unchanged; this plan only adds the minimum new `StateFlow`s needed to feed the new UI (live media metadata, VPB status text).

**Tech Stack:** Kotlin, Android View system (XML layouts + ViewBinding), AndroidX Fragment, Jetpack Navigation Component (`navigation-fragment-ktx`/`navigation-ui-ktx`), Material Components for Android (`com.google.android.material`), AndroidX Lifecycle (`LiveData`/`ViewModel`).

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-03-xml-ui-redesign-design.md`. Follow it exactly; this plan implements every section of it.
- No multi-bike support, no bike-selection screen — Dashboard is the navigation start destination.
- No implementation of vehicle telemetry / odometer (`VehicleInformation` 0x0007) — the dashboard shows a static `"--"` placeholder for it.
- Music metadata, Phone metadata, and Auto-reconnect Settings toggles are UI-only stubs backed by `SharedPreferences` — they must **not** gate or drive any real monitor/service behavior in this plan. That is explicitly out of scope (separate follow-up project).
- No new automated UI tests (per spec). The one exception is the pure `deriveConnectButtonState` function (Task 3), which gets a plain JUnit test — this matches the existing repo convention of unit-testing pure Kotlin logic (see `YammiC/app/src/test/java/co/cnewb/yammic/ymcsdk/navigation/protocol/NaviLiteCrc32Test.kt`).
- Theme: light mode = white surface (`#FFFFFF`) + pink accent (`#E91E63`); dark mode = black surface (`#000000`) + teal accent (`#26A69A`), via `Theme.MaterialComponents.DayNight.NoActionBar` and a `values-night/colors.xml` override.
- **Do not run `git commit`.** The user has asked not to commit without an explicit request. Each task ends with a build/verification step, not a commit — leave changes staged/unstaged in the working tree for the user to review and commit themselves.
- Build/verify command: `./gradlew.bat :app:compileDebugKotlin` (fast Kotlin+resource compile check) or `./gradlew.bat :app:assembleDebug` (full build, used in the final task). Requires `JAVA_HOME` set to a JDK 17+ install, per `AGENTS.md`.

---

### Task 1: Remove Compose, add View-system dependencies

**Files:**
- Modify: `YammiC/build.gradle.kts`
- Modify: `YammiC/gradle/libs.versions.toml`
- Modify: `YammiC/app/build.gradle.kts`

**Interfaces:**
- Produces: `viewBinding = true` build feature (generates `co.cnewb.yammic.databinding.*Binding` classes consumed by every later task), and the `libs.androidx.*` catalog entries every later task's `build.gradle.kts` dependency list already assumes exist.

- [ ] **Step 1: Update the version catalog**

Replace the full contents of `YammiC/gradle/libs.versions.toml` with:

```toml
[versions]
agp = "9.2.1"
coreKtx = "1.10.1"
junit = "4.13.2"
junitVersion = "1.1.5"
espressoCore = "3.5.1"
lifecycleRuntimeKtx = "2.6.1"
activity = "1.9.3"
fragment = "1.8.5"
navigation = "2.8.4"
material = "1.12.0"
appcompat = "1.7.0"

[libraries]
androidx-core-ktx = { group = "androidx.core", name = "core-ktx", version.ref = "coreKtx" }
junit = { group = "junit", name = "junit", version.ref = "junit" }
androidx-junit = { group = "androidx.test.ext", name = "junit", version.ref = "junitVersion" }
androidx-espresso-core = { group = "androidx.test.espresso", name = "espresso-core", version.ref = "espressoCore" }
androidx-lifecycle-runtime-ktx = { group = "androidx.lifecycle", name = "lifecycle-runtime-ktx", version.ref = "lifecycleRuntimeKtx" }
androidx-lifecycle-livedata-ktx = { group = "androidx.lifecycle", name = "lifecycle-livedata-ktx", version.ref = "lifecycleRuntimeKtx" }
androidx-lifecycle-viewmodel-ktx = { group = "androidx.lifecycle", name = "lifecycle-viewmodel-ktx", version.ref = "lifecycleRuntimeKtx" }
androidx-activity-ktx = { group = "androidx.activity", name = "activity-ktx", version.ref = "activity" }
androidx-fragment-ktx = { group = "androidx.fragment", name = "fragment-ktx", version.ref = "fragment" }
androidx-navigation-fragment-ktx = { group = "androidx.navigation", name = "navigation-fragment-ktx", version.ref = "navigation" }
androidx-navigation-ui-ktx = { group = "androidx.navigation", name = "navigation-ui-ktx", version.ref = "navigation" }
androidx-material = { group = "com.google.android.material", name = "material", version.ref = "material" }
androidx-appcompat = { group = "androidx.appcompat", name = "appcompat", version.ref = "appcompat" }

[plugins]
android-application = { id = "com.android.application", version.ref = "agp" }
```

This drops `kotlin`, `composeBom`, `activityCompose`, and every `androidx-compose-*` entry, and adds the Fragment/Navigation/Material/AppCompat/Activity/Lifecycle-livedata-viewmodel entries the rest of this plan needs.

- [ ] **Step 2: Update the root build file**

Replace the full contents of `YammiC/build.gradle.kts` with:

```kotlin
// Top-level build file where you can add configuration options common to all sub-projects/modules.
plugins {
    alias(libs.plugins.android.application) apply false
}
```

(Drops `alias(libs.plugins.kotlin.compose) apply false` — the Compose compiler plugin is no longer needed.)

- [ ] **Step 3: Update the app module build file**

Replace the full contents of `YammiC/app/build.gradle.kts` with:

```kotlin
plugins {
    alias(libs.plugins.android.application)
}

android {
    namespace = "co.cnewb.yammic"
    compileSdk {
        version = release(36) {
            minorApiLevel = 1
        }
    }

    defaultConfig {
        applicationId = "co.cnewb.yammic"
        minSdk = 31
        targetSdk = 36
        versionCode = 1
        versionName = "1.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    buildTypes {
        release {
            optimization {
                enable = false
            }
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }
    buildFeatures {
        viewBinding = true
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.lifecycle.livedata.ktx)
    implementation(libs.androidx.lifecycle.viewmodel.ktx)
    implementation(libs.androidx.activity.ktx)
    implementation(libs.androidx.appcompat)
    implementation(libs.androidx.material)
    implementation(libs.androidx.fragment.ktx)
    implementation(libs.androidx.navigation.fragment.ktx)
    implementation(libs.androidx.navigation.ui.ktx)
    testImplementation(libs.junit)
    androidTestImplementation(libs.androidx.junit)
    androidTestImplementation(libs.androidx.espresso.core)
}
```

This drops `buildFeatures.compose = true` and every `androidx.compose.*`/`ui-tooling`/`ui-test` dependency, turns on `viewBinding`, and adds AppCompat/Material/Fragment/Navigation/Activity/Lifecycle.

- [ ] **Step 4: Verify the dependency change resolves**

Run: `./gradlew.bat :app:dependencies --configuration debugRuntimeClasspath`
Expected: completes without "could not resolve" errors (the build will still fail to *compile* at this point, since `MainActivity.kt` still references Compose APIs that no longer exist — that's expected and gets fixed in Task 7. This step only confirms the new dependency graph resolves.)

---

### Task 2: Theming and shared resources

**Files:**
- Modify: `YammiC/app/src/main/res/values/colors.xml`
- Create: `YammiC/app/src/main/res/values-night/colors.xml`
- Modify: `YammiC/app/src/main/res/values/themes.xml`
- Modify: `YammiC/app/src/main/res/values/strings.xml`
- Create: `YammiC/app/src/main/res/drawable/ic_arrow_back.xml`

**Interfaces:**
- Produces: theme attributes (`colorPrimary`, `colorSurface`, `colorOnSurface`, `colorError`) that every later layout task relies on implicitly via Material widget styling; `@drawable/ic_arrow_back` (consumed by Settings/Pairing/Navigation-settings toolbars in Tasks 9–11); the full `strings.xml` string set (consumed by every layout/Fragment task).

- [ ] **Step 1: Light-mode colors**

Replace the full contents of `YammiC/app/src/main/res/values/colors.xml` with:

```xml
<?xml version="1.0" encoding="utf-8"?>
<resources>
    <color name="surface">#FFFFFF</color>
    <color name="accent">#E91E63</color>
    <color name="on_surface">#1A1A1A</color>
    <color name="muted">#6B6B6B</color>
    <color name="error">#B00020</color>
</resources>
```

- [ ] **Step 2: Dark-mode color override**

Create `YammiC/app/src/main/res/values-night/colors.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<resources>
    <color name="surface">#000000</color>
    <color name="accent">#26A69A</color>
    <color name="on_surface">#F5F5F5</color>
    <color name="muted">#9AA0A6</color>
    <color name="error">#FF6E6E</color>
</resources>
```

- [ ] **Step 3: Theme**

Replace the full contents of `YammiC/app/src/main/res/values/themes.xml` with:

```xml
<?xml version="1.0" encoding="utf-8"?>
<resources>

    <style name="Theme.YammiC" parent="Theme.MaterialComponents.DayNight.NoActionBar">
        <item name="colorPrimary">@color/accent</item>
        <item name="colorPrimaryDark">@color/accent</item>
        <item name="colorAccent">@color/accent</item>
        <item name="colorSurface">@color/surface</item>
        <item name="android:colorBackground">@color/surface</item>
        <item name="colorOnSurface">@color/on_surface</item>
        <item name="colorOnBackground">@color/on_surface</item>
        <item name="colorError">@color/error</item>
    </style>

</resources>
```

- [ ] **Step 4: Strings**

Replace the full contents of `YammiC/app/src/main/res/values/strings.xml` with:

```xml
<resources>
    <string name="app_name">YammiC</string>

    <string name="settings_title">Settings</string>
    <string name="pairing_title">Pairing</string>
    <string name="navigation_settings_title">Navigation</string>

    <string name="notification_access_required">Notification access required for music</string>
    <string name="grant">Grant</string>

    <string name="connect">Connect</string>
    <string name="disconnect">Disconnect</string>

    <string name="media_card_title">Now playing</string>
    <string name="media_nothing_playing">Nothing playing</string>

    <string name="vpb_card_title">Virtual Pit Board</string>
    <string name="vpb_status_format">Displaying: %1$s</string>

    <string name="navigation_card_title">Navigation</string>
    <string name="navigation_status_inactive">Inactive</string>
    <string name="navigation_status_connecting">Connecting…</string>
    <string name="navigation_status_active">Active (%1$d fps)</string>
    <string name="navigation_status_error">Error: %1$s</string>
    <string name="navigation_settings_toggle">Navigation screen</string>
    <string name="navigation_settings_status_format">Status: %1$s</string>

    <string name="log_card_title">Debug log</string>
    <string name="clear_logs">Clear logs</string>

    <string name="ble_name_format">BLE name: %1$s</string>
    <string name="status_format">Status: %1$s</string>
    <string name="odometer_placeholder">Odometer: --</string>

    <string name="music_metadata_setting">Music metadata</string>
    <string name="phone_metadata_setting">Phone metadata</string>
    <string name="navigation_setting">Navigation</string>
    <string name="vpb_setting">Virtual Pit Board support</string>
    <string name="auto_reconnect_setting">Auto-reconnect (run in background)</string>

    <string name="pairing_row_title">Pairing</string>
    <string name="pairing_row_subtitle">CCU ID, PassKey, and device name</string>
    <string name="ccu_id_label">CCU ID</string>
    <string name="pass_key_label">PassKey</string>
    <string name="device_name_label">Device name</string>
    <string name="save">Save</string>
</resources>
```

- [ ] **Step 5: Back-arrow icon**

Create `YammiC/app/src/main/res/drawable/ic_arrow_back.xml`:

```xml
<vector xmlns:android="http://schemas.android.com/apk/res/android"
    android:width="24dp"
    android:height="24dp"
    android:viewportWidth="24"
    android:viewportHeight="24"
    android:tint="?attr/colorControlNormal">
    <path
        android:fillColor="@android:color/white"
        android:pathData="M20,11H7.83l5.59,-5.59L12,4l-8,8 8,8 1.41,-1.41L7.83,13H20v-2z"/>
</vector>
```

- [ ] **Step 6: Verify resources compile**

Run: `./gradlew.bat :app:compileDebugKotlin`
Expected: still fails (Compose references in `MainActivity.kt` remain until Task 7), but the failure should be Kotlin compile errors about Compose symbols, **not** resource-linking errors about colors/strings/drawables. If AAPT errors appear instead, fix the resource XML before continuing.

---

### Task 3: Connect-button state derivation + connection "Done" fix

**Files:**
- Create: `YammiC/app/src/main/java/co/cnewb/yammic/ui/ConnectButtonState.kt`
- Create: `YammiC/app/src/test/java/co/cnewb/yammic/ui/ConnectButtonStateTest.kt`
- Modify: `YammiC/app/src/main/java/co/cnewb/yammic/ymcsdk/YmcConnectionBroker.kt:322-325`

**Interfaces:**
- Produces: `enum class ConnectButtonState { IDLE, CONNECTING, CONNECTED }` and `fun deriveConnectButtonState(connectionLabel: String, isBusy: Boolean): ConnectButtonState`, both consumed by `BikeViewModel` (Task 6) and `DashboardFragment` (Task 8).

**Context:** `YmcConnectionBroker.pushState(label)` sets `BikeDebugState.isBusy = label !in setOf("Done", "Idle", "Disconnected")`, but nothing in the current connect flow ever pushes the `"Done"` label — after `IdentifyAppRequest` is sent, `messageBroker` is assigned directly without a further `pushState` call, so `connectionLabel` stays `"Identifying"` and `isBusy` stays `true` forever after a successful connect. The new 3-state Connect/Disconnect button (Idle → Connecting → Connected) needs a real "connected" signal in the observable state, so this task adds the missing `pushState("Done")` call.

- [ ] **Step 1: Write the failing test**

Create `YammiC/app/src/test/java/co/cnewb/yammic/ui/ConnectButtonStateTest.kt`:

```kotlin
package co.cnewb.yammic.ui

import org.junit.Assert.assertEquals
import org.junit.Test

class ConnectButtonStateTest {
    @Test
    fun `idle when not busy and not done`() {
        assertEquals(ConnectButtonState.IDLE, deriveConnectButtonState("Idle", isBusy = false))
    }

    @Test
    fun `connecting while busy`() {
        assertEquals(ConnectButtonState.CONNECTING, deriveConnectButtonState("Scanning", isBusy = true))
    }

    @Test
    fun `connected once label reaches Done`() {
        assertEquals(ConnectButtonState.CONNECTED, deriveConnectButtonState("Done", isBusy = false))
    }

    @Test
    fun `idle after disconnect`() {
        assertEquals(ConnectButtonState.IDLE, deriveConnectButtonState("Disconnected", isBusy = false))
    }
}
```

- [ ] **Step 2: Run it to confirm it fails to compile**

Run: `./gradlew.bat :app:testDebugUnitTest --tests "co.cnewb.yammic.ui.ConnectButtonStateTest"`
Expected: FAIL — `ConnectButtonState`/`deriveConnectButtonState` unresolved references.

- [ ] **Step 3: Implement `ConnectButtonState`**

Create `YammiC/app/src/main/java/co/cnewb/yammic/ui/ConnectButtonState.kt`:

```kotlin
package co.cnewb.yammic.ui

enum class ConnectButtonState { IDLE, CONNECTING, CONNECTED }

fun deriveConnectButtonState(
    connectionLabel: String,
    isBusy: Boolean,
): ConnectButtonState =
    when {
        connectionLabel == "Done" -> ConnectButtonState.CONNECTED
        isBusy -> ConnectButtonState.CONNECTING
        else -> ConnectButtonState.IDLE
    }
```

- [ ] **Step 4: Run the test to confirm it passes**

Run: `./gradlew.bat :app:testDebugUnitTest --tests "co.cnewb.yammic.ui.ConnectButtonStateTest"`
Expected: PASS (4/4).

- [ ] **Step 5: Fix the missing "Done" state push**

In `YammiC/app/src/main/java/co/cnewb/yammic/ymcsdk/YmcConnectionBroker.kt`, find:

```kotlin
            messageBroker = localBroker
            runOnConnectJobs(localBroker)
            startConnectedJobs(localBroker)
            pushLog("Connected!")
```

Replace with:

```kotlin
            messageBroker = localBroker
            runOnConnectJobs(localBroker)
            startConnectedJobs(localBroker)
            pushState("Done")
            pushLog("Connected!")
```

- [ ] **Step 6: Verify the module still compiles**

Run: `./gradlew.bat :app:compileDebugKotlin`
Expected: still fails on the pre-existing Compose references in `MainActivity.kt` (fixed in Task 7) — but no new errors from `YmcConnectionBroker.kt` or the new `ui` package.

---

### Task 4: Expose live media metadata and VPB status

**Files:**
- Modify: `YammiC/app/src/main/java/co/cnewb/yammic/bike/MediaStateMonitor.kt`
- Modify: `YammiC/app/src/main/java/co/cnewb/yammic/bike/BikeForegroundService.kt`

**Interfaces:**
- Produces: `MediaStateMonitor.latestMetadata: StateFlow<YmcMediaMetaData?>`, `BikeForegroundService.mediaMetadata: StateFlow<YmcMediaMetaData?>`, `BikeForegroundService.vpbStatus: StateFlow<String>` — all consumed by `BikeViewModel` (Task 6).

**Context:** The media metadata card and VPB card need to display *current* state, but today's `MediaStateMonitor`/`BikeForegroundService` only ever push metadata *to the CCU* — nothing keeps the latest value around for the UI to read. This task adds that without changing any existing on-the-wire behavior.

- [ ] **Step 1: Add `latestMetadata` to `MediaStateMonitor`**

In `YammiC/app/src/main/java/co/cnewb/yammic/bike/MediaStateMonitor.kt`, add to the imports:

```kotlin
import co.cnewb.yammic.ymcsdk.models.YmcMediaMetaData
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
```

Add this property right after the `musicMetadataProvider` field:

```kotlin
    private val _latestMetadata = MutableStateFlow<YmcMediaMetaData?>(null)
    val latestMetadata = _latestMetadata.asStateFlow()
```

Change `pushMetadata()` from:

```kotlin
    fun pushMetadata() {
        pendingPushJob?.cancel()
        pendingPushJob =
            scope.launch {
                try {
                    delay(200.milliseconds)
                    val broker = getBroker() ?: return@launch
                    broker.sendMusicMetaDataResponse(musicMetadataProvider.getMetadata())
                } catch (t: Throwable) {
                    // Silently ignore transient BLE errors; the next state change will retry.
                }
            }
    }
```

to:

```kotlin
    fun pushMetadata() {
        // Update the UI-facing snapshot immediately; the BLE send below is separately
        // debounced and re-reads metadata after the delay so its own behavior is unchanged.
        _latestMetadata.value = musicMetadataProvider.getMetadata()
        pendingPushJob?.cancel()
        pendingPushJob =
            scope.launch {
                try {
                    delay(200.milliseconds)
                    val broker = getBroker() ?: return@launch
                    broker.sendMusicMetaDataResponse(musicMetadataProvider.getMetadata())
                } catch (t: Throwable) {
                    // Silently ignore transient BLE errors; the next state change will retry.
                }
            }
    }
```

- [ ] **Step 2: Expose `mediaMetadata` and `vpbStatus` from `BikeForegroundService`**

In `YammiC/app/src/main/java/co/cnewb/yammic/bike/BikeForegroundService.kt`, add to the imports:

```kotlin
import co.cnewb.yammic.ymcsdk.models.YmcMediaMetaData
import kotlinx.coroutines.flow.StateFlow
```

Add this property right after `val navigationEnabled = _navigationEnabled.asStateFlow()`:

```kotlin
    private val _vpbStatus = MutableStateFlow("Idle")
    val vpbStatus = _vpbStatus.asStateFlow()

    val mediaMetadata: StateFlow<YmcMediaMetaData?> get() = mediaStateMonitor.latestMetadata
```

- [ ] **Step 3: Update `setVpbEnabled` to track status**

Replace:

```kotlin
    fun setVpbEnabled(enabled: Boolean) {
        _vpbEnabled.value = enabled
        if (!enabled && controller.isBikeConnected) {
            scope.launch {
                try {
                    controller.clearVirtualPitBoard()
                } catch (t: Throwable) {
                    logger.debug("setVpbEnabled: clearVirtualPitBoard failed: ${t.message}")
                }
            }
        }
    }
```

with:

```kotlin
    fun setVpbEnabled(enabled: Boolean) {
        _vpbEnabled.value = enabled
        if (!enabled) {
            _vpbStatus.value = "Disabled"
            if (controller.isBikeConnected) {
                scope.launch {
                    try {
                        controller.clearVirtualPitBoard()
                    } catch (t: Throwable) {
                        logger.debug("setVpbEnabled: clearVirtualPitBoard failed: ${t.message}")
                    }
                }
            }
        } else {
            _vpbStatus.value = "Idle"
        }
    }
```

- [ ] **Step 4: Update the on-connect VPB sends to record status**

Replace:

```kotlin
            runOnConnectStep("send Connected message via VPB") {
                if (_vpbEnabled.value) {
                    val vpbConnected = controller.sendVirtualPitBoardMessage(buildConnectedVirtualPitBoardImage())
                    logger.debug("Virtual Pit Board Connected message result: $vpbConnected")
                } else {
                    logger.debug("VPB disabled, skipping Connected message")
                }
            }
```

with:

```kotlin
            runOnConnectStep("send Connected message via VPB") {
                if (_vpbEnabled.value) {
                    val vpbConnected = controller.sendVirtualPitBoardMessage(buildConnectedVirtualPitBoardImage())
                    logger.debug("Virtual Pit Board Connected message result: $vpbConnected")
                    if (vpbConnected) _vpbStatus.value = "Connected message"
                } else {
                    logger.debug("VPB disabled, skipping Connected message")
                }
            }
```

Replace:

```kotlin
            runOnConnectStep("send sandwich image via VPB (delayed 15s)") {
                delay(15.seconds)
                if (_vpbEnabled.value) {
                    val sandwichBitmap = ContextCompat.getDrawable(applicationContext, R.drawable.sandwich) as? BitmapDrawable
                    if (sandwichBitmap != null) {
                        val vpbImage = VpbImageBuilder.fromBitmap(sandwichBitmap.bitmap)
                        val result = controller.sendVirtualPitBoardMessage(vpbImage)
                        logger.debug("Sandwich image send result: $result")
                    } else {
                        logger.debug("Failed to load sandwich drawable")
                    }
                } else {
                    logger.debug("VPB disabled, skipping sandwich image")
                }
            }
```

with:

```kotlin
            runOnConnectStep("send sandwich image via VPB (delayed 15s)") {
                delay(15.seconds)
                if (_vpbEnabled.value) {
                    val sandwichBitmap = ContextCompat.getDrawable(applicationContext, R.drawable.sandwich) as? BitmapDrawable
                    if (sandwichBitmap != null) {
                        val vpbImage = VpbImageBuilder.fromBitmap(sandwichBitmap.bitmap)
                        val result = controller.sendVirtualPitBoardMessage(vpbImage)
                        logger.debug("Sandwich image send result: $result")
                        if (result) _vpbStatus.value = "Sandwich image"
                    } else {
                        logger.debug("Failed to load sandwich drawable")
                    }
                } else {
                    logger.debug("VPB disabled, skipping sandwich image")
                }
            }
```

- [ ] **Step 5: Update `sendNavRequestNotification` to record status**

Replace:

```kotlin
    private fun sendNavRequestNotification(message: String) {
        if (!_vpbEnabled.value) return
        scope.launch {
            try {
                val image = buildVirtualPitBoardImage(message, Color.GREEN)
                controller.sendVirtualPitBoardMessage(image)
            } catch (t: Throwable) {
                logger.debug("sendNavRequestNotification failed: ${t.message}")
            }
        }
    }
```

with:

```kotlin
    private fun sendNavRequestNotification(message: String) {
        if (!_vpbEnabled.value) return
        scope.launch {
            try {
                val image = buildVirtualPitBoardImage(message, Color.GREEN)
                val sent = controller.sendVirtualPitBoardMessage(image)
                if (sent) _vpbStatus.value = message
            } catch (t: Throwable) {
                logger.debug("sendNavRequestNotification failed: ${t.message}")
            }
        }
    }
```

- [ ] **Step 6: Verify**

Run: `./gradlew.bat :app:compileDebugKotlin`
Expected: still fails only on the pre-existing Compose references in `MainActivity.kt`; no new errors from `bike/MediaStateMonitor.kt` or `bike/BikeForegroundService.kt`.

---

### Task 5: Settings preferences (stub toggles)

**Files:**
- Create: `YammiC/app/src/main/java/co/cnewb/yammic/settings/SettingsPreferences.kt`

**Interfaces:**
- Produces: `class SettingsPreferences(context: Context)` with `var musicMetadataEnabled: Boolean`, `var phoneMetadataEnabled: Boolean`, `var autoReconnectEnabled: Boolean` — consumed by `SettingsFragment` (Task 9).

- [ ] **Step 1: Implement the preferences wrapper**

Create `YammiC/app/src/main/java/co/cnewb/yammic/settings/SettingsPreferences.kt`:

```kotlin
package co.cnewb.yammic.settings

import android.content.Context

/**
 * UI-only preference store for the Settings page's stub toggles. Nothing else in the app
 * reads these yet — Music metadata, Phone metadata, and Auto-reconnect have no backing
 * monitor/service logic wired to them (see the design spec's Non-goals). Wiring real
 * behavior to these values is a separate follow-up project.
 */
class SettingsPreferences(context: Context) {
    private val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    var musicMetadataEnabled: Boolean
        get() = prefs.getBoolean(KEY_MUSIC_METADATA, true)
        set(value) = prefs.edit().putBoolean(KEY_MUSIC_METADATA, value).apply()

    var phoneMetadataEnabled: Boolean
        get() = prefs.getBoolean(KEY_PHONE_METADATA, true)
        set(value) = prefs.edit().putBoolean(KEY_PHONE_METADATA, value).apply()

    var autoReconnectEnabled: Boolean
        get() = prefs.getBoolean(KEY_AUTO_RECONNECT, false)
        set(value) = prefs.edit().putBoolean(KEY_AUTO_RECONNECT, value).apply()

    private companion object {
        const val PREFS_NAME = "yammic_settings"
        const val KEY_MUSIC_METADATA = "music_metadata_enabled"
        const val KEY_PHONE_METADATA = "phone_metadata_enabled"
        const val KEY_AUTO_RECONNECT = "auto_reconnect_enabled"
    }
}
```

- [ ] **Step 2: Verify**

Run: `./gradlew.bat :app:compileDebugKotlin`
Expected: still fails only on the pre-existing Compose references in `MainActivity.kt`; no new errors from the `settings` package.

---

### Task 6: `BikeViewModel`

**Files:**
- Create: `YammiC/app/src/main/java/co/cnewb/yammic/ui/BikeViewModel.kt`

**Interfaces:**
- Consumes: `BikeForegroundService` (`co.cnewb.yammic.bike.BikeForegroundService`, `.controller`, `.vpbEnabled`, `.navigationEnabled`, `.vpbStatus`, `.mediaMetadata`, `.navigation`, `.setVpbEnabled(Boolean)`, `.setNavigationEnabled(Boolean)`, `.restartPhoneCallMonitor()` — all from Task 4 and pre-existing code); `YmcConnectionBroker` (`.state: StateFlow<BikeDebugState>`, `.setPermissionsGranted(Boolean)`, `.connectAndSend()`, `.disconnect()`, `.clearLogs()`, `.updateCredentials(String, String)`, `.updateDeviceName(String)`, `.logThrowable(String, Throwable)` — all pre-existing); `deriveConnectButtonState` (Task 3).
- Produces: `class BikeViewModel(application: Application) : AndroidViewModel(application)` exposing `connectionState: LiveData<BikeDebugState>`, `connectButtonState: LiveData<ConnectButtonState>`, `vpbEnabled: LiveData<Boolean>`, `navigationEnabled: LiveData<Boolean>`, `vpbStatus: LiveData<String>`, `mediaMetadata: LiveData<YmcMediaMetaData?>`, `navigationState: LiveData<NavigationState>`, plus action methods `onPermissionsGranted(Boolean)`, `disconnect()`, `clearLogs()`, `updateCredentials(String, String)`, `updateDeviceName(String)`, `setVpbEnabled(Boolean)`, `setNavigationEnabled(Boolean)`, `logThrowable(String, Throwable)`. Consumed by `MainActivity` (Task 7) and every Fragment (Tasks 8–11) via `activityViewModels()`.

- [ ] **Step 1: Implement `BikeViewModel`**

Create `YammiC/app/src/main/java/co/cnewb/yammic/ui/BikeViewModel.kt`:

```kotlin
package co.cnewb.yammic.ui

import android.app.Application
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.os.IBinder
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData
import androidx.lifecycle.map
import androidx.lifecycle.viewModelScope
import co.cnewb.yammic.bike.BikeDebugState
import co.cnewb.yammic.bike.BikeForegroundService
import co.cnewb.yammic.ymcsdk.YmcConnectionBroker
import co.cnewb.yammic.ymcsdk.models.YmcMediaMetaData
import co.cnewb.yammic.ymcsdk.navigation.NavigationState
import kotlinx.coroutines.launch

/**
 * Activity-scoped ViewModel that owns the [BikeForegroundService] binding so every Fragment
 * observes the same live connection/feature state instead of each re-binding the service.
 */
class BikeViewModel(application: Application) : AndroidViewModel(application) {

    private var controller: YmcConnectionBroker? = null
    private var service: BikeForegroundService? = null

    private val _connectionState = MutableLiveData(BikeDebugState())
    val connectionState: LiveData<BikeDebugState> = _connectionState

    val connectButtonState: LiveData<ConnectButtonState> =
        _connectionState.map { deriveConnectButtonState(it.connectionLabel, it.isBusy) }

    private val _vpbEnabled = MutableLiveData(true)
    val vpbEnabled: LiveData<Boolean> = _vpbEnabled

    private val _navigationEnabled = MutableLiveData(true)
    val navigationEnabled: LiveData<Boolean> = _navigationEnabled

    private val _vpbStatus = MutableLiveData("Idle")
    val vpbStatus: LiveData<String> = _vpbStatus

    private val _mediaMetadata = MutableLiveData<YmcMediaMetaData?>(null)
    val mediaMetadata: LiveData<YmcMediaMetaData?> = _mediaMetadata

    private val _navigationState = MutableLiveData<NavigationState>(NavigationState.Idle)
    val navigationState: LiveData<NavigationState> = _navigationState

    private val serviceConnection =
        object : ServiceConnection {
            override fun onServiceConnected(
                name: ComponentName,
                binder: IBinder,
            ) {
                val bound = (binder as BikeForegroundService.LocalBinder).service
                service = bound
                controller = bound.controller
                viewModelScope.launch { bound.controller.state.collect { _connectionState.value = it } }
                viewModelScope.launch { bound.vpbEnabled.collect { _vpbEnabled.value = it } }
                viewModelScope.launch { bound.navigationEnabled.collect { _navigationEnabled.value = it } }
                viewModelScope.launch { bound.vpbStatus.collect { _vpbStatus.value = it } }
                viewModelScope.launch { bound.mediaMetadata.collect { _mediaMetadata.value = it } }
                viewModelScope.launch { bound.navigation.state.collect { _navigationState.value = it } }
            }

            override fun onServiceDisconnected(name: ComponentName) {
                service = null
                controller = null
            }
        }

    init {
        val context = getApplication<Application>()
        val intent = Intent(context, BikeForegroundService::class.java)
        context.startService(intent)
        context.bindService(intent, serviceConnection, Context.BIND_AUTO_CREATE)
    }

    fun onPermissionsGranted(granted: Boolean) {
        controller?.setPermissionsGranted(granted)
        service?.restartPhoneCallMonitor()
        if (granted) controller?.connectAndSend()
    }

    fun disconnect() = controller?.disconnect()

    fun clearLogs() = controller?.clearLogs()

    fun updateCredentials(
        ccuId: String,
        passKey: String,
    ) = controller?.updateCredentials(ccuId, passKey)

    fun updateDeviceName(name: String) = controller?.updateDeviceName(name)

    fun setVpbEnabled(enabled: Boolean) = service?.setVpbEnabled(enabled)

    fun setNavigationEnabled(enabled: Boolean) = service?.setNavigationEnabled(enabled)

    fun logThrowable(
        tag: String,
        throwable: Throwable,
    ) = controller?.logThrowable(tag, throwable)

    override fun onCleared() {
        try {
            getApplication<Application>().unbindService(serviceConnection)
        } catch (_: Exception) {
        }
        super.onCleared()
    }
}
```

- [ ] **Step 2: Verify**

Run: `./gradlew.bat :app:compileDebugKotlin`
Expected: still fails only on the pre-existing Compose references in `MainActivity.kt`; no new errors from the new `ui/BikeViewModel.kt`.

---

### Task 7: Navigation graph, activity shell, and `MainActivity` rewrite

**Files:**
- Create: `YammiC/app/src/main/res/navigation/nav_graph.xml`
- Create: `YammiC/app/src/main/res/layout/activity_main.xml`
- Modify: `YammiC/app/src/main/java/co/cnewb/yammic/MainActivity.kt` (full rewrite — delete all Composables)

**Interfaces:**
- Consumes: `BikeViewModel` (Task 6); fragment classes referenced by fully-qualified name in `nav_graph.xml` (`co.cnewb.yammic.ui.dashboard.DashboardFragment`, `co.cnewb.yammic.ui.settings.SettingsFragment`, `co.cnewb.yammic.ui.settings.PairingFragment`, `co.cnewb.yammic.ui.navigation.NavigationSettingsFragment` — created in Tasks 8–11; referencing them here by string is safe before they exist, since Navigation Component resolves them reflectively at runtime, not at compile time).
- Produces: action IDs `R.id.action_dashboard_to_settings`, `R.id.action_dashboard_to_navigationSettings`, `R.id.action_settings_to_pairing` — consumed by `DashboardFragment` (Task 8) and `SettingsFragment` (Task 9).

- [ ] **Step 1: Navigation graph**

Create `YammiC/app/src/main/res/navigation/nav_graph.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<navigation xmlns:android="http://schemas.android.com/apk/res/android"
    xmlns:app="http://schemas.android.com/apk/res-auto"
    android:id="@+id/nav_graph"
    app:startDestination="@id/dashboardFragment">

    <fragment
        android:id="@+id/dashboardFragment"
        android:name="co.cnewb.yammic.ui.dashboard.DashboardFragment"
        android:label="@string/app_name">
        <action
            android:id="@+id/action_dashboard_to_settings"
            app:destination="@id/settingsFragment" />
        <action
            android:id="@+id/action_dashboard_to_navigationSettings"
            app:destination="@id/navigationSettingsFragment" />
    </fragment>

    <fragment
        android:id="@+id/settingsFragment"
        android:name="co.cnewb.yammic.ui.settings.SettingsFragment"
        android:label="@string/settings_title">
        <action
            android:id="@+id/action_settings_to_pairing"
            app:destination="@id/pairingFragment" />
    </fragment>

    <fragment
        android:id="@+id/pairingFragment"
        android:name="co.cnewb.yammic.ui.settings.PairingFragment"
        android:label="@string/pairing_title" />

    <fragment
        android:id="@+id/navigationSettingsFragment"
        android:name="co.cnewb.yammic.ui.navigation.NavigationSettingsFragment"
        android:label="@string/navigation_settings_title" />

</navigation>
```

- [ ] **Step 2: Activity layout**

Create `YammiC/app/src/main/res/layout/activity_main.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<FrameLayout xmlns:android="http://schemas.android.com/apk/res/android"
    xmlns:app="http://schemas.android.com/apk/res-auto"
    android:layout_width="match_parent"
    android:layout_height="match_parent">

    <androidx.fragment.app.FragmentContainerView
        android:id="@+id/nav_host_fragment"
        android:name="androidx.navigation.fragment.NavHostFragment"
        android:layout_width="match_parent"
        android:layout_height="match_parent"
        app:defaultNavHost="true"
        app:navGraph="@navigation/nav_graph" />

</FrameLayout>
```

- [ ] **Step 3: Rewrite `MainActivity.kt`**

Replace the full contents of `YammiC/app/src/main/java/co/cnewb/yammic/MainActivity.kt` with:

```kotlin
package co.cnewb.yammic

import android.os.Bundle
import android.util.Log
import androidx.activity.viewModels
import androidx.appcompat.app.AppCompatActivity
import co.cnewb.yammic.databinding.ActivityMainBinding
import co.cnewb.yammic.ui.BikeViewModel

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private val bikeViewModel: BikeViewModel by viewModels()

    private var previousUncaughtHandler: Thread.UncaughtExceptionHandler? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        previousUncaughtHandler = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, throwable ->
            try {
                bikeViewModel.logThrowable("uncaught:${thread.name}", throwable)
            } catch (_: Throwable) {
                Log.e("YammiC", "Uncaught exception", throwable)
            }
            previousUncaughtHandler?.uncaughtException(thread, throwable)
        }

        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)
    }

    override fun onDestroy() {
        Thread.setDefaultUncaughtExceptionHandler(previousUncaughtHandler)
        super.onDestroy()
    }
}
```

- [ ] **Step 4: Verify**

Run: `./gradlew.bat :app:compileDebugKotlin`
Expected: PASS. This is the first fully-green compile since Task 1 — all Compose references are gone. (The app is not yet runnable end-to-end: `nav_graph.xml` points at Fragment classes that don't exist until Tasks 8–11, so launching it now would crash with a `ClassNotFoundException`. That's expected mid-plan.)

---

### Task 8: `DashboardFragment`

**Files:**
- Create: `YammiC/app/src/main/res/menu/dashboard_menu.xml`
- Create: `YammiC/app/src/main/res/layout/fragment_dashboard.xml`
- Create: `YammiC/app/src/main/java/co/cnewb/yammic/ui/navigation/NavigationStateFormatter.kt`
- Create: `YammiC/app/src/main/java/co/cnewb/yammic/ui/dashboard/DashboardFragment.kt`

**Interfaces:**
- Consumes: `BikeViewModel` (Task 6), `ConnectButtonState` (Task 3), `R.id.action_dashboard_to_settings` / `R.id.action_dashboard_to_navigationSettings` (Task 7).
- Produces: `fun describeNavigationState(context: Context, state: NavigationState): String`, reused by `NavigationSettingsFragment` (Task 11).

- [ ] **Step 1: Toolbar menu**

Create `YammiC/app/src/main/res/menu/dashboard_menu.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<menu xmlns:android="http://schemas.android.com/apk/res/android"
    xmlns:app="http://schemas.android.com/apk/res-auto">
    <item
        android:id="@+id/action_settings"
        android:icon="@android:drawable/ic_menu_manage"
        android:title="@string/settings_title"
        app:showAsAction="always" />
</menu>
```

- [ ] **Step 2: Layout**

Create `YammiC/app/src/main/res/layout/fragment_dashboard.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<androidx.core.widget.NestedScrollView xmlns:android="http://schemas.android.com/apk/res/android"
    xmlns:app="http://schemas.android.com/apk/res-auto"
    xmlns:tools="http://schemas.android.com/tools"
    android:layout_width="match_parent"
    android:layout_height="match_parent"
    android:fillViewport="true">

    <LinearLayout
        android:layout_width="match_parent"
        android:layout_height="wrap_content"
        android:orientation="vertical"
        android:padding="16dp">

        <com.google.android.material.appbar.MaterialToolbar
            android:id="@+id/toolbar"
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:title="@string/app_name"
            app:menu="@menu/dashboard_menu" />

        <com.google.android.material.card.MaterialCardView
            android:id="@+id/notificationAccessCard"
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="12dp"
            android:visibility="gone">

            <LinearLayout
                android:layout_width="match_parent"
                android:layout_height="wrap_content"
                android:orientation="horizontal"
                android:gravity="center_vertical"
                android:padding="12dp">

                <TextView
                    android:layout_width="0dp"
                    android:layout_height="wrap_content"
                    android:layout_weight="1"
                    android:text="@string/notification_access_required" />

                <Button
                    android:id="@+id/grantNotificationAccessButton"
                    style="@style/Widget.MaterialComponents.Button.TextButton"
                    android:layout_width="wrap_content"
                    android:layout_height="wrap_content"
                    android:text="@string/grant" />

            </LinearLayout>
        </com.google.android.material.card.MaterialCardView>

        <TextView
            android:id="@+id/bikeNameText"
            android:layout_width="wrap_content"
            android:layout_height="wrap_content"
            android:layout_marginTop="16dp"
            android:textAppearance="?attr/textAppearanceHeadline5"
            tools:text="YCCU_01234567890123" />

        <LinearLayout
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="8dp"
            android:orientation="horizontal">

            <TextView
                android:id="@+id/bleNameChip"
                android:layout_width="0dp"
                android:layout_height="wrap_content"
                android:layout_weight="1"
                tools:text="BLE name: YCCU_01234567890123" />

            <TextView
                android:id="@+id/connectionStatusChip"
                android:layout_width="0dp"
                android:layout_height="wrap_content"
                android:layout_weight="1"
                tools:text="Status: Idle" />

            <TextView
                android:id="@+id/odometerChip"
                android:layout_width="0dp"
                android:layout_height="wrap_content"
                android:layout_weight="1"
                tools:text="Odometer: --" />

        </LinearLayout>

        <TextView
            android:id="@+id/lastErrorText"
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="8dp"
            android:textColor="?attr/colorError"
            android:visibility="gone"
            tools:text="Connection failed" />

        <LinearLayout
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="16dp"
            android:orientation="horizontal">

            <Button
                android:id="@+id/connectButton"
                android:layout_width="0dp"
                android:layout_height="wrap_content"
                android:layout_weight="1"
                android:layout_marginEnd="8dp"
                android:text="@string/connect" />

            <Button
                android:id="@+id/disconnectButton"
                style="@style/Widget.MaterialComponents.Button.OutlinedButton"
                android:layout_width="0dp"
                android:layout_height="wrap_content"
                android:layout_weight="1"
                android:enabled="false"
                android:text="@string/disconnect" />

        </LinearLayout>

        <com.google.android.material.card.MaterialCardView
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="16dp">

            <LinearLayout
                android:layout_width="match_parent"
                android:layout_height="wrap_content"
                android:orientation="vertical"
                android:padding="16dp">

                <TextView
                    android:layout_width="wrap_content"
                    android:layout_height="wrap_content"
                    android:text="@string/media_card_title"
                    android:textAppearance="?attr/textAppearanceSubtitle1" />

                <TextView
                    android:id="@+id/mediaTrackText"
                    android:layout_width="match_parent"
                    android:layout_height="wrap_content"
                    android:layout_marginTop="8dp"
                    tools:text="Song Title" />

                <TextView
                    android:id="@+id/mediaArtistText"
                    android:layout_width="match_parent"
                    android:layout_height="wrap_content"
                    tools:text="Artist Name" />

                <TextView
                    android:id="@+id/mediaStateText"
                    android:layout_width="match_parent"
                    android:layout_height="wrap_content"
                    tools:text="Playing" />

            </LinearLayout>
        </com.google.android.material.card.MaterialCardView>

        <com.google.android.material.card.MaterialCardView
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="16dp">

            <LinearLayout
                android:layout_width="match_parent"
                android:layout_height="wrap_content"
                android:orientation="vertical"
                android:padding="16dp">

                <LinearLayout
                    android:layout_width="match_parent"
                    android:layout_height="wrap_content"
                    android:orientation="horizontal"
                    android:gravity="center_vertical">

                    <TextView
                        android:layout_width="0dp"
                        android:layout_height="wrap_content"
                        android:layout_weight="1"
                        android:text="@string/vpb_card_title"
                        android:textAppearance="?attr/textAppearanceSubtitle1" />

                    <com.google.android.material.materialswitch.MaterialSwitch
                        android:id="@+id/vpbSwitch"
                        android:layout_width="wrap_content"
                        android:layout_height="wrap_content" />

                </LinearLayout>

                <TextView
                    android:id="@+id/vpbStatusText"
                    android:layout_width="match_parent"
                    android:layout_height="wrap_content"
                    android:layout_marginTop="8dp"
                    tools:text="Displaying: Connected message" />

            </LinearLayout>
        </com.google.android.material.card.MaterialCardView>

        <com.google.android.material.card.MaterialCardView
            android:id="@+id/navigationCard"
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="16dp"
            android:clickable="true"
            android:focusable="true"
            android:foreground="?attr/selectableItemBackground">

            <LinearLayout
                android:layout_width="match_parent"
                android:layout_height="wrap_content"
                android:orientation="vertical"
                android:padding="16dp">

                <TextView
                    android:layout_width="wrap_content"
                    android:layout_height="wrap_content"
                    android:text="@string/navigation_card_title"
                    android:textAppearance="?attr/textAppearanceSubtitle1" />

                <TextView
                    android:id="@+id/navigationStatusText"
                    android:layout_width="match_parent"
                    android:layout_height="wrap_content"
                    android:layout_marginTop="8dp"
                    tools:text="Status: Active (5 fps)" />

            </LinearLayout>
        </com.google.android.material.card.MaterialCardView>

        <com.google.android.material.card.MaterialCardView
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="16dp"
            android:layout_marginBottom="16dp">

            <LinearLayout
                android:layout_width="match_parent"
                android:layout_height="wrap_content"
                android:orientation="vertical"
                android:padding="16dp">

                <LinearLayout
                    android:layout_width="match_parent"
                    android:layout_height="wrap_content"
                    android:orientation="horizontal"
                    android:gravity="center_vertical">

                    <TextView
                        android:layout_width="0dp"
                        android:layout_height="wrap_content"
                        android:layout_weight="1"
                        android:text="@string/log_card_title"
                        android:textAppearance="?attr/textAppearanceSubtitle1" />

                    <Button
                        android:id="@+id/clearLogsButton"
                        style="@style/Widget.MaterialComponents.Button.TextButton"
                        android:layout_width="wrap_content"
                        android:layout_height="wrap_content"
                        android:text="@string/clear_logs" />

                </LinearLayout>

                <ScrollView
                    android:id="@+id/logScrollView"
                    android:layout_width="match_parent"
                    android:layout_height="200dp"
                    android:layout_marginTop="8dp">

                    <TextView
                        android:id="@+id/logText"
                        android:layout_width="match_parent"
                        android:layout_height="wrap_content"
                        android:fontFamily="monospace"
                        android:textSize="12sp"
                        tools:text="Scanning...\nConnecting...\nConnected!" />

                </ScrollView>

            </LinearLayout>
        </com.google.android.material.card.MaterialCardView>

    </LinearLayout>
</androidx.core.widget.NestedScrollView>
```

- [ ] **Step 3: Shared navigation-state formatter**

Create `YammiC/app/src/main/java/co/cnewb/yammic/ui/navigation/NavigationStateFormatter.kt`:

```kotlin
package co.cnewb.yammic.ui.navigation

import android.content.Context
import co.cnewb.yammic.R
import co.cnewb.yammic.ymcsdk.navigation.NavigationState

fun describeNavigationState(
    context: Context,
    state: NavigationState,
): String =
    when (state) {
        is NavigationState.Idle -> context.getString(R.string.navigation_status_inactive)
        is NavigationState.Connecting -> context.getString(R.string.navigation_status_connecting)
        is NavigationState.Active -> context.getString(R.string.navigation_status_active, state.fps)
        is NavigationState.Error -> context.getString(R.string.navigation_status_error, state.message)
    }
```

- [ ] **Step 4: `DashboardFragment`**

Create `YammiC/app/src/main/java/co/cnewb/yammic/ui/dashboard/DashboardFragment.kt`:

```kotlin
package co.cnewb.yammic.ui.dashboard

import android.Manifest
import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.activity.result.contract.ActivityResultContracts
import androidx.fragment.app.Fragment
import androidx.fragment.app.activityViewModels
import androidx.navigation.fragment.findNavController
import co.cnewb.yammic.R
import co.cnewb.yammic.bike.BikeDebugState
import co.cnewb.yammic.databinding.FragmentDashboardBinding
import co.cnewb.yammic.ui.BikeViewModel
import co.cnewb.yammic.ui.ConnectButtonState
import co.cnewb.yammic.ui.navigation.describeNavigationState

class DashboardFragment : Fragment() {

    private var _binding: FragmentDashboardBinding? = null
    private val binding get() = _binding!!

    private val viewModel: BikeViewModel by activityViewModels()

    private val permissionLauncher =
        registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { results ->
            viewModel.onPermissionsGranted(results.values.all { it })
        }

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentDashboardBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(
        view: View,
        savedInstanceState: Bundle?,
    ) {
        super.onViewCreated(view, savedInstanceState)

        binding.toolbar.setOnMenuItemClickListener { item ->
            if (item.itemId == R.id.action_settings) {
                findNavController().navigate(R.id.action_dashboard_to_settings)
                true
            } else {
                false
            }
        }

        binding.grantNotificationAccessButton.setOnClickListener {
            startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS))
        }

        binding.connectButton.setOnClickListener { requestPermissionsAndConnect() }
        binding.disconnectButton.setOnClickListener { viewModel.disconnect() }
        binding.clearLogsButton.setOnClickListener { viewModel.clearLogs() }
        binding.navigationCard.setOnClickListener {
            findNavController().navigate(R.id.action_dashboard_to_navigationSettings)
        }
        binding.vpbSwitch.setOnCheckedChangeListener { switchView, isChecked ->
            if (switchView.isPressed) viewModel.setVpbEnabled(isChecked)
        }

        observeState()
    }

    private fun observeState() {
        viewModel.connectionState.observe(viewLifecycleOwner) { renderState(it) }
        viewModel.connectButtonState.observe(viewLifecycleOwner) { renderConnectButtons(it) }
        viewModel.vpbEnabled.observe(viewLifecycleOwner) { enabled ->
            if (binding.vpbSwitch.isChecked != enabled) binding.vpbSwitch.isChecked = enabled
        }
        viewModel.vpbStatus.observe(viewLifecycleOwner) { status ->
            binding.vpbStatusText.text = getString(R.string.vpb_status_format, status)
        }
        viewModel.mediaMetadata.observe(viewLifecycleOwner) { metadata ->
            if (metadata == null) {
                binding.mediaTrackText.text = getString(R.string.media_nothing_playing)
                binding.mediaArtistText.text = ""
                binding.mediaStateText.text = ""
            } else {
                binding.mediaTrackText.text = metadata.songName.ifBlank { getString(R.string.media_nothing_playing) }
                binding.mediaArtistText.text = metadata.artistName
                binding.mediaStateText.text = metadata.playbackState.name
            }
        }
        viewModel.navigationState.observe(viewLifecycleOwner) { navState ->
            binding.navigationStatusText.text = describeNavigationState(requireContext(), navState)
        }
    }

    private fun renderState(state: BikeDebugState) {
        binding.bikeNameText.text = state.deviceName
        binding.bleNameChip.text = getString(R.string.ble_name_format, state.deviceName)
        binding.connectionStatusChip.text = getString(R.string.status_format, state.connectionLabel)
        binding.odometerChip.text = getString(R.string.odometer_placeholder)
        if (state.lastError.isNotBlank()) {
            binding.lastErrorText.visibility = View.VISIBLE
            binding.lastErrorText.text = state.lastError
        } else {
            binding.lastErrorText.visibility = View.GONE
        }
        binding.logText.text = state.logs.joinToString("\n")
        binding.logScrollView.post { binding.logScrollView.fullScroll(View.FOCUS_DOWN) }
    }

    private fun renderConnectButtons(buttonState: ConnectButtonState) {
        when (buttonState) {
            ConnectButtonState.IDLE -> {
                binding.connectButton.isEnabled = true
                binding.connectButton.isSelected = true
                binding.disconnectButton.isEnabled = false
            }
            ConnectButtonState.CONNECTING -> {
                binding.connectButton.isEnabled = false
                binding.connectButton.isSelected = false
                binding.disconnectButton.isEnabled = false
            }
            ConnectButtonState.CONNECTED -> {
                binding.connectButton.isEnabled = false
                binding.connectButton.isSelected = false
                binding.disconnectButton.isEnabled = true
            }
        }
    }

    private fun requestPermissionsAndConnect() {
        val permissions =
            buildList {
                add(Manifest.permission.ACCESS_FINE_LOCATION)
                add(Manifest.permission.READ_PHONE_STATE)
                add(Manifest.permission.READ_CONTACTS)
                add(Manifest.permission.ANSWER_PHONE_CALLS)
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                    add(Manifest.permission.BLUETOOTH_CONNECT)
                    add(Manifest.permission.BLUETOOTH_SCAN)
                }
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                    add(Manifest.permission.POST_NOTIFICATIONS)
                }
            }.toTypedArray()
        permissionLauncher.launch(permissions)
    }

    override fun onResume() {
        super.onResume()
        binding.notificationAccessCard.visibility = if (isNotificationListenerGranted()) View.GONE else View.VISIBLE
    }

    private fun isNotificationListenerGranted(): Boolean {
        val flat =
            Settings.Secure.getString(requireContext().contentResolver, "enabled_notification_listeners")
                ?: return false
        return flat.split(":").any { it.startsWith("${requireContext().packageName}/") }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
```

- [ ] **Step 5: Verify**

Run: `./gradlew.bat :app:compileDebugKotlin`
Expected: PASS.

---

### Task 9: `SettingsFragment`

**Files:**
- Create: `YammiC/app/src/main/res/layout/fragment_settings.xml`
- Create: `YammiC/app/src/main/java/co/cnewb/yammic/ui/settings/SettingsFragment.kt`

**Interfaces:**
- Consumes: `BikeViewModel` (Task 6), `SettingsPreferences` (Task 5), `R.id.action_settings_to_pairing` (Task 7).

- [ ] **Step 1: Layout**

Create `YammiC/app/src/main/res/layout/fragment_settings.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<androidx.core.widget.NestedScrollView xmlns:android="http://schemas.android.com/apk/res/android"
    xmlns:app="http://schemas.android.com/apk/res-auto"
    android:layout_width="match_parent"
    android:layout_height="match_parent"
    android:fillViewport="true">

    <LinearLayout
        android:layout_width="match_parent"
        android:layout_height="wrap_content"
        android:orientation="vertical"
        android:padding="16dp">

        <com.google.android.material.appbar.MaterialToolbar
            android:id="@+id/toolbar"
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:title="@string/settings_title"
            app:navigationIcon="@drawable/ic_arrow_back" />

        <LinearLayout
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="16dp"
            android:orientation="horizontal"
            android:gravity="center_vertical">
            <TextView
                android:layout_width="0dp"
                android:layout_height="wrap_content"
                android:layout_weight="1"
                android:text="@string/music_metadata_setting" />
            <com.google.android.material.materialswitch.MaterialSwitch
                android:id="@+id/musicMetadataSwitch"
                android:layout_width="wrap_content"
                android:layout_height="wrap_content" />
        </LinearLayout>

        <LinearLayout
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="16dp"
            android:orientation="horizontal"
            android:gravity="center_vertical">
            <TextView
                android:layout_width="0dp"
                android:layout_height="wrap_content"
                android:layout_weight="1"
                android:text="@string/phone_metadata_setting" />
            <com.google.android.material.materialswitch.MaterialSwitch
                android:id="@+id/phoneMetadataSwitch"
                android:layout_width="wrap_content"
                android:layout_height="wrap_content" />
        </LinearLayout>

        <LinearLayout
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="16dp"
            android:orientation="horizontal"
            android:gravity="center_vertical">
            <TextView
                android:layout_width="0dp"
                android:layout_height="wrap_content"
                android:layout_weight="1"
                android:text="@string/navigation_setting" />
            <com.google.android.material.materialswitch.MaterialSwitch
                android:id="@+id/navigationSettingSwitch"
                android:layout_width="wrap_content"
                android:layout_height="wrap_content" />
        </LinearLayout>

        <LinearLayout
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="16dp"
            android:orientation="horizontal"
            android:gravity="center_vertical">
            <TextView
                android:layout_width="0dp"
                android:layout_height="wrap_content"
                android:layout_weight="1"
                android:text="@string/vpb_setting" />
            <com.google.android.material.materialswitch.MaterialSwitch
                android:id="@+id/vpbSettingSwitch"
                android:layout_width="wrap_content"
                android:layout_height="wrap_content" />
        </LinearLayout>

        <LinearLayout
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="16dp"
            android:orientation="horizontal"
            android:gravity="center_vertical">
            <TextView
                android:layout_width="0dp"
                android:layout_height="wrap_content"
                android:layout_weight="1"
                android:text="@string/auto_reconnect_setting" />
            <com.google.android.material.materialswitch.MaterialSwitch
                android:id="@+id/autoReconnectSwitch"
                android:layout_width="wrap_content"
                android:layout_height="wrap_content" />
        </LinearLayout>

        <LinearLayout
            android:id="@+id/pairingRow"
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="24dp"
            android:orientation="vertical"
            android:clickable="true"
            android:focusable="true"
            android:background="?attr/selectableItemBackground"
            android:padding="12dp">
            <TextView
                android:layout_width="wrap_content"
                android:layout_height="wrap_content"
                android:text="@string/pairing_row_title"
                android:textAppearance="?attr/textAppearanceSubtitle1" />
            <TextView
                android:layout_width="wrap_content"
                android:layout_height="wrap_content"
                android:text="@string/pairing_row_subtitle" />
        </LinearLayout>

    </LinearLayout>
</androidx.core.widget.NestedScrollView>
```

- [ ] **Step 2: `SettingsFragment`**

Create `YammiC/app/src/main/java/co/cnewb/yammic/ui/settings/SettingsFragment.kt`:

```kotlin
package co.cnewb.yammic.ui.settings

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.fragment.app.activityViewModels
import androidx.navigation.fragment.findNavController
import co.cnewb.yammic.R
import co.cnewb.yammic.databinding.FragmentSettingsBinding
import co.cnewb.yammic.settings.SettingsPreferences
import co.cnewb.yammic.ui.BikeViewModel

class SettingsFragment : Fragment() {

    private var _binding: FragmentSettingsBinding? = null
    private val binding get() = _binding!!

    private val viewModel: BikeViewModel by activityViewModels()
    private lateinit var preferences: SettingsPreferences

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentSettingsBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(
        view: View,
        savedInstanceState: Bundle?,
    ) {
        super.onViewCreated(view, savedInstanceState)
        preferences = SettingsPreferences(requireContext())

        binding.toolbar.setNavigationOnClickListener { findNavController().navigateUp() }

        binding.musicMetadataSwitch.isChecked = preferences.musicMetadataEnabled
        binding.musicMetadataSwitch.setOnCheckedChangeListener { _, isChecked ->
            preferences.musicMetadataEnabled = isChecked
        }

        binding.phoneMetadataSwitch.isChecked = preferences.phoneMetadataEnabled
        binding.phoneMetadataSwitch.setOnCheckedChangeListener { _, isChecked ->
            preferences.phoneMetadataEnabled = isChecked
        }

        binding.autoReconnectSwitch.isChecked = preferences.autoReconnectEnabled
        binding.autoReconnectSwitch.setOnCheckedChangeListener { _, isChecked ->
            preferences.autoReconnectEnabled = isChecked
        }

        binding.navigationSettingSwitch.setOnCheckedChangeListener { switchView, isChecked ->
            if (switchView.isPressed) viewModel.setNavigationEnabled(isChecked)
        }
        viewModel.navigationEnabled.observe(viewLifecycleOwner) { enabled ->
            if (binding.navigationSettingSwitch.isChecked != enabled) binding.navigationSettingSwitch.isChecked = enabled
        }

        binding.vpbSettingSwitch.setOnCheckedChangeListener { switchView, isChecked ->
            if (switchView.isPressed) viewModel.setVpbEnabled(isChecked)
        }
        viewModel.vpbEnabled.observe(viewLifecycleOwner) { enabled ->
            if (binding.vpbSettingSwitch.isChecked != enabled) binding.vpbSettingSwitch.isChecked = enabled
        }

        binding.pairingRow.setOnClickListener {
            findNavController().navigate(R.id.action_settings_to_pairing)
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
```

- [ ] **Step 3: Verify**

Run: `./gradlew.bat :app:compileDebugKotlin`
Expected: PASS.

---

### Task 10: `PairingFragment`

**Files:**
- Create: `YammiC/app/src/main/res/layout/fragment_pairing.xml`
- Create: `YammiC/app/src/main/java/co/cnewb/yammic/ui/settings/PairingFragment.kt`

**Interfaces:**
- Consumes: `BikeViewModel` (Task 6), `.connectionState` (for `ccuId`/`passKey`/`deviceName`), `.updateCredentials(String, String)`, `.updateDeviceName(String)`.

- [ ] **Step 1: Layout**

Create `YammiC/app/src/main/res/layout/fragment_pairing.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<androidx.core.widget.NestedScrollView xmlns:android="http://schemas.android.com/apk/res/android"
    xmlns:app="http://schemas.android.com/apk/res-auto"
    android:layout_width="match_parent"
    android:layout_height="match_parent"
    android:fillViewport="true">

    <LinearLayout
        android:layout_width="match_parent"
        android:layout_height="wrap_content"
        android:orientation="vertical"
        android:padding="16dp">

        <com.google.android.material.appbar.MaterialToolbar
            android:id="@+id/toolbar"
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:title="@string/pairing_title"
            app:navigationIcon="@drawable/ic_arrow_back" />

        <com.google.android.material.textfield.TextInputLayout
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="16dp"
            android:hint="@string/ccu_id_label">
            <com.google.android.material.textfield.TextInputEditText
                android:id="@+id/ccuIdInput"
                android:layout_width="match_parent"
                android:layout_height="wrap_content" />
        </com.google.android.material.textfield.TextInputLayout>

        <com.google.android.material.textfield.TextInputLayout
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="8dp"
            android:hint="@string/pass_key_label">
            <com.google.android.material.textfield.TextInputEditText
                android:id="@+id/passKeyInput"
                android:layout_width="match_parent"
                android:layout_height="wrap_content" />
        </com.google.android.material.textfield.TextInputLayout>

        <com.google.android.material.textfield.TextInputLayout
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="8dp"
            android:hint="@string/device_name_label">
            <com.google.android.material.textfield.TextInputEditText
                android:id="@+id/deviceNameInput"
                android:layout_width="match_parent"
                android:layout_height="wrap_content" />
        </com.google.android.material.textfield.TextInputLayout>

        <Button
            android:id="@+id/saveButton"
            android:layout_width="wrap_content"
            android:layout_height="wrap_content"
            android:layout_marginTop="16dp"
            android:text="@string/save" />

    </LinearLayout>
</androidx.core.widget.NestedScrollView>
```

- [ ] **Step 2: `PairingFragment`**

Create `YammiC/app/src/main/java/co/cnewb/yammic/ui/settings/PairingFragment.kt`:

```kotlin
package co.cnewb.yammic.ui.settings

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.fragment.app.activityViewModels
import androidx.navigation.fragment.findNavController
import co.cnewb.yammic.databinding.FragmentPairingBinding
import co.cnewb.yammic.ui.BikeViewModel

class PairingFragment : Fragment() {

    private var _binding: FragmentPairingBinding? = null
    private val binding get() = _binding!!

    private val viewModel: BikeViewModel by activityViewModels()

    // True until the fields have been pre-filled once from the current state, so later state
    // emissions (e.g. a log update) don't clobber text the user is actively editing.
    private var prefilled = false

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentPairingBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(
        view: View,
        savedInstanceState: Bundle?,
    ) {
        super.onViewCreated(view, savedInstanceState)

        binding.toolbar.setNavigationOnClickListener { findNavController().navigateUp() }

        viewModel.connectionState.observe(viewLifecycleOwner) { state ->
            if (!prefilled) {
                binding.ccuIdInput.setText(state.ccuId)
                binding.passKeyInput.setText(state.passKey)
                binding.deviceNameInput.setText(state.deviceName)
                prefilled = true
            }
        }

        binding.saveButton.setOnClickListener {
            viewModel.updateCredentials(
                binding.ccuIdInput.text?.toString().orEmpty(),
                binding.passKeyInput.text?.toString().orEmpty(),
            )
            viewModel.updateDeviceName(binding.deviceNameInput.text?.toString().orEmpty())
            findNavController().navigateUp()
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
```

- [ ] **Step 3: Verify**

Run: `./gradlew.bat :app:compileDebugKotlin`
Expected: PASS.

---

### Task 11: `NavigationSettingsFragment`

**Files:**
- Create: `YammiC/app/src/main/res/layout/fragment_navigation_settings.xml`
- Create: `YammiC/app/src/main/java/co/cnewb/yammic/ui/navigation/NavigationSettingsFragment.kt`

**Interfaces:**
- Consumes: `BikeViewModel` (Task 6), `.navigationEnabled`, `.navigationState`, `.setNavigationEnabled(Boolean)`; `describeNavigationState` (Task 8).

- [ ] **Step 1: Layout**

Create `YammiC/app/src/main/res/layout/fragment_navigation_settings.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<androidx.core.widget.NestedScrollView xmlns:android="http://schemas.android.com/apk/res/android"
    xmlns:app="http://schemas.android.com/apk/res-auto"
    xmlns:tools="http://schemas.android.com/tools"
    android:layout_width="match_parent"
    android:layout_height="match_parent"
    android:fillViewport="true">

    <LinearLayout
        android:layout_width="match_parent"
        android:layout_height="wrap_content"
        android:orientation="vertical"
        android:padding="16dp">

        <com.google.android.material.appbar.MaterialToolbar
            android:id="@+id/toolbar"
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:title="@string/navigation_settings_title"
            app:navigationIcon="@drawable/ic_arrow_back" />

        <LinearLayout
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="16dp"
            android:orientation="horizontal"
            android:gravity="center_vertical">
            <TextView
                android:layout_width="0dp"
                android:layout_height="wrap_content"
                android:layout_weight="1"
                android:text="@string/navigation_settings_toggle" />
            <com.google.android.material.materialswitch.MaterialSwitch
                android:id="@+id/navigationSwitch"
                android:layout_width="wrap_content"
                android:layout_height="wrap_content" />
        </LinearLayout>

        <TextView
            android:id="@+id/navigationStatusText"
            android:layout_width="match_parent"
            android:layout_height="wrap_content"
            android:layout_marginTop="16dp"
            tools:text="Status: Active (5 fps)" />

    </LinearLayout>
</androidx.core.widget.NestedScrollView>
```

- [ ] **Step 2: `NavigationSettingsFragment`**

Create `YammiC/app/src/main/java/co/cnewb/yammic/ui/navigation/NavigationSettingsFragment.kt`:

```kotlin
package co.cnewb.yammic.ui.navigation

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.fragment.app.activityViewModels
import androidx.navigation.fragment.findNavController
import co.cnewb.yammic.R
import co.cnewb.yammic.databinding.FragmentNavigationSettingsBinding
import co.cnewb.yammic.ui.BikeViewModel

class NavigationSettingsFragment : Fragment() {

    private var _binding: FragmentNavigationSettingsBinding? = null
    private val binding get() = _binding!!

    private val viewModel: BikeViewModel by activityViewModels()

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentNavigationSettingsBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(
        view: View,
        savedInstanceState: Bundle?,
    ) {
        super.onViewCreated(view, savedInstanceState)

        binding.toolbar.setNavigationOnClickListener { findNavController().navigateUp() }

        binding.navigationSwitch.setOnCheckedChangeListener { switchView, isChecked ->
            if (switchView.isPressed) viewModel.setNavigationEnabled(isChecked)
        }
        viewModel.navigationEnabled.observe(viewLifecycleOwner) { enabled ->
            if (binding.navigationSwitch.isChecked != enabled) binding.navigationSwitch.isChecked = enabled
        }
        viewModel.navigationState.observe(viewLifecycleOwner) { state ->
            binding.navigationStatusText.text =
                getString(R.string.navigation_settings_status_format, describeNavigationState(requireContext(), state))
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
```

- [ ] **Step 3: Verify**

Run: `./gradlew.bat :app:compileDebugKotlin`
Expected: PASS.

---

### Task 12: Delete Compose leftovers and full build verification

**Files:**
- Delete: `YammiC/app/src/main/java/co/cnewb/yammic/ui/theme/Color.kt`
- Delete: `YammiC/app/src/main/java/co/cnewb/yammic/ui/theme/Theme.kt`
- Delete: `YammiC/app/src/main/java/co/cnewb/yammic/ui/theme/Type.kt`

**Interfaces:**
- Consumes: nothing new — this task only removes dead code and does a final full-project check.

- [ ] **Step 1: Delete the old Compose theme files**

```bash
rm "YammiC/app/src/main/java/co/cnewb/yammic/ui/theme/Color.kt"
rm "YammiC/app/src/main/java/co/cnewb/yammic/ui/theme/Theme.kt"
rm "YammiC/app/src/main/java/co/cnewb/yammic/ui/theme/Type.kt"
rmdir "YammiC/app/src/main/java/co/cnewb/yammic/ui/theme" 2>/dev/null || true
```

(These were already unused after Task 7's `MainActivity.kt` rewrite dropped `YammiCTheme { ... }` — this step just removes the now-dead files. If the `ui/theme` directory fails to remove because it's non-empty, leave it; that means something in an earlier task still references it and needs investigating before continuing.)

- [ ] **Step 2: Run the full unit test suite**

Run: `./gradlew.bat :app:testDebugUnitTest`
Expected: PASS, including the existing `NaviLite*`/`NavigationEngineTest`/`NavFrameReaderTest` suite and the new `ConnectButtonStateTest` from Task 3.

- [ ] **Step 3: Full debug assembly**

Run: `./gradlew.bat :app:assembleDebug`
Expected: PASS — this is the first task where resource linking for every new layout/menu/drawable/navigation-graph file is exercised together (earlier tasks only ran `compileDebugKotlin`, which doesn't always fully validate every resource reference across the whole module).

- [ ] **Step 4: Manual on-device verification**

This cannot be scripted — install the debug APK on a device and walk through:

1. App opens directly to the Dashboard (no bike-selection screen).
2. Dashboard shows the bike/BLE name, connection status, and the `Odometer: --` placeholder.
3. Connect button starts enabled/selected, Disconnect starts disabled.
4. Tapping Connect: both buttons disable while connecting; once connected, Connect stays disabled and Disconnect becomes enabled.
5. Tapping Disconnect returns to the Idle state.
6. Media metadata card reflects whatever is currently playing on the phone (or "Nothing playing").
7. VPB card's switch toggles VPB on/off and its status line updates as VPB messages are sent (e.g. after the 15s sandwich-image delay).
8. Debug log card shows recent log lines and auto-scrolls; "Clear logs" empties it.
9. Tapping the Navigation card opens Navigation settings; its toggle and status match the Dashboard's VPB-adjacent navigation summary; back-navigation returns to Dashboard.
10. Settings gear icon opens Settings; all five toggles are present and persist across app restarts (Music/Phone/Auto-reconnect are inert stubs — confirm they don't crash or affect behavior).
11. Pairing row opens the Pairing page pre-filled with the current CCU ID/PassKey/device name; Save updates them and returns to Settings.
12. Toggle system dark mode and confirm the app switches from the white/pink light theme to the black/teal dark theme.

---

## Spec coverage check

- XML/Fragment UI instead of Compose → Task 1, 7.
- Dashboard as start destination, no bike-selection screen → Task 7 (`nav_graph.xml` `startDestination`).
- Header (bike name, BLE name, connection status, odometer placeholder) → Task 8.
- Connect/Disconnect 3-state button → Tasks 3, 8.
- Notification-access banner → Task 8.
- Media metadata card → Tasks 4, 8.
- VPB card (toggle + status) → Tasks 4, 8.
- Debug log card → Task 8.
- Navigation card → Tasks 8 navigation card summary; full page in Task 11.
- Settings page (5 toggles + Pairing row) → Task 9.
- Pairing page → Task 10.
- Navigation settings page → Task 11.
- Light (white/pink) and dark (black/teal) theming → Task 2.
- No automated UI tests; one pure-logic unit test (connect state) → Task 3.
- No commits during execution → Global Constraints, honored by every task's final "Verify" step (no "Commit" steps anywhere in this plan).
