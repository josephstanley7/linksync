package com.linksync.app.telephony

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.provider.Telephony
import android.util.Log
import com.linksync.app.service.ForegroundSyncService

class SmsBroadcastReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent?) {
        val action = intent?.action
        if (action == "com.linksync.app.action.TEST_SMS") {
            val sender = intent.getStringExtra("sender") ?: "Google"
            val body = intent.getStringExtra("body") ?: "Your verification code is 849201. Never share this code."
            val otpCode = OtpExtractor.extractOtp(body)
            Log.i("SmsReceiver", "Test SMS simulated from $sender: OTP=$otpCode")
            ForegroundSyncService.handleIncomingSms(
                address = sender,
                body = body,
                otpCode = otpCode ?: "",
                timestamp = System.currentTimeMillis()
            )
            return
        }

        if (action != Telephony.Sms.Intents.SMS_RECEIVED_ACTION) return

        try {
            val messages = Telephony.Sms.Intents.getMessagesFromIntent(intent)
            if (messages.isNullOrEmpty()) return

            val sender = messages[0].displayOriginatingAddress ?: "Unknown"
            val bodyBuilder = StringBuilder()
            val timestamp = messages[0].timestampMillis

            for (msg in messages) {
                bodyBuilder.append(msg.displayMessageBody ?: "")
            }

            val fullBody = bodyBuilder.toString()
            val otpCode = OtpExtractor.extractOtp(fullBody)

            Log.i("SmsReceiver", "Incoming SMS from $sender: bodyLen=${fullBody.length}, OTP=${otpCode ?: "none"}")

            ForegroundSyncService.handleIncomingSms(
                address = sender,
                body = fullBody,
                otpCode = otpCode ?: "",
                timestamp = timestamp
            )
        } catch (e: Exception) {
            Log.e("SmsReceiver", "Error processing incoming SMS PDU", e)
        }
    }
}
