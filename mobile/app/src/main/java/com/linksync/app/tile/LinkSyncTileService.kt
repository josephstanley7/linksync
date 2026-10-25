package com.linksync.app.tile

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Build
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import androidx.annotation.RequiresApi
import androidx.core.content.ContextCompat
import com.linksync.app.ScreenCapturePromptActivity
import com.linksync.app.service.ScreenRecorderService

@RequiresApi(Build.VERSION_CODES.N)
class LinkSyncTileService : TileService() {

    private val stateReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            refreshTile()
        }
    }

    private var receiverRegistered = false

    override fun onClick() {
        super.onClick()

        if (!ScreenRecorderService.isRecording) {
            val promptIntent = Intent(this, ScreenCapturePromptActivity::class.java).apply {
                addFlags(
                    Intent.FLAG_ACTIVITY_NEW_TASK or
                        Intent.FLAG_ACTIVITY_NO_ANIMATION or
                        Intent.FLAG_ACTIVITY_EXCLUDE_FROM_RECENTS or
                        Intent.FLAG_ACTIVITY_NO_HISTORY
                )
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
                val pendingIntent = PendingIntent.getActivity(
                    this,
                    0,
                    promptIntent,
                    PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
                )
                startActivityAndCollapse(pendingIntent)
            } else {
                @Suppress("DEPRECATION")
                startActivityAndCollapse(promptIntent)
            }
            // Tile stays inactive until ScreenRecorderService actually starts capturing.
            refreshTile()
        } else {
            val stopIntent = Intent(this, ScreenRecorderService::class.java).apply {
                action = ScreenRecorderService.ACTION_STOP
            }
            startService(stopIntent)
        }
    }

    override fun onStartListening() {
        super.onStartListening()
        refreshTile()
        if (!receiverRegistered) {
            ContextCompat.registerReceiver(
                this,
                stateReceiver,
                IntentFilter(ScreenRecorderService.ACTION_RECORDING_STATE),
                ContextCompat.RECEIVER_NOT_EXPORTED
            )
            receiverRegistered = true
        }
    }

    override fun onStopListening() {
        super.onStopListening()
        if (receiverRegistered) {
            try {
                unregisterReceiver(stateReceiver)
            } catch (_: Exception) {
            }
            receiverRegistered = false
        }
    }

    private fun refreshTile() {
        val tile = qsTile ?: return
        val recording = ScreenRecorderService.isRecording
        tile.state = if (recording) Tile.STATE_ACTIVE else Tile.STATE_INACTIVE
        tile.label = if (recording) "Recording..." else "LinkSync Record"
        tile.updateTile()
    }
}
