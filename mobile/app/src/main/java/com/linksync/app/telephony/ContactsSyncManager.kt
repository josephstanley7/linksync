package com.linksync.app.telephony

import android.content.Context
import android.provider.ContactsContract
import android.util.Log
import org.json.JSONArray
import org.json.JSONObject

object ContactsSyncManager {

    private val PROJECTION = arrayOf(
        ContactsContract.CommonDataKinds.Phone.CONTACT_ID,
        ContactsContract.CommonDataKinds.Phone.DISPLAY_NAME_PRIMARY,
        ContactsContract.CommonDataKinds.Phone.NUMBER,
        ContactsContract.CommonDataKinds.Phone.STARRED
    )

    fun fetchContactsJson(context: Context): String {
        val contactsArray = JSONArray()
        val seenNumbers = HashSet<String>()

        try {
            val cursor = context.contentResolver.query(
                ContactsContract.CommonDataKinds.Phone.CONTENT_URI,
                PROJECTION,
                null,
                null,
                "${ContactsContract.CommonDataKinds.Phone.DISPLAY_NAME_PRIMARY} ASC"
            )

            cursor?.use {
                val idIdx = it.getColumnIndex(ContactsContract.CommonDataKinds.Phone.CONTACT_ID)
                val nameIdx = it.getColumnIndex(ContactsContract.CommonDataKinds.Phone.DISPLAY_NAME_PRIMARY)
                val numIdx = it.getColumnIndex(ContactsContract.CommonDataKinds.Phone.NUMBER)
                val starIdx = it.getColumnIndex(ContactsContract.CommonDataKinds.Phone.STARRED)

                while (it.moveToNext()) {
                    val rawNumber = if (numIdx >= 0) it.getString(numIdx) ?: "" else ""
                    val cleanNumber = rawNumber.replace("\\s+".toRegex(), "").replace("-", "")
                    if (cleanNumber.isBlank() || seenNumbers.contains(cleanNumber)) {
                        continue
                    }
                    seenNumbers.add(cleanNumber)

                    val contactObj = JSONObject().apply {
                        put("id", if (idIdx >= 0) it.getString(idIdx) ?: "" else "")
                        put("name", if (nameIdx >= 0) it.getString(nameIdx) ?: "Unknown" else "Unknown")
                        put("number", rawNumber.trim())
                        put("starred", if (starIdx >= 0) it.getInt(starIdx) == 1 else false)
                    }
                    contactsArray.put(contactObj)
                }
            }
        } catch (e: SecurityException) {
            Log.w("ContactsSync", "Missing READ_CONTACTS permission: ${e.message}")
        } catch (e: Exception) {
            Log.e("ContactsSync", "Error querying contacts", e)
        }

        return contactsArray.toString()
    }
}
