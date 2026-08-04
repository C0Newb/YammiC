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
