package com.linksync.app.service

import android.app.Activity
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.graphics.PixelFormat
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.media.MediaRecorder
import android.media.MediaScannerConnection
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.provider.Settings
import android.util.DisplayMetrics
import android.util.Log
import android.view.WindowManager
import android.widget.Toast
import androidx.core.app.NotificationCompat
import com.linksync.app.R
import com.linksync.app.overlay.DrawOverlayView
import com.linksync.app.overlay.FloatingControlView
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class ScreenRecorderService : Service() {

    private var mediaProjection: MediaProjection? = null
    private var virtualDisplay: VirtualDisplay? = null
    private var mediaRecorder: MediaRecorder? = null

    private var windowManager: WindowManager? = null
    private var drawOverlayView: DrawOverlayView? = null
    private var floatingControlView: FloatingControlView? = null

    private var currentVideoFile: File? = null

    companion object {
        const val CHANNEL_ID = "linksync_recording_channel"
        const val NOTIFICATION_ID = 2002
        const val ACTION_START = "com.linksync.app.START_RECORDING"
        const val ACTION_STOP = "com.linksync.app.STOP_RECORDING"
        const val ACTION_RECORDING_STATE = "com.linksync.app.RECORDING_STATE"
        const val EXTRA_RESULT_CODE = "extra_result_code"
        const val EXTRA_DATA = "extra_data"
        const val EXTRA_RECORDING = "extra_recording"

        @Volatile
        var isRecording: Boolean = false
            internal set

        fun publishRecordingState(context: Context, recording: Boolean) {
            isRecording = recording
            context.sendBroadcast(
                Intent(ACTION_RECORDING_STATE).apply {
                    setPackage(context.packageName)
                    putExtra(EXTRA_RECORDING, recording)
                }
            )
        }
    }

    override fun onCreate() {
        super.onCreate()
        windowManager = getSystemService(Context.WINDOW_SERVICE) as WindowManager
        createNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_START -> {
                val resultCode = intent.getIntExtra(EXTRA_RESULT_CODE, Activity.RESULT_CANCELED)
                val data = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                    intent.getParcelableExtra(EXTRA_DATA, Intent::class.java)
                } else {
                    @Suppress("DEPRECATION")
                    intent.getParcelableExtra(EXTRA_DATA)
                }

                if (resultCode == Activity.RESULT_OK && data != null) {
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                        startForeground(
                            NOTIFICATION_ID,
                            buildRecordingNotification(),
                            ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION
                        )
                    } else {
                        startForeground(NOTIFICATION_ID, buildRecordingNotification())
                    }
                    initAndStartRecording(resultCode, data)
                } else {
                    stopSelf()
                }
            }
            ACTION_STOP -> {
                stopRecordingAndSync()
            }
        }
        return START_NOT_STICKY
    }

    private fun initAndStartRecording(resultCode: Int, data: Intent) {
        if (isRecording) return

        try {
            val projectionManager = getSystemService(Context.MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
            mediaProjection = projectionManager.getMediaProjection(resultCode, data)

            if (mediaProjection == null) {
                Log.e("ScreenRecorder", "MediaProjection is null after getMediaProjection")
                Toast.makeText(this, "Screen capture permission was not granted", Toast.LENGTH_SHORT).show()
                stopForeground(STOP_FOREGROUND_REMOVE)
                stopSelf()
                return
            }

            // CRUCIAL FOR ANDROID 14+ / 15: Register callback before createVirtualDisplay
            mediaProjection?.registerCallback(object : MediaProjection.Callback() {
                override fun onStop() {
                    Log.i("ScreenRecorder", "MediaProjection stopped by system")
                    stopRecordingAndSync()
                }
            }, Handler(Looper.getMainLooper()))

            val metrics = DisplayMetrics()
            @Suppress("DEPRECATION")
            windowManager?.defaultDisplay?.getRealMetrics(metrics)

            // Adjust width/height to even dimensions for H.264 encoder (multiples of 16 for hardware encoders)
            val width = (metrics.widthPixels / 16) * 16
            val height = (metrics.heightPixels / 16) * 16
            val density = metrics.densityDpi

            try {
                setupOverlayWindows()
            } catch (e: Exception) {
                Log.w("ScreenRecorder", "Could not setup drawing overlay: ${e.message}")
            }

            val dir = File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_MOVIES), "LinkSync")
            if (!dir.exists()) dir.mkdirs()

            val timestamp = SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US).format(Date())
            currentVideoFile = File(dir, "LinkSync_REC_$timestamp.mp4")

            mediaRecorder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                MediaRecorder(this)
            } else {
                @Suppress("DEPRECATION")
                MediaRecorder()
            }.apply {
                setVideoSource(MediaRecorder.VideoSource.SURFACE)
                setOutputFormat(MediaRecorder.OutputFormat.MPEG_4)
                setVideoEncoder(MediaRecorder.VideoEncoder.H264)
                setVideoEncodingBitRate(6_000_000)
                setVideoFrameRate(30)
                setVideoSize(width, height)
                setOutputFile(currentVideoFile!!.absolutePath)
                prepare()
            }

            val surface = mediaRecorder!!.surface
            virtualDisplay = mediaProjection?.createVirtualDisplay(
                "LinkSyncRecord",
                width,
                height,
                density,
                DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
                surface,
                null,
                null
            )

            mediaRecorder?.start()
            publishRecordingState(this, true)
            Toast.makeText(this, "Recording started", Toast.LENGTH_SHORT).show()

        } catch (e: Exception) {
            Log.e("ScreenRecorder", "Failed to start recording", e)
            Toast.makeText(this, "Failed to start recording: ${e.message}", Toast.LENGTH_LONG).show()
            cleanup()
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf()
        }
    }

    private fun setupOverlayWindows() {
        if (!Settings.canDrawOverlays(this)) {
            Log.w("ScreenRecorder", "SYSTEM_ALERT_WINDOW not granted; skipping floating drawing overlay")
            return
        }
        val wm = windowManager ?: return

        val overlayType = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
        } else {
            @Suppress("DEPRECATION")
            WindowManager.LayoutParams.TYPE_PHONE
        }

        // 1. Full-screen transparent drawing canvas
        drawOverlayView = DrawOverlayView(this).apply {
            val drawParams = WindowManager.LayoutParams(
                WindowManager.LayoutParams.MATCH_PARENT,
                WindowManager.LayoutParams.MATCH_PARENT,
                overlayType,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
                        WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
                PixelFormat.TRANSLUCENT
            )
            wm.addView(this, drawParams)
        }

        // 2. Floating Pill Controller
        floatingControlView = FloatingControlView(this, wm, drawOverlayView!!) {
            stopRecordingAndSync()
        }.apply {
            attach()
        }
    }

    private fun stopRecordingAndSync() {
        if (!isRecording) return
        publishRecordingState(this, false)

        try {
            mediaRecorder?.stop()
            mediaRecorder?.reset()
        } catch (e: Exception) {
            Log.w("ScreenRecorder", "Error stopping MediaRecorder: ${e.message}")
        }

        cleanup()

        currentVideoFile?.let { file ->
            MediaScannerConnection.scanFile(
                applicationContext,
                arrayOf(file.absolutePath),
                arrayOf("video/mp4")
            ) { path, uri ->
                Log.i("ScreenRecorder", "Scanned recorded video: $path, uri: $uri")
                // Trigger foreground sync to push to laptop receiver
                ForegroundSyncService.sendMediaFileDirect(applicationContext, uri, file.name)
            }
            Toast.makeText(this, "Recording Saved: ${file.name}\nSyncing to PC...", Toast.LENGTH_LONG).show()
        }

        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun cleanup() {
        if (isRecording) {
            publishRecordingState(this, false)
        }
        try { virtualDisplay?.release() } catch (_: Exception) {}
        try { mediaProjection?.stop() } catch (_: Exception) {}
        try { mediaRecorder?.release() } catch (_: Exception) {}
        virtualDisplay = null
        mediaProjection = null
        mediaRecorder = null

        floatingControlView?.detach()
        drawOverlayView?.let {
            try { windowManager?.removeView(it) } catch (_: Exception) {}
        }
        floatingControlView = null
        drawOverlayView = null
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                "LinkSync Screen Recording",
                NotificationManager.IMPORTANCE_LOW
            )
            val manager = getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(channel)
        }
    }

    private fun buildRecordingNotification(): Notification {
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("LinkSync Recording Active")
            .setContentText("Screen and live annotations are being captured.")
            .setSmallIcon(R.drawable.ic_screen_record)
            .setOngoing(true)
            .build()
    }

    override fun onDestroy() {
        cleanup()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null
}
