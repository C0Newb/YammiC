package co.cnewb.yammic

import android.Manifest
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.os.Build
import android.os.Bundle
import android.os.IBinder
import android.provider.Settings
import android.util.Log
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.lifecycleScope
import co.cnewb.yammic.bike.BikeDebugState
import co.cnewb.yammic.bike.BikeForegroundService
import co.cnewb.yammic.ui.theme.YammiCTheme
import co.cnewb.yammic.ymcsdk.YmcConnectionBroker
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {
    // ── Service binding ───────────────────────────────────────────────────────

    private var bikeController: YmcConnectionBroker? = null
    private var bikeService: BikeForegroundService? = null
    private val _state = MutableStateFlow(BikeDebugState())
    private val _notificationAccessGranted = MutableStateFlow(false)
    private val _vpbEnabled = MutableStateFlow(true)
    private val _navigationEnabled = MutableStateFlow(true)
    private var stateJob: Job? = null
    private var vpbEnabledJob: Job? = null
    private var navigationEnabledJob: Job? = null

    private val serviceConnection =
        object : ServiceConnection {
            override fun onServiceConnected(
                name: ComponentName,
                binder: IBinder,
            ) {
                val service = (binder as BikeForegroundService.LocalBinder).service
                bikeService = service
                bikeController = service.controller
                stateJob?.cancel()
                stateJob =
                    lifecycleScope.launch {
                        service.controller.state.collect { _state.value = it }
                    }
                vpbEnabledJob?.cancel()
                vpbEnabledJob =
                    lifecycleScope.launch {
                        service.vpbEnabled.collect { _vpbEnabled.value = it }
                    }
                navigationEnabledJob?.cancel()
                navigationEnabledJob =
                    lifecycleScope.launch {
                        service.navigationEnabled.collect { _navigationEnabled.value = it }
                    }
            }

            override fun onServiceDisconnected(name: ComponentName) {
                bikeController = null
                bikeService = null
                stateJob?.cancel()
                vpbEnabledJob?.cancel()
                navigationEnabledJob?.cancel()
            }
        }

    private fun startAndBindService() {
        val intent = Intent(this, BikeForegroundService::class.java)
        startService(intent)
        bindService(intent, serviceConnection, Context.BIND_AUTO_CREATE)
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private var previousUncaughtHandler: Thread.UncaughtExceptionHandler? = null

    override fun onResume() {
        super.onResume()
        _notificationAccessGranted.value = isNotificationListenerGranted()
    }

    private fun isNotificationListenerGranted(): Boolean {
        val flat =
            Settings.Secure.getString(contentResolver, "enabled_notification_listeners")
                ?: return false
        return flat.split(":").any { it.startsWith("$packageName/") }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        previousUncaughtHandler = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, throwable ->
            try {
                bikeController?.logThrowable("uncaught:${thread.name}", throwable)
            } catch (_: Throwable) {
                Log.e("YammiC", "Uncaught exception", throwable)
            }
            previousUncaughtHandler?.uncaughtException(thread, throwable)
        }

        startAndBindService()

        setContent {
            YammiCTheme {
                val state by _state.collectAsState()
                val notificationAccessGranted by _notificationAccessGranted.collectAsState()
                val vpbEnabled by _vpbEnabled.collectAsState()
                val navigationEnabled by _navigationEnabled.collectAsState()
                val permissionLauncher =
                    rememberLauncherForActivityResult(
                        contract = ActivityResultContracts.RequestMultiplePermissions(),
                        onResult = { results ->
                            val granted = results.values.all { it }
                            bikeController?.setPermissionsGranted(granted)
                            bikeService?.restartPhoneCallMonitor()
                            if (granted) {
                                bikeController?.connectAndSend()
                            }
                        },
                    )

                fun requestPermissionsAndConnect() {
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

                Scaffold(modifier = Modifier.fillMaxSize()) { innerPadding ->
                    DebugScreen(
                        state = state,
                        modifier = Modifier.padding(innerPadding),
                        notificationAccessGranted = notificationAccessGranted,
                        vpbEnabled = vpbEnabled,
                        navigationEnabled = navigationEnabled,
                        onConnectAndSend = { requestPermissionsAndConnect() },
                        onDisconnect = { bikeController?.disconnect() },
                        onClearLogs = { bikeController?.clearLogs() },
                        onGrantNotificationAccess = {
                            startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS))
                        },
                        onVpbToggled = { enabled -> bikeService?.setVpbEnabled(enabled) },
                        onNavigationToggled = { enabled -> bikeService?.setNavigationEnabled(enabled) },
                        onCredentialsChanged = { ccuId, passKey ->
                            bikeController?.updateCredentials(ccuId, passKey)
                        },
                        onDeviceNameChanged = { bikeController?.updateDeviceName(it) },
                    )
                }
            }
        }
    }

    override fun onDestroy() {
        Thread.setDefaultUncaughtExceptionHandler(previousUncaughtHandler)
        try {
            unbindService(serviceConnection)
        } catch (_: Exception) {
        }
        super.onDestroy()
    }
}

