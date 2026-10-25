package com.linksync.app.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.media.MediaScannerConnection
import android.net.Uri
import android.os.BatteryManager
import android.os.Build
import android.os.Environment
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.SystemClock
import android.provider.MediaStore
import android.util.Log
import androidx.core.app.NotificationCompat
import com.linksync.app.ClipSyncActivity
import com.linksync.app.R
import com.linksync.app.net.LinkSyncProtocol
import com.linksync.app.net.NetworkDiscovery
import com.linksync.app.observer.ClipboardLogcatWatcher
import com.linksync.app.observer.ScreenshotFileWatcher
import com.linksync.app.observer.ScreenshotObserver
import kotlinx.coroutines.*
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.File
import java.lang.ref.WeakReference
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.charset.StandardCharsets

data class BridgeConnectionState(
    val isConnected: Boolean = false,
    val host: String = "",
    val desktopName: String = "",
    val lastSyncMessage: String = ""
)

data class ClipboardHistoryItem(
    val text: String,
    val timestamp: Long = System.currentTimeMillis(),
    val fromPhone: Boolean = true
)

class ForegroundSyncService : Service() {

    private val scope = CoroutineScope(Dispatchers.IO + SupervisorJob())
    private var socket: Socket? = null
    private var screenshotObserver: ScreenshotObserver? = null
    private var screenshotFileWatcher: ScreenshotFileWatcher? = null
    private var clipboardManager: ClipboardManager? = null
    private var lastReceivedFromPcText: String? = null
    private var lastReceivedFromPcTime: Long = 0L
    private var lastSentToPcText: String? = null
    private var lastSentToPcTime: Long = 0L
    private var clipboardLogcatWatcher: ClipboardLogcatWatcher? = null

    companion object {
        const val ACTION_STOP = "com.linksync.app.action.STOP"
        const val ACTION_RECONNECT = "com.linksync.app.action.RECONNECT"
        const val CHANNEL_ID = "linksync_service_channel"
        const val NOTIFICATION_ID = 1001
        var targetHost = "127.0.0.1" // USB tunnel default
        var targetPort = 54884
        var isConnected = false
        var connectedDesktopName = ""

        val connectionState = kotlinx.coroutines.flow.MutableStateFlow(BridgeConnectionState())
        val clipboardHistory = kotlinx.coroutines.flow.MutableStateFlow<List<ClipboardHistoryItem>>(emptyList())
        val sentFileUris = java.util.concurrent.ConcurrentHashMap<String, Uri>()
        val fileSyncEvent = kotlinx.coroutines.flow.MutableSharedFlow<String>(extraBufferCapacity = 10)

        var activeInstance: WeakReference<ForegroundSyncService>? = null

        fun addToHistory(text: String, fromPhone: Boolean) {
            val list = clipboardHistory.value.toMutableList()
            if (list.isNotEmpty() && list.first().text == text) return
            list.add(0, ClipboardHistoryItem(text = text, timestamp = System.currentTimeMillis(), fromPhone = fromPhone))
            if (list.size > 50) list.removeAt(list.size - 1)
            clipboardHistory.value = list
        }

        fun clearHistory() {
            clipboardHistory.value = emptyList()
        }

        fun sendMediaFileDirect(context: Context, uri: Uri?, fileName: String) {
            if (uri == null) return
            activeInstance?.get()?.let { service ->
                service.scope.launch {
                    service.sendMediaFile(uri, fileName)
                }
            }
        }

        fun sendClipboardDirect(text: String) {
            activeInstance?.get()?.sendClipboardInternal(text)
        }
    }

