package com.linksync.app.service

import android.Manifest
import android.app.Activity
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.pm.ServiceInfo
import android.graphics.PixelFormat
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.media.MediaCodecInfo
import android.media.MediaRecorder
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Environment
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import android.provider.Settings
import android.util.DisplayMetrics
import android.util.Log
import android.view.View
import android.view.WindowManager
import android.widget.Toast
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import com.linksync.app.R
import com.linksync.app.VideoPreviewActivity
import com.linksync.app.overlay.CountdownOverlayView
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
    private var isPaused = false
    private var isAudioActive = true

    // Shake-to-Stop Sensor
    private var sensorManager: SensorManager? = null
    private var accelerometer: Sensor? = null
    private var isShakeRegistered = false

    private val shakeListener = object : SensorEventListener {
        private var lastX = 0f
        private var lastY = 0f
        private var lastZ = 0f
        private var lastUpdateTime = 0L
        private var lastShakeTriggerTime = 0L

        override fun onSensorChanged(event: SensorEvent?) {
            if (event == null || !isRecording) return
            val curTime = System.currentTimeMillis()
            val diffTime = curTime - lastUpdateTime
            if (diffTime > 100) {
                lastUpdateTime = curTime
                val x = event.values[0]
                val y = event.values[1]
                val z = event.values[2]

                val delta = Math.abs(x + y + z - lastX - lastY - lastZ) / diffTime * 10000

                if (delta > 850) { // Distinct intentional shake
                    if (curTime - lastShakeTriggerTime > 2500) {
                        lastShakeTriggerTime = curTime
                        Log.i("ScreenRecorder", "Shake detected! Stopping recording.")
                        val vibrator = getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator
                        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                            vibrator?.vibrate(VibrationEffect.createOneShot(120, VibrationEffect.DEFAULT_AMPLITUDE))
                        } else {
                            @Suppress("DEPRECATION")
                            vibrator?.vibrate(120)
                        }
                        Handler(Looper.getMainLooper()).post {
                            Toast.makeText(applicationContext, "Shake detected - Stopping recording", Toast.LENGTH_SHORT).show()
                            stopRecordingAndSync()
                        }
                    }
                }
                lastX = x
                lastY = y
                lastZ = z
            }
        }

        override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) {}
    }

    companion object {
        const val CHANNEL_ID = "linksync_recording_channel"
        const val NOTIFICATION_ID = 2002
        const val ACTION_START = "com.linksync.app.START_RECORDING"
        const val ACTION_STOP = "com.linksync.app.STOP_RECORDING"
        const val ACTION_PAUSE_RESUME = "com.linksync.app.PAUSE_RESUME"
        const val ACTION_TOGGLE_DRAW = "com.linksync.app.TOGGLE_DRAW"
        const val ACTION_RECORDING_STATE = "com.linksync.app.RECORDING_STATE"
        const val EXTRA_RESULT_CODE = "extra_result_code"
        const val EXTRA_DATA = "extra_data"
        const val EXTRA_RECORDING = "extra_recording"
        const val EXTRA_RECORD_AUDIO = "extra_record_audio"

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
                val recordAudio = intent.getBooleanExtra(EXTRA_RECORD_AUDIO, true)
                isAudioActive = recordAudio

                if (resultCode == Activity.RESULT_OK && data != null) {
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                        var fgsType = ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION
                        val hasMicPermission = ContextCompat.checkSelfPermission(
                            this, Manifest.permission.RECORD_AUDIO
                        ) == PackageManager.PERMISSION_GRANTED
                        if (recordAudio && Build.VERSION.SDK_INT >= Build.VERSION_CODES.R && hasMicPermission) {
                            fgsType = fgsType or ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE
                        }
                        startForeground(NOTIFICATION_ID, buildRecordingNotification(), fgsType)
                    } else {
                        startForeground(NOTIFICATION_ID, buildRecordingNotification())
                    }
                    initAndStartRecording(resultCode, data, recordAudio)
                } else {
                    stopSelf()
                }
            }
            ACTION_STOP -> {
                stopRecordingAndSync()
            }
            ACTION_PAUSE_RESUME -> {
                togglePauseResumeInternal(!isPaused)
            }
            ACTION_TOGGLE_DRAW -> {
                floatingControlView?.showBubble()
                floatingControlView?.toggleDrawingTools()
            }
        }
        return START_NOT_STICKY
    }

    private fun togglePauseResumeInternal(paused: Boolean) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N && mediaRecorder != null) {
            try {
                if (paused) {
                    mediaRecorder?.pause()
                    Toast.makeText(this, "Recording paused", Toast.LENGTH_SHORT).show()
                } else {
                    mediaRecorder?.resume()
                    Toast.makeText(this, "Recording resumed", Toast.LENGTH_SHORT).show()
                }
                isPaused = paused
                val notificationManager = getSystemService(NotificationManager::class.java)
                notificationManager?.notify(NOTIFICATION_ID, buildRecordingNotification())
                floatingControlView?.setPausedState(paused)
            } catch (e: Exception) {
                Log.w("ScreenRecorder", "Error toggling pause/resume: ${e.message}")
            }
        }
    }

    private fun initAndStartRecording(resultCode: Int, data: Intent, recordAudio: Boolean) {
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

            val width: Int
            val height: Int
            val density: Int

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                val wm = windowManager ?: (getSystemService(Context.WINDOW_SERVICE) as WindowManager)
                val bounds = wm.currentWindowMetrics.bounds
                width = (bounds.width() / 2) * 2
                height = (bounds.height() / 2) * 2
                density = resources.configuration.densityDpi
            } else {
                val metrics = DisplayMetrics()
                @Suppress("DEPRECATION")
                windowManager?.defaultDisplay?.getRealMetrics(metrics)
                width = (metrics.widthPixels / 2) * 2
                height = (metrics.heightPixels / 2) * 2
                density = metrics.densityDpi
            }

            try {
                setupOverlayWindows(recordAudio)
            } catch (e: Exception) {
                Log.w("ScreenRecorder", "Could not setup drawing overlay: ${e.message}")
            }

            val dir = File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_MOVIES), "LinkSync")
            if (!dir.exists()) dir.mkdirs()

            val timestamp = SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US).format(Date())
            currentVideoFile = File(dir, "LinkSync_REC_$timestamp.mp4")

            val hasMic = ContextCompat.checkSelfPermission(
                this, Manifest.permission.RECORD_AUDIO
            ) == PackageManager.PERMISSION_GRANTED

            mediaRecorder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                MediaRecorder(this)
            } else {
                @Suppress("DEPRECATION")
                MediaRecorder()
            }.apply {
                if (recordAudio && hasMic) {
                    try {
                        setAudioSource(MediaRecorder.AudioSource.MIC)
                    } catch (e: Exception) {
                        Log.w("ScreenRecorder", "Failed to set audio source MIC: ${e.message}")
                    }
                }
                setVideoSource(MediaRecorder.VideoSource.SURFACE)
                setOutputFormat(MediaRecorder.OutputFormat.MPEG_4)
                if (recordAudio && hasMic) {
                    try {
                        setAudioEncoder(MediaRecorder.AudioEncoder.AAC)
                        setAudioEncodingBitRate(128_000)
                        setAudioSamplingRate(44100)
                    } catch (e: Exception) {
                        Log.w("ScreenRecorder", "Failed to set audio encoder AAC: ${e.message}")
                    }
                }
                setVideoEncoder(MediaRecorder.VideoEncoder.H264)
                // 16 Mbps high-fidelity bitrate: crisp text, zero blur, professional clarity
                setVideoEncodingBitRate(16_000_000)
                setVideoFrameRate(30)
                setVideoSize(width, height)
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                    try {
                        setVideoEncodingProfileLevel(
                            MediaCodecInfo.CodecProfileLevel.AVCProfileHigh,
                            MediaCodecInfo.CodecProfileLevel.AVCLevel42
                        )
                    } catch (e: Exception) {
                        Log.w("ScreenRecorder", "AVCProfileHigh fallback: ${e.message}")
                    }
                }
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

            if (virtualDisplay == null) {
                throw IllegalStateException("Failed to create VirtualDisplay for screen recording.")
            }

            val startCapture = {
                try {
                    mediaRecorder?.start()
                    publishRecordingState(this, true)
                    floatingControlView?.startTimer()

                    val prefs = getSharedPreferences("linksync_prefs", Context.MODE_PRIVATE)
                    val hideBubble = prefs.getBoolean("pref_hide_floating_controller", true)
                    if (hideBubble) {
                        floatingControlView?.hideBubble()
                    }

                    if (prefs.getBoolean("pref_shake_to_stop", true)) {
                        startShakeDetector()
                    }

                    Toast.makeText(this, "Recording started", Toast.LENGTH_SHORT).show()
                } catch (err: Exception) {
                    Log.e("ScreenRecorder", "Failed to start media recorder", err)
                    cleanup()
                    stopForeground(STOP_FOREGROUND_REMOVE)
                    stopSelf()
                }
            }

            // Launch XRecorder-grade 3-2-1 Animated Countdown before recording starts
            val wm = windowManager
            if (Settings.canDrawOverlays(this) && wm != null) {
                CountdownOverlayView(this, wm) {
                    startCapture()
                }.start()
            } else {
                startCapture()
            }

        } catch (e: Exception) {
            Log.e("ScreenRecorder", "Failed to start recording", e)
            Toast.makeText(this, "Failed to start recording: ${e.message}", Toast.LENGTH_LONG).show()
            cleanup()
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf()
        }
    }

    private fun setupOverlayWindows(recordAudio: Boolean) {
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

        // 1. Full-screen transparent drawing canvas (FLAG_NOT_TOUCHABLE enabled by default so touches pass through)
        val drawParams = WindowManager.LayoutParams(
            WindowManager.LayoutParams.MATCH_PARENT,
            WindowManager.LayoutParams.MATCH_PARENT,
            overlayType,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
                WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE or
                WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
            PixelFormat.TRANSLUCENT
        )

        drawOverlayView = DrawOverlayView(this).apply {
            this.windowManager = wm
            this.overlayLayoutParams = drawParams
            wm.addView(this, drawParams)
        }

        // 2. Floating Pill Controller
        floatingControlView = FloatingControlView(
            this,
            wm,
            drawOverlayView!!,
            recordAudio = recordAudio,
            onPauseResumeClicked = { paused ->
                togglePauseResumeInternal(paused)
            },
            onStopRecordingClicked = {
                stopRecordingAndSync()
            }
        ).apply {
            attach()
        }
    }

    private fun startShakeDetector() {
        try {
            sensorManager = getSystemService(Context.SENSOR_SERVICE) as SensorManager
            accelerometer = sensorManager?.getDefaultSensor(Sensor.TYPE_ACCELEROMETER)
            accelerometer?.let {
                sensorManager?.registerListener(shakeListener, it, SensorManager.SENSOR_DELAY_UI)
                isShakeRegistered = true
            }
        } catch (e: Exception) {
            Log.w("ScreenRecorder", "Failed to start shake detector: ${e.message}")
        }
    }

    private fun stopShakeDetector() {
        if (isShakeRegistered) {
            try {
                sensorManager?.unregisterListener(shakeListener)
            } catch (_: Exception) {}
            isShakeRegistered = false
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
            if (file.exists() && file.length() > 1024) {
                // Launch XRecorder-grade post-recording preview & trimming gate
                val previewIntent = Intent(applicationContext, VideoPreviewActivity::class.java).apply {
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
                    putExtra(VideoPreviewActivity.EXTRA_VIDEO_PATH, file.absolutePath)
                }
                startActivity(previewIntent)
            } else {
                Toast.makeText(this, "Recording discarded (too short)", Toast.LENGTH_SHORT).show()
            }
        }

        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun cleanup() {
        if (isRecording) {
            publishRecordingState(this, false)
        }
        stopShakeDetector()
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
        val stopIntent = Intent(this, ScreenRecorderService::class.java).apply { action = ACTION_STOP }
        val stopPending = PendingIntent.getService(this, 101, stopIntent, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)

        val pauseIntent = Intent(this, ScreenRecorderService::class.java).apply { action = ACTION_PAUSE_RESUME }
        val pausePending = PendingIntent.getService(this, 102, pauseIntent, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)

        val drawIntent = Intent(this, ScreenRecorderService::class.java).apply { action = ACTION_TOGGLE_DRAW }
        val drawPending = PendingIntent.getService(this, 103, drawIntent, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)

        val title = if (isPaused) "⏸ LinkSync Recording Paused" else "⏺ LinkSync Recording Active"
        val subtitle = if (isAudioActive) "Screen & Audio (Mic) active" else "Silent screen recording active"

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle(title)
            .setContentText(subtitle)
            .setSmallIcon(R.drawable.ic_screen_record)
            .setOngoing(true)
            .addAction(R.drawable.ic_screen_record, "⏹ Stop", stopPending)
            .addAction(android.R.drawable.ic_media_pause, if (isPaused) "▶ Resume" else "⏸ Pause", pausePending)
            .addAction(android.R.drawable.ic_menu_edit, "🎨 Brush", drawPending)
            .build()
    }

    override fun onDestroy() {
        cleanup()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null
}
