package co.cnewb.yammic.bike

import android.Manifest
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.provider.ContactsContract
import android.telephony.TelephonyCallback
import android.telephony.TelephonyManager
import android.util.Log
import co.cnewb.yammic.ymcsdk.YmcMessageBroker
import co.cnewb.yammic.ymcsdk.packets.app.AppCallChangeNotification
import co.cnewb.yammic.ymcsdk.packets.app.AppIncomingCallInformationRequest
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch

private const val TAG = "PhoneCallMonitor"

class PhoneCallMonitor(
    private val context: Context,
    private val scope: CoroutineScope,
    private val getBroker: () -> YmcMessageBroker?,
) {
    private val telephonyManager = context.getSystemService(TelephonyManager::class.java)

    private var lastState = TelephonyManager.CALL_STATE_IDLE
    private var wasRinging = false
    private var callActive = false
    private var incomingPhoneNumber: String? = null
    private var telemetryCallbackRegistered = false
    private var broadcastReceiverRegistered = false

    private val callStateCallback =
        object : TelephonyCallback(), TelephonyCallback.CallStateListener {
            override fun onCallStateChanged(state: Int) = handleCallStateChanged(state)
        }

    private val phoneStateReceiver =
        object : BroadcastReceiver() {
            override fun onReceive(
                context: Context?,
                intent: Intent?,
            ) {
                if (intent?.action == TelephonyManager.ACTION_PHONE_STATE_CHANGED) {
                    val state = intent.getStringExtra(TelephonyManager.EXTRA_STATE)
                    val incomingNumber = intent.getStringExtra(TelephonyManager.EXTRA_INCOMING_NUMBER)
                    Log.d(TAG, "ACTION_PHONE_STATE_CHANGED: state=$state number=$incomingNumber")
                    if (!incomingNumber.isNullOrEmpty()) {
                        incomingPhoneNumber = incomingNumber
                    }
                    when (state) {
                        TelephonyManager.EXTRA_STATE_RINGING -> handleCallStateChanged(TelephonyManager.CALL_STATE_RINGING)
                    }
                }
            }
        }

    private var isListening = false

    fun start() {
        if (isListening) return
        if (context.checkSelfPermission(Manifest.permission.READ_PHONE_STATE) != PackageManager.PERMISSION_GRANTED) {
            return
        }
        telephonyManager?.registerTelephonyCallback(context.mainExecutor, callStateCallback)
        telemetryCallbackRegistered = true
        context.registerReceiver(phoneStateReceiver, IntentFilter(TelephonyManager.ACTION_PHONE_STATE_CHANGED))
        broadcastReceiverRegistered = true
        isListening = true
    }

    fun stop() {
        if (!isListening) return
        if (telemetryCallbackRegistered) {
            telephonyManager?.unregisterTelephonyCallback(callStateCallback)
            telemetryCallbackRegistered = false
        }
        if (broadcastReceiverRegistered) {
            try {
                context.unregisterReceiver(phoneStateReceiver)
            } catch (e: IllegalArgumentException) {
                // Already unregistered, ignore
            }
            broadcastReceiverRegistered = false
        }
        isListening = false
    }

    /** Sends the current call state immediately, e.g. right after connecting. */
    fun pushCurrentCallState() {
        scope.launch {
            val broker = getBroker() ?: return@launch
            AppCallChangeNotification(broker.packetHandler, started = callActive).send(broker.connectionContext)
        }
    }

    private fun handleCallStateChanged(state: Int) {
        when (state) {
            TelephonyManager.CALL_STATE_OFFHOOK -> {
                if (!callActive) {
                    callActive = true
                    wasRinging = false
                    scope.launch {
                        val broker = getBroker() ?: return@launch
                        broker.sendCallChange(true)
                    }
                }
            }

            TelephonyManager.CALL_STATE_RINGING -> {
                wasRinging = true
                var number = incomingPhoneNumber.orEmpty()
                Log.d(TAG, "RINGING detected: incomingPhoneNumber=$number")

                // Fallback: try to get number from TelephonyManager
                if (number.isEmpty() &&
                    context.checkSelfPermission(Manifest.permission.READ_PHONE_STATE) == PackageManager.PERMISSION_GRANTED
                ) {
                    val line1 = telephonyManager?.line1Number
                    if (!line1.isNullOrEmpty()) {
                        number = line1
                        Log.d(TAG, "Fallback to line1Number: $number")
                    }
                }

                val name = if (number.isNotEmpty()) lookupContactName(number) ?: number else "Unknown Caller"
                Log.d(TAG, "Sending incoming call: name=$name number=$number")

                scope.launch {
                    val broker = getBroker() ?: return@launch
                    broker.sendIncomingCallInformation(
                        callerName = name,
                        phoneNumber = number,
                        isMissedCall = false,
                    )
                }
            }

            TelephonyManager.CALL_STATE_IDLE -> {
                if (wasRinging && lastState == TelephonyManager.CALL_STATE_RINGING) {
                    val number = incomingPhoneNumber ?: "0"
                    val name = if (number.isNotEmpty()) lookupContactName(number) ?: number else "Unknown Caller"
                    scope.launch {
                        val broker = getBroker() ?: return@launch
                        broker.sendIncomingCallInformation(
                            callerName = name,
                            phoneNumber = number,
                            isMissedCall = false,
                        )
                    }
                }
                wasRinging = false
                incomingPhoneNumber = null
                if (callActive) {
                    callActive = false
                    scope.launch {
                        val broker = getBroker() ?: return@launch
                        broker.sendCallChange(false)
                    }
                }
            }

            else -> {
                Unit
            }
        }
        lastState = state
    }

    private fun lookupContactName(phoneNumber: String): String? {
        if (phoneNumber.isBlank()) return null
        if (context.checkSelfPermission(Manifest.permission.READ_CONTACTS) != PackageManager.PERMISSION_GRANTED) return null
        val uri =
            ContactsContract.PhoneLookup.CONTENT_FILTER_URI
                .buildUpon()
                .appendPath(phoneNumber)
                .build()
        return context.contentResolver
            .query(
                uri,
                arrayOf(ContactsContract.PhoneLookup.DISPLAY_NAME),
                null,
                null,
                null,
            )?.use { cursor ->
                if (cursor.moveToFirst()) cursor.getString(0) else null
            }
    }
}