    fun sendClipboardInternal(text: String) {
        if (text.isEmpty()) return
        val now = SystemClock.elapsedRealtime()

        // 1. Echo suppression: Do not bounce back what PC sent us within 3.5s
        if (text == lastReceivedFromPcText && (now - lastReceivedFromPcTime) < 3500L) {
            Log.d("LinkSyncService", "Suppressing echo to PC (content matches text received from PC)")
            return
        }

        // 2. Debounce immediate duplicate triggers within 800ms
        if (text == lastSentToPcText && (now - lastSentToPcTime) < 800L) {
            return
        }

        lastSentToPcText = text
        lastSentToPcTime = now

        addToHistory(text, fromPhone = true)

        scope.launch {
            socket?.let { s ->
                try {
                    LinkSyncProtocol.writeFrame(
                        s.getOutputStream(),
                        LinkSyncProtocol.OP_CLIPBOARD_TEXT,
                        text.toByteArray(StandardCharsets.UTF_8)
                    )
                    Log.i("LinkSyncService", "Directly synced clipboard text to PC (${text.length} chars): ${text.take(30)}...")
                } catch (e: Exception) {
                    Log.e("LinkSyncService", "Failed to directly sync clipboard to PC", e)
                }
            } ?: Log.w("LinkSyncService", "Cannot sync clipboard: socket is null/disconnected")
        }
    }

    private val reconnectSignal = kotlinx.coroutines.channels.Channel<Unit>(kotlinx.coroutines.channels.Channel.CONFLATED)