// ── Composables ───────────────────────────────────────────────────────────────

@Composable
private fun DebugScreen(
    state: BikeDebugState,
    modifier: Modifier = Modifier,
    notificationAccessGranted: Boolean = true,
    vpbEnabled: Boolean = true,
    navigationEnabled: Boolean = true,
    onConnectAndSend: () -> Unit,
    onDisconnect: () -> Unit,
    onClearLogs: () -> Unit,
    onGrantNotificationAccess: () -> Unit = {},
    onVpbToggled: (Boolean) -> Unit = {},
    onNavigationToggled: (Boolean) -> Unit = {},
    onCredentialsChanged: (String, String) -> Unit,
    onDeviceNameChanged: (String) -> Unit,
) {
    val accent = Color(0xFFE6B45A)
    val panel = Color(0xFF15171B)
    val surface = Color(0xFF0F1114)
    val text = Color(0xFFECE7DB)
    val muted = Color(0xFF95A0AA)
    val background =
        Brush.linearGradient(
            colors = listOf(Color(0xFF090A0C), Color(0xFF11131A), Color(0xFF090A0C)),
        )
    val fieldColors =
        TextFieldDefaults.colors(
            focusedTextColor = text,
            unfocusedTextColor = text,
            disabledTextColor = text.copy(alpha = 0.5f),
            focusedLabelColor = accent,
            unfocusedLabelColor = muted,
            disabledLabelColor = muted.copy(alpha = 0.5f),
            cursorColor = accent,
            focusedIndicatorColor = accent,
            unfocusedIndicatorColor = muted.copy(alpha = 0.6f),
            disabledIndicatorColor = muted.copy(alpha = 0.25f),
            focusedContainerColor = Color.Transparent,
            unfocusedContainerColor = Color.Transparent,
            disabledContainerColor = Color.Transparent,
        )

    Surface(modifier = modifier.fillMaxSize()) {
        Box(
            modifier =
                Modifier
                    .fillMaxSize()
                    .background(background)
                    .padding(16.dp),
        ) {
            Column(
                verticalArrangement = Arrangement.spacedBy(12.dp),
                modifier = Modifier.verticalScroll(rememberScrollState()),
            ) {
                Text("YammiC debugger", color = accent, fontSize = 28.sp, fontWeight = FontWeight.Bold)
                Text(
                    "BLE client for Yamaha CCU",
                    color = muted,
                    fontSize = 13.sp,
                )

                if (!notificationAccessGranted) {
                    Card(
                        colors = CardDefaults.cardColors(containerColor = Color(0xFF2A1A00)),
                        shape = RoundedCornerShape(14.dp),
                        modifier = Modifier.fillMaxWidth(),
                    ) {
                        Row(
                            modifier = Modifier.padding(horizontal = 16.dp, vertical = 10.dp),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Text(
                                "Notification access required for music",
                                color = Color(0xFFFFCC80),
                                fontSize = 13.sp,
                                modifier = Modifier.weight(1f),
                            )
                            TextButton(onClick = onGrantNotificationAccess) {
                                Text("Grant", color = Color(0xFFFFCC80), fontWeight = FontWeight.Bold)
                            }
                        }
                    }
                }

                StatusCard(state = state, panel = panel, accent = accent, text = text, muted = muted)

                Card(
                    colors = CardDefaults.cardColors(containerColor = surface),
                    shape = RoundedCornerShape(18.dp),
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    Column(
                        modifier = Modifier.padding(16.dp),
                        verticalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        Text("Credentials and target", color = text, fontWeight = FontWeight.SemiBold)
                        OutlinedTextField(
                            value = state.ccuId,
                            onValueChange = { onCredentialsChanged(it, state.passKey) },
                            label = { Text("CCU ID") },
                            singleLine = true,
                            colors = fieldColors,
                            modifier = Modifier.fillMaxWidth(),
                        )
                        OutlinedTextField(
                            value = state.passKey,
                            onValueChange = { onCredentialsChanged(state.ccuId, it) },
                            label = { Text("PassKey") },
                            singleLine = true,
                            colors = fieldColors,
                            modifier = Modifier.fillMaxWidth(),
                        )
                        OutlinedTextField(
                            value = state.deviceName,
                            onValueChange = onDeviceNameChanged,
                            label = { Text("Device name") },
                            singleLine = true,
                            colors = fieldColors,
                            modifier = Modifier.fillMaxWidth(),
                        )
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Text("Virtual Pit Board", color = text, fontSize = 14.sp)
                            Switch(
                                checked = vpbEnabled,
                                onCheckedChange = onVpbToggled,
                                colors = SwitchDefaults.colors(checkedTrackColor = accent),
                            )
                        }
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Text("Navigation screen", color = text, fontSize = 14.sp)
                            Switch(
                                checked = navigationEnabled,
                                onCheckedChange = onNavigationToggled,
                                colors = SwitchDefaults.colors(checkedTrackColor = accent),
                            )
                        }

                        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            Button(
                                onClick = onConnectAndSend,
                                colors = ButtonDefaults.buttonColors(containerColor = accent),
                                enabled = !state.isBusy,
                            ) {
                                Text("Connect and send", color = Color.Black)
                            }
                            TextButton(onClick = onDisconnect) { Text("Disconnect", color = text) }
                            TextButton(onClick = onClearLogs) { Text("Clear logs", color = text) }
                        }
                    }
                }

                Card(
                    colors = CardDefaults.cardColors(containerColor = surface),
                    shape = RoundedCornerShape(18.dp),
                    modifier =
                        Modifier
                            .fillMaxWidth()
                            .height(320.dp),
                ) {
                    Column(
                        modifier =
                            Modifier
                                .fillMaxSize()
                                .padding(16.dp),
                    ) {
                        Text("Live log", color = text, fontWeight = FontWeight.SemiBold)
                        Spacer(modifier = Modifier.height(10.dp))
                        val logListState = rememberLazyListState()
                        LaunchedEffect(state.logs.size) {
                            if (state.logs.isNotEmpty()) {
                                logListState.animateScrollToItem(state.logs.lastIndex)
                            }
                        }
                        Box(
                            modifier =
                                Modifier
                                    .fillMaxSize()
                                    .border(1.dp, Color(0xFF2B3138), RoundedCornerShape(14.dp))
                                    .padding(12.dp),
                        ) {
                            LazyColumn(
                                state = logListState,
                                verticalArrangement = Arrangement.spacedBy(6.dp),
                            ) {
                                items(state.logs) { line ->
                                    Text(
                                        text = line,
                                        color = text,
                                        fontFamily = FontFamily.Monospace,
                                        fontSize = 12.sp,
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun StatusCard(
    state: BikeDebugState,
    panel: Color,
    accent: Color,
    text: Color,
    muted: Color,
) {
    Card(
        colors = CardDefaults.cardColors(containerColor = panel),
        shape = RoundedCornerShape(18.dp),
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column {
                    Text(state.connectionLabel, color = text, fontWeight = FontWeight.SemiBold)
                    Text(state.lastEvent.ifBlank { "Waiting for action" }, color = muted, fontSize = 13.sp)
                }
                Box(
                    modifier =
                        Modifier
                            .size(14.dp)
                            .background(
                                color = if (state.isBusy) accent else Color(0xFF4CAF50),
                                shape = RoundedCornerShape(99.dp),
                            ),
                )
            }
            Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                InfoChip(label = "Log", value = state.logFileLabel, text = text, muted = muted)
                InfoChip(label = "Bike", value = state.deviceName, text = text, muted = muted)
                // InfoChip(label = "UUID", value = state.phoneUuid.takeLast(8), text = text, muted = muted)
            }
            if (state.lastError.isNotBlank()) {
                Text(
                    text = state.lastError,
                    color = Color(0xFFFF9E80),
                    fontFamily = FontFamily.Monospace,
                    fontSize = 12.sp,
                )
            }
        }
    }
}

@Composable
private fun InfoChip(
    label: String,
    value: String,
    text: Color,
    muted: Color,
) {
    Column(
        modifier =
            Modifier
                .background(Color(0xFF1B1F24), RoundedCornerShape(12.dp))
                .padding(horizontal = 12.dp, vertical = 10.dp),
    ) {
        Text(label, color = muted, fontSize = 11.sp)
        Text(value, color = text, fontSize = 13.sp, fontWeight = FontWeight.Medium)
    }
}
