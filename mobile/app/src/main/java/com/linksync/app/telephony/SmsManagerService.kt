package com.linksync.app.telephony

import android.content.Context
import android.os.Build
import android.provider.Telephony
import android.telephony.SmsManager
import android.util.Log
import org.json.JSONArray
import org.json.JSONObject

object SmsManagerService {

    private val SMS_PROJECTION = arrayOf(
        Telephony.Sms._ID,
        Telephony.Sms.THREAD_ID,
        Telephony.Sms.ADDRESS,
        Telephony.Sms.BODY,
        Telephony.Sms.DATE,
        Telephony.Sms.TYPE,
        Telephony.Sms.READ
    )

    fun fetchRecentSmsJson(context: Context, limit: Int = 100): String {
        val messagesArray = JSONArray()

        try {
            val cursor = context.contentResolver.query(
                Telephony.Sms.CONTENT_URI,
                SMS_PROJECTION,
                null,
                null,
                "${Telephony.Sms.DATE} DESC LIMIT $limit"
            )

            cursor?.use {
                val idIdx = it.getColumnIndex(Telephony.Sms._ID)
                val threadIdx = it.getColumnIndex(Telephony.Sms.THREAD_ID)
                val addressIdx = it.getColumnIndex(Telephony.Sms.ADDRESS)
                val bodyIdx = it.getColumnIndex(Telephony.Sms.BODY)
                val dateIdx = it.getColumnIndex(Telephony.Sms.DATE)
                val typeIdx = it.getColumnIndex(Telephony.Sms.TYPE)
                val readIdx = it.getColumnIndex(Telephony.Sms.READ)

                while (it.moveToNext()) {
                    val type = if (typeIdx >= 0) it.getInt(typeIdx) else Telephony.Sms.MESSAGE_TYPE_INBOX
                    val isIncoming = type == Telephony.Sms.MESSAGE_TYPE_INBOX

                    val item = JSONObject().apply {
                        put("id", if (idIdx >= 0) it.getString(idIdx) ?: "" else "")
                        put("threadId", if (threadIdx >= 0) it.getString(threadIdx) ?: "" else "")
                        put("address", if (addressIdx >= 0) it.getString(addressIdx) ?: "Unknown" else "Unknown")
                        put("body", if (bodyIdx >= 0) it.getString(bodyIdx) ?: "" else "")
                        put("timestamp", if (dateIdx >= 0) it.getLong(dateIdx) else System.currentTimeMillis())
                        put("isIncoming", isIncoming)
                        put("read", if (readIdx >= 0) it.getInt(readIdx) == 1 else true)
                    }
                    messagesArray.put(item)
                }
            }
        } catch (e: SecurityException) {
            Log.w("SmsService", "Missing READ_SMS permission: ${e.message}")
        } catch (e: Exception) {
            Log.e("SmsService", "Error reading SMS database", e)
        }

        return messagesArray.toString()
    }

    fun sendSms(context: Context, address: String, body: String): Boolean {
        if (address.isBlank() || body.isBlank()) return false

        return try {
            val smsManager: SmsManager = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                context.getSystemService(SmsManager::class.java)
            } else {
                @Suppress("DEPRECATION")
                SmsManager.getDefault()
            }

            val parts = smsManager.divideMessage(body)
            if (parts.size > 1) {
                smsManager.sendMultipartTextMessage(address, null, parts, null, null)
            } else {
                smsManager.sendTextMessage(address, null, body, null, null)
            }
            Log.i("SmsService", "Dispatched SMS to $address (chars: ${body.length})")
            true
        } catch (e: SecurityException) {
            Log.e("SmsService", "Missing SEND_SMS permission", e)
            false
        } catch (e: Exception) {
            Log.e("SmsService", "Failed to send SMS to $address", e)
            false
        }
    }
}
