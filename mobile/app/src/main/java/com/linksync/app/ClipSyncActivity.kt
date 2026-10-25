package com.linksync.app

import android.app.Activity
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.os.Bundle
import android.util.Log
import android.widget.Toast
import com.linksync.app.service.ForegroundSyncService

/**
 * A sub-30ms transparent trampoline activity designed to obtain momentary window focus
 * so Android 10+ allows reading the primary clipboard, then immediately forwards it to the PC
 * and closes with zero animation.
 */
class ClipSyncActivity : Activity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        overridePendingTransition(0, 0)
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) {
            try {
                val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                val clip = clipboard.primaryClip
                if (clip != null && clip.itemCount > 0) {
                    val text = clip.getItemAt(0).text?.toString() ?: ""
                    if (text.isNotEmpty()) {
                        ForegroundSyncService.sendClipboardDirect(text)
                        Log.i("ClipSyncActivity", "Successfully grabbed clipboard with window focus: ${text.take(30)}...")
                    }
                }
            } catch (e: Exception) {
                Log.e("ClipSyncActivity", "Failed to access clipboard: ${e.message}")
            } finally {
                finish()
                overridePendingTransition(0, 0)
            }
        }
    }
}
