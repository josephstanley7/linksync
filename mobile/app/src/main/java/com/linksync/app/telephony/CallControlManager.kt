package com.linksync.app.telephony

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.telecom.TelecomManager
import android.telephony.PhoneStateListener
import android.telephony.TelephonyCallback
import android.telephony.TelephonyManager
import android.util.Log
import java.util.concurrent.Executors

class CallControlManager(private val context: Context, private val onCallStateChanged: (state: String, number: String) -> Unit) {

    private val telephonyManager = context.getSystemService(Context.TELEPHONY_SERVICE) as? TelephonyManager
    private val telecomManager = context.getSystemService(Context.TELECOM_SERVICE) as? TelecomManager
    private val executor = Executors.newSingleThreadExecutor()

    private var activeCallback: Any? = null
    private var lastObservedNumber: String = ""

    fun startListening() {
        if (telephonyManager == null) return

        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                val callback = object : TelephonyCallback(), TelephonyCallback.CallStateListener {
                    override fun onCallStateChanged(state: Int) {
                        handleState(state, lastObservedNumber)
                    }
                }
                telephonyManager.registerTelephonyCallback(executor, callback)
                activeCallback = callback
            } else {
                @Suppress("DEPRECATION")
                val listener = object : PhoneStateListener() {
                    @Deprecated("Deprecated in Java")
                    override fun onCallStateChanged(state: Int, phoneNumber: String?) {
                        val num = if (!phoneNumber.isNullOrBlank()) phoneNumber else lastObservedNumber
                        handleState(state, num)
                    }
                }
                @Suppress("DEPRECATION")
                telephonyManager.listen(listener, PhoneStateListener.LISTEN_CALL_STATE)
                activeCallback = listener
            }
        } catch (e: SecurityException) {
            Log.w("CallControl", "Missing READ_PHONE_STATE permission: ${e.message}")
        } catch (e: Exception) {
            Log.e("CallControl", "Error registering call state callback", e)
        }
    }

    fun stopListening() {
        val tm = telephonyManager ?: return
        val cb = activeCallback ?: return
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                if (cb is TelephonyCallback) {
                    tm.unregisterTelephonyCallback(cb)
                }
            } else {
                if (cb is PhoneStateListener) {
                    @Suppress("DEPRECATION")
                    tm.listen(cb, PhoneStateListener.LISTEN_NONE)
                }
            }
        } catch (e: Exception) {
            Log.w("CallControl", "Error unregistering callback", e)
        }
        activeCallback = null
    }

    private fun handleState(state: Int, number: String) {
        val stateStr = when (state) {
            TelephonyManager.CALL_STATE_RINGING -> "RINGING"
            TelephonyManager.CALL_STATE_OFFHOOK -> "OFFHOOK"
            TelephonyManager.CALL_STATE_IDLE -> "IDLE"
            else -> "UNKNOWN"
        }
        Log.i("CallControl", "Call state changed: $stateStr (number: $number)")
        onCallStateChanged(stateStr, number)
    }

    fun dialCall(number: String): Boolean {
        val cleanNumber = number.replace("[^0-9+*#]".toRegex(), "")
        if (cleanNumber.isBlank()) return false

        lastObservedNumber = cleanNumber
        return try {
            val intent = Intent(Intent.ACTION_CALL).apply {
                data = Uri.parse("tel:$cleanNumber")
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            context.startActivity(intent)
            Log.i("CallControl", "Dialing initiated to $cleanNumber")
            true
        } catch (e: SecurityException) {
            Log.e("CallControl", "Missing CALL_PHONE permission", e)
            // Fall back to dialer screen if CALL_PHONE is not yet granted
            try {
                val dialIntent = Intent(Intent.ACTION_DIAL).apply {
                    data = Uri.parse("tel:$cleanNumber")
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                }
                context.startActivity(dialIntent)
                true
            } catch (ex: Exception) {
                false
            }
        } catch (e: Exception) {
            Log.e("CallControl", "Failed to place call", e)
            false
        }
    }

    fun endCall(): Boolean {
        return try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                telecomManager?.endCall() == true
            } else {
                false
            }
        } catch (e: SecurityException) {
            Log.w("CallControl", "Missing ANSWER_PHONE_CALLS permission to end call", e)
            false
        } catch (e: Exception) {
            Log.e("CallControl", "Failed to end call", e)
            false
        }
    }
}