    fun triggerImmediateReconnect() {
        try {
            socket?.close()
        } catch (_: Exception) {}
        socket = null
        reconnectSignal.trySend(Unit)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf()
            return START_NOT_STICKY
        }
        if (intent?.action == ACTION_RECONNECT) {
            triggerImmediateReconnect()
        }
        return START_STICKY
    }

    override fun onCreate() {
        super.onCreate()
        activeInstance = WeakReference(this)
        createNotificationChannel()
        startForeground(NOTIFICATION_ID, buildNotification("LinkSync Service Active"))

        clipboardManager = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        setupClipboardSync()
        setupScreenshotObserver()
        startScreenshotPollingLoop()
        clipboardLogcatWatcher = ClipboardLogcatWatcher(this, scope)
        clipboardLogcatWatcher?.start()
        NetworkDiscovery.startPassiveListener(this, scope)
        startConnectionLoop()
    }

    private fun startScreenshotPollingLoop() {
        scope.launch {
            while (isActive) {
                delay(1500)
                try {
                    screenshotObserver?.checkForNewScreenshot()
                } catch (_: Exception) { }
            }
        }
    }

    private fun isUsbConnected(): Boolean {
        return try {
            val intent = registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
            val plugged = intent?.getIntExtra(BatteryManager.EXTRA_PLUGGED, -1) ?: -1
            plugged == BatteryManager.BATTERY_PLUGGED_USB
        } catch (e: Exception) {
            false
        }
    }

    private fun startConnectionLoop() {
        scope.launch {
            val prefs = getSharedPreferences("linksync_prefs", Context.MODE_PRIVATE)

            while (isActive) {
                try {
                    val candidateHosts = mutableListOf<String>()

                    // 1. Manual IP override (if entered by user in advanced settings)
                    val manualHost = prefs.getString("manual_target_host", "")?.trim() ?: ""
                    if (manualHost.isNotEmpty()) {
                        candidateHosts.add(manualHost)
                    }

                    // 2. Always prioritize USB reverse loopback FIRST (0ms latency)
                    candidateHosts.add("127.0.0.1")

                    // 3. Dynamic UDP discovery across Wi-Fi & Hotspot (zero-configuration)
                    val cachedIp = prefs.getString("last_discovered_ip", "") ?: ""
                    NetworkDiscovery.sendDiscoveryPing(cachedIp)

                    // Brief pause for incoming pong/discovery datagrams
                    delay(250)

                    val discoveredList = NetworkDiscovery.discoveredDesktops.value
                    val pairedDeviceId = prefs.getString("paired_device_id", "") ?: ""

                    if (discoveredList.isNotEmpty()) {
                        if (pairedDeviceId.isNotEmpty()) {
                            // Check if the paired computer is online
                            val pairedDevice = discoveredList.find { it.deviceId == pairedDeviceId }
                            if (pairedDevice != null) {
                                if (!candidateHosts.contains(pairedDevice.ipAddress)) {
                                    candidateHosts.add(pairedDevice.ipAddress)
                                }
                                connectedDesktopName = pairedDevice.deviceName
                                targetPort = pairedDevice.port
                                prefs.edit().putString("last_discovered_ip", pairedDevice.ipAddress).apply()
                            } else {
                                val first = discoveredList.first()
                                if (!candidateHosts.contains(first.ipAddress)) {
                                    candidateHosts.add(first.ipAddress)
                                }
                            }
                        } else {
                            if (discoveredList.size == 1) {
                                val single = discoveredList.first()
                                prefs.edit()
                                    .putString("paired_device_id", single.deviceId)
                                    .putString("paired_device_name", single.deviceName)
                                    .putString("last_discovered_ip", single.ipAddress)
                                    .apply()
                                if (!candidateHosts.contains(single.ipAddress)) {
                                    candidateHosts.add(single.ipAddress)
                                }
                                connectedDesktopName = single.deviceName
                                targetPort = single.port
                                Log.i("LinkSyncService", "Auto-paired with single discovered PC: ${single.deviceName} [${single.deviceId}]")
                            } else {
                                Log.i("LinkSyncService", "Multiple PCs online (${discoveredList.size}). Waiting for user selection.")
                                connectionState.value = BridgeConnectionState(
                                    isConnected = false,
                                    lastSyncMessage = "Found ${discoveredList.size} computers. Tap 'Select PC' to pair."
                                )
                            }
                        }
                    }

                    // Add remembered Wi-Fi hosts
                    if (cachedIp.isNotEmpty() && !candidateHosts.contains(cachedIp)) {
                        candidateHosts.add(cachedIp)
                    }
                    val wifiHost = prefs.getString("wifi_host", "") ?: ""
                    if (wifiHost.isNotEmpty() && !candidateHosts.contains(wifiHost)) {
                        candidateHosts.add(wifiHost)
                    }



                    var connected = false
                    for (host in candidateHosts) {
                        if (!isActive) break
                        try {
                            Log.i("LinkSyncService", "Attempting connection to desktop at $host:$targetPort...")
                            val s = Socket()
                            s.connect(InetSocketAddress(host, targetPort), 2500)
                            socket = s
                            isConnected = true
                            targetHost = host
                            val isUsb = (host == "127.0.0.1")
                            connectionState.value = BridgeConnectionState(
                                isConnected = true,
                                host = host,
                                desktopName = connectedDesktopName.ifEmpty { "HP EliteBook Folio 9470M" },
                                lastSyncMessage = if (isUsb) "Connected via USB Cable" else "Connected via Wi-Fi"
                            )
                            updateNotification("Connected to Laptop (${if (isUsb) "USB" else "Wi-Fi"})")
                            Log.i("LinkSyncService", "Connected to desktop at $host:$targetPort via ${if (isUsb) "USB" else "Wi-Fi"}!")

                            // Handshake frame
                            val deviceJson = JSONObject().apply {
                                put("deviceName", Build.MODEL)
                                put("deviceManufacturer", Build.MANUFACTURER)
                                put("androidVersion", Build.VERSION.RELEASE)
                            }
                            LinkSyncProtocol.writeFrame(s.getOutputStream(), LinkSyncProtocol.OP_HANDSHAKE, deviceJson.toString().toByteArray())

                            val input = s.getInputStream()
                            while (isActive) {
                                val frame = LinkSyncProtocol.readFrame(input) ?: break
                                handleIncomingFrame(frame)
                            }

                            Log.i("LinkSyncService", "Socket disconnected from desktop $host")
                            connected = true
                            break
                        } catch (e: Exception) {
                            Log.i("LinkSyncService", "Could not connect to $host: ${e.message}")
                        } finally {
                            try { socket?.close() } catch (_: Exception) {}
                            socket = null
                            isConnected = false
                        }
                    }

                    if (!connected) {
                        isConnected = false
                        val currentUsb = isUsbConnected()
                        val statusMsg = if (currentUsb) {
                            "🔌 USB Cable detected. Waiting for LinkSync desktop..."
                        } else {
                            "Searching on Wi-Fi & USB..."
                        }
                        connectionState.value = BridgeConnectionState(
                            isConnected = false,
                            lastSyncMessage = statusMsg
                        )
                        updateNotification(if (currentUsb) "USB Cable detected: Waiting for LinkSync" else "Searching for Laptop...")
                    }

                    // Reactive pause: sleep 2.5s OR wake up immediately if reconnect signal received
                    kotlinx.coroutines.withTimeoutOrNull(2500) {
                        reconnectSignal.receive()
                    }
                } catch (e: Exception) {
                    Log.e("LinkSyncService", "Error in connection loop", e)
                    delay(2000)
                }
            }
        }
    }

    private fun handleIncomingFrame(frame: LinkSyncProtocol.Frame) {
        when (frame.opCode) {
            LinkSyncProtocol.OP_HANDSHAKE -> {
                try {
                    val jsonStr = String(frame.payload, StandardCharsets.UTF_8)
                    val json = JSONObject(jsonStr)
                    connectedDesktopName = json.optString("deviceName", "Desktop PC")
                    val devId = json.optString("deviceId", "")
                    if (devId.isNotEmpty()) {
                        getSharedPreferences("linksync_prefs", Context.MODE_PRIVATE)
                            .edit()
                            .putString("paired_device_id", devId)
                            .putString("paired_device_name", connectedDesktopName)
                            .apply()
                    }
                    Log.i("LinkSyncService", "Handshake confirmed by desktop: $connectedDesktopName [ID: $devId]")
                    connectionState.value = BridgeConnectionState(
                        isConnected = true,
                        host = targetHost,
                        desktopName = connectedDesktopName,
                        lastSyncMessage = "Paired with $connectedDesktopName"
                    )
                    updateNotification("Connected to $connectedDesktopName")
                } catch (e: Exception) {
                    Log.w("LinkSyncService", "Failed to parse handshake response", e)
                }
            }

            LinkSyncProtocol.OP_CLIPBOARD_TEXT -> {
                val text = String(frame.payload, StandardCharsets.UTF_8)
                if (text.isNotEmpty()) {
                    lastReceivedFromPcText = text
                    lastReceivedFromPcTime = SystemClock.elapsedRealtime()
                    addToHistory(text, fromPhone = false)
                    Handler(Looper.getMainLooper()).post {
                        try {
                            val clip = ClipData.newPlainText("LinkSync", text)
                            clipboardManager?.setPrimaryClip(clip)
                            Log.i("LinkSyncService", "Set phone clipboard from PC: ${text.take(30)}...")
                        } catch (e: Exception) {
                            Log.e("LinkSyncService", "Failed to set clipboard", e)
                        }
                    }
                }
            }

            LinkSyncProtocol.OP_FILE_SYNC -> {
                handleIncomingFile(frame.payload)
            }

            LinkSyncProtocol.OP_FILE_SYNC_ACK -> {
                handleFileSyncAck(frame.payload)
            }
        }
    }

    private fun handleFileSyncAck(payload: ByteArray) {
        scope.launch(Dispatchers.IO) {
            try {
                val ackJson = String(payload, StandardCharsets.UTF_8)
                val ack = JSONObject(ackJson)
                val fileName = ack.optString("fileName", "")
                val status = ack.optString("status", "")
                Log.i("LinkSyncService", "Received OP_FILE_SYNC_ACK from laptop for $fileName (status=$status)")

                val prefs = getSharedPreferences("linksync_prefs", Context.MODE_PRIVATE)
                val autoDelete = prefs.getBoolean("auto_delete_synced_screenshots", false)

                if (autoDelete && fileName.isNotEmpty()) {
                    var deleted = false

                    // 1. Check if we tracked URI for this file
                    val trackedUri = sentFileUris.remove(fileName)
                    if (trackedUri != null) {
                        try {
                            val rows = contentResolver.delete(trackedUri, null, null)
                            if (rows > 0) deleted = true
                        } catch (e: Exception) {
                            Log.w("LinkSyncService", "Could not delete tracked URI: $trackedUri", e)
                        }
                    }

                    // 2. Search common screenshot directories
                    val searchDirs = listOf(
                        File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_PICTURES), "Screenshots"),
                        File(File(Environment.getExternalStorageDirectory(), "DCIM"), "Screenshots"),
                        File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_MOVIES), "LinkSync")
                    )

                    for (dir in searchDirs) {
                        if (dir.exists()) {
                            val candidate = File(dir, fileName)
                            if (candidate.exists()) {
                                if (candidate.delete()) {
                                    deleted = true
                                    Log.i("LinkSyncService", "Deleted local file: ${candidate.absolutePath}")
                                }
                            }
                        }
                    }

                    // 3. Query MediaStore to remove indexing
                    try {
                        val uri = MediaStore.Images.Media.EXTERNAL_CONTENT_URI
                        val rows = contentResolver.delete(
                            uri,
                            "${MediaStore.MediaColumns.DISPLAY_NAME} = ?",
                            arrayOf(fileName)
                        )
                        if (rows > 0) deleted = true
                    } catch (e: Exception) {
                        Log.w("LinkSyncService", "MediaStore delete error", e)
                    }

                    if (deleted) {
                        Log.i("LinkSyncService", "Auto-deleted synced screenshot: $fileName")
                        updateNotification("🗑️ Auto-deleted $fileName (Saved on Laptop)")
                        fileSyncEvent.emit(fileName)
                    }
                }
            } catch (e: Exception) {
                Log.e("LinkSyncService", "Error handling file sync ack", e)
            }
        }
    }

    private fun handleIncomingFile(payload: ByteArray) {
        scope.launch(Dispatchers.IO) {
            try {
                if (payload.size < 4) return@launch
                val metaLen = ByteBuffer.wrap(payload, 0, 4).order(ByteOrder.BIG_ENDIAN).int
                if (payload.size < 4 + metaLen) return@launch

                val metaJson = String(payload, 4, metaLen, StandardCharsets.UTF_8)
                val meta = JSONObject(metaJson)
                val fileName = meta.optString("fileName", "LinkSync_${System.currentTimeMillis()}.png")

                val fileOffset = 4 + metaLen
                val fileBytes = payload.copyOfRange(fileOffset, payload.size)

                val isVideo = fileName.endsWith(".mp4", ignoreCase = true)
                val targetDir = if (isVideo) {
                    File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_MOVIES), "LinkSync")
                } else {
                    File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_PICTURES), "Screenshots")
                }
                if (!targetDir.exists()) targetDir.mkdirs()

                val destFile = File(targetDir, fileName)
                destFile.writeBytes(fileBytes)
                Log.i("LinkSyncService", "Saved incoming file from laptop: ${destFile.absolutePath} (${fileBytes.size} bytes, isVideo=$isVideo)")

                val mimeType = if (isVideo) {
                    "video/mp4"
                } else if (fileName.endsWith(".jpg", ignoreCase = true) || fileName.endsWith(".jpeg", ignoreCase = true)) {
                    "image/jpeg"
                } else {
                    "image/png"
                }

                MediaScannerConnection.scanFile(
                    applicationContext,
                    arrayOf(destFile.absolutePath),
                    arrayOf(mimeType)
                ) { path, uri ->
                    Log.i("LinkSyncService", "Indexed incoming file from PC: $path -> $uri ($mimeType)")
                    if (!isVideo && uri != null) {
                        Handler(Looper.getMainLooper()).post {
                            try {
                                val clip = ClipData.newUri(contentResolver, "Screenshot from Laptop", uri)
                                clipboardManager?.setPrimaryClip(clip)
                            } catch (e: Exception) {
                                Log.w("LinkSyncService", "Could not set image to clipboard", e)
                            }
                        }
                    }
                    scope.launch {
                        fileSyncEvent.emit(fileName)
                    }
                }

                updateNotification(if (isVideo) "🎬 Received $fileName from Laptop" else "📸 Received $fileName from Laptop")
            } catch (e: Exception) {
                Log.e("LinkSyncService", "Error saving file from laptop", e)
            }
        }
    }

    private fun setupClipboardSync() {
        clipboardManager?.addPrimaryClipChangedListener {
            try {
                val clip = clipboardManager?.primaryClip
                if (clip != null && clip.itemCount > 0) {
                    val text = clip.getItemAt(0).text?.toString() ?: ""
                    if (text.isNotEmpty()) {
                        sendClipboardInternal(text)
                    }
                }
            } catch (e: Exception) {
                // Background read denied on Android 10+; handled by ClipboardLogcatWatcher
            }
        }
    }

    private fun setupScreenshotObserver() {
        screenshotObserver = ScreenshotObserver(applicationContext) { uri, fileName ->
            scope.launch {
                sendMediaFile(uri, fileName)
            }
        }
        contentResolver.registerContentObserver(
            MediaStore.Images.Media.EXTERNAL_CONTENT_URI,
            true,
            screenshotObserver!!
        )

        screenshotFileWatcher = ScreenshotFileWatcher(applicationContext) { uri, fileName, _ ->
            scope.launch {
                sendMediaFile(uri, fileName)
            }
        }
        screenshotFileWatcher?.start()
    }

    fun sendMediaFile(uri: Uri, fileName: String) {
        sentFileUris[fileName] = uri
        val s = socket
        if (s == null || !s.isConnected) {
            Log.w("LinkSyncService", "Cannot send media: socket is null/disconnected")
            return
        }

        scope.launch(Dispatchers.IO) {
            try {
                // Retry reading stream up to 3 times with 150ms backoff in case file write is finalizing
                var bytes: ByteArray? = null
                for (attempt in 1..3) {
                    try {
                        contentResolver.openInputStream(uri)?.use { stream ->
                            val read = stream.readBytes()
                            if (read.isNotEmpty()) {
                                bytes = read
                            }
                        }
                        if (bytes != null) break
                    } catch (e: Exception) {
                        delay(150)
                    }
                }

                if (bytes == null || bytes.isEmpty()) {
                    Log.e("LinkSyncService", "Failed to read bytes for media $fileName from $uri")
                    return@launch
                }

                val mime = when {
                    fileName.endsWith(".mp4", ignoreCase = true) -> "video/mp4"
                    fileName.endsWith(".jpg", ignoreCase = true) || fileName.endsWith(".jpeg", ignoreCase = true) -> "image/jpeg"
                    else -> "image/png"
                }

                val metaJson = JSONObject().apply {
                    put("fileName", fileName)
                    put("fileSize", bytes.size)
                    put("mimeType", mime)
                    put("timestamp", System.currentTimeMillis())
                }

                val metaBytes = metaJson.toString().toByteArray(StandardCharsets.UTF_8)
                val buffer = ByteArrayOutputStream()
                buffer.write(ByteBuffer.allocate(4).order(ByteOrder.BIG_ENDIAN).putInt(metaBytes.size).array())
                buffer.write(metaBytes)
                buffer.write(bytes)

                LinkSyncProtocol.writeFrame(
                    s.getOutputStream(),
                    LinkSyncProtocol.OP_FILE_SYNC,
                    buffer.toByteArray()
                )
                Log.i("LinkSyncService", "Pushed media file $fileName (${bytes.size} bytes) to PC")
            } catch (e: Exception) {
                Log.e("LinkSyncService", "Failed to transfer media file $fileName", e)
            }
        }
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                getString(R.string.notification_channel_name),
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = getString(R.string.notification_channel_desc)
            }
            val manager = getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(channel)
        }
    }

    private fun buildNotification(status: String): Notification {
        val clipIntent = Intent(this, ClipSyncActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_NO_ANIMATION
        }
        val clipPendingIntent = PendingIntent.getActivity(
            this,
            201,
            clipIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("LinkSync Bridge")
            .setContentText(status)
            .setSmallIcon(R.drawable.ic_screen_record)
            .setOngoing(true)
            .addAction(
                R.drawable.ic_screen_record,
                "📋 Send Copied Text to PC",
                clipPendingIntent
            )
            .build()
    }

    private fun updateNotification(status: String) {
        val manager = getSystemService(NotificationManager::class.java)
        manager.notify(NOTIFICATION_ID, buildNotification(status))
    }

    override fun onDestroy() {
        super.onDestroy()
        clipboardLogcatWatcher?.stop()
        screenshotObserver?.let { contentResolver.unregisterContentObserver(it) }
        screenshotFileWatcher?.stop()
        NetworkDiscovery.stopPassiveListener()
        activeInstance = null
        scope.cancel()
        connectionState.value = BridgeConnectionState(isConnected = false)
        try { socket?.close() } catch (_: Exception) {}
    }

    override fun onBind(intent: Intent?): IBinder? = null
}
