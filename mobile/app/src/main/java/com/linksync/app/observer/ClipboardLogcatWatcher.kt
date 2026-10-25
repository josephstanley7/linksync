package com.linksync.app.observer

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.SystemClock
import android.util.Log
import androidx.core.content.ContextCompat
import com.linksync.app.ClipSyncActivity
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.BufferedReader
import java.io.InputStreamReader

/**
 * Monitors logcat for Android 10+ ClipboardService security denial events.
 * When Android denies background clipboard access to LinkSync, this watcher
 * immediately invokes ClipSyncActivity to gain momentary focus and capture the clipboard.
 */
class ClipboardLogcatWatcher(private val context: Context, private val scope: CoroutineScope) {

    private var process: Process? = null
    private var lastTriggerTime = 0L

    fun start() {
        if (ContextCompat.checkSelfPermission(context, Manifest.permission.READ_LOGS) != PackageManager.PERMISSION_GRANTED) {
            Log.d("ClipboardLogcatWatcher", "READ_LOGS not granted. Automated logcat clipboard watcher standby.")
            return
        }

        scope.launch(Dispatchers.IO) {
            try {
                val cmd = arrayOf("logcat", "-v", "brief", "-T", "1", "-s", "ClipboardService:E")
                process = Runtime.getRuntime().exec(cmd)
                val reader = BufferedReader(InputStreamReader(process!!.inputStream))

                Log.i("ClipboardLogcatWatcher", "Clipboard logcat monitoring active.")

                while (isActive) {
                    val line = reader.readLine() ?: break
                    if (line.contains("Denying clipboard access to com.linksync.app")) {
                        val now = SystemClock.elapsedRealtime()
                        if (now - lastTriggerTime > 350) { // 350ms debounce
                            lastTriggerTime = now
                            Log.i("ClipboardLogcatWatcher", "Clipboard denial intercepted. Triggering focus trampoline...")

                            val intent = Intent(context, ClipSyncActivity::class.java).apply {
                                flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_NO_ANIMATION
                            }
                            context.startActivity(intent)
                        }
                    }
                }
            } catch (e: Exception) {
                Log.w("ClipboardLogcatWatcher", "Logcat monitoring stopped: ${e.message}")
            } finally {
                stop()
            }
        }
    }

    fun stop() {
        try {
            process?.destroy()
        } catch (_: Exception) {}
        process = null
    }
}
