package com.linksync.app

import android.app.Activity
import android.content.Intent
import android.os.Bundle
import android.widget.Toast
import com.linksync.app.service.ForegroundSyncService

/**
 * Handles the native Android text selection popup action ("⚡ Send to PC").
 * When user highlights text in any app and selects "Send to PC", it forwards immediately.
 */
class SendTextActivity : Activity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        overridePendingTransition(0, 0)

        val text = intent?.getCharSequenceExtra(Intent.EXTRA_PROCESS_TEXT)?.toString()
            ?: intent?.getStringExtra(Intent.EXTRA_TEXT)

        if (!text.isNullOrEmpty()) {
            try {
                val clipboard = getSystemService(android.content.Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
                clipboard.setPrimaryClip(android.content.ClipData.newPlainText("LinkSync", text))
            } catch (_: Exception) {}

            ForegroundSyncService.sendClipboardDirect(text)
            Toast.makeText(this, "⚡ Sent to PC Clipboard", Toast.LENGTH_SHORT).show()
        }

        finish()
        overridePendingTransition(0, 0)
    }
}
