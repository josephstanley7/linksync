package com.linksync.app

import android.Manifest
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Typeface
import android.media.MediaMetadataRetriever
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.provider.Settings
import android.text.TextUtils
import android.util.LruCache
import android.view.Gravity
import android.view.View
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.core.content.FileProvider
import androidx.lifecycle.lifecycleScope
import com.google.android.material.button.MaterialButton
import com.google.android.material.card.MaterialCardView
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.linksync.app.databinding.ActivityMainBinding
import com.linksync.app.net.DiscoveredDesktop
import com.linksync.app.net.NetworkDiscovery
import com.linksync.app.service.ClipboardHistoryItem
import com.linksync.app.service.ForegroundSyncService
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding

    // 12MB in-memory LRU thumbnail cache to guarantee sub-millisecond scrolling with 0% GC stutter
    private val thumbnailCache = object : LruCache<String, Bitmap>(12 * 1024 * 1024) {
        override fun sizeOf(key: String, bitmap: Bitmap): Int = bitmap.byteCount
    }

    private val permissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { permissions ->
        val allGranted = permissions.entries.all { it.value }
        if (allGranted) {
            startSyncService()
            loadPhoneMediaFiles()
        } else {
            Toast.makeText(this, "Storage and media permissions required for LinkSync", Toast.LENGTH_SHORT).show()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        // Ensure system status bar and navigation bars don't overlap app header or bottom bar
        androidx.core.view.ViewCompat.setOnApplyWindowInsetsListener(binding.root) { view, windowInsets ->
            val insets = windowInsets.getInsets(androidx.core.view.WindowInsetsCompat.Type.systemBars())
            view.setPadding(insets.left, insets.top, insets.right, insets.bottom)
            windowInsets
        }

        val prefs = getSharedPreferences("linksync_prefs", Context.MODE_PRIVATE)
        val savedHost = prefs.getString("manual_target_host", "") ?: ""
        if (savedHost.isNotEmpty()) {
            binding.etTargetHost.setText(savedHost)
        }

        // Collapsible Advanced Network Settings
        binding.btnToggleSettings.setOnClickListener {
            val isVisible = binding.layoutAdvancedSettings.visibility == View.VISIBLE
            binding.layoutAdvancedSettings.visibility = if (isVisible) View.GONE else View.VISIBLE
            binding.btnToggleSettings.text = if (isVisible) "⚙️ Advanced Network Settings" else "▲ Hide Advanced Settings"
        }

        binding.btnConnect.setOnClickListener {
            if (ForegroundSyncService.connectionState.value.isConnected) {
                val stopIntent = Intent(this, ForegroundSyncService::class.java).apply {
                    action = ForegroundSyncService.ACTION_STOP
                }
                startService(stopIntent)
            } else {
                val host = binding.etTargetHost.text?.toString()?.trim() ?: ""
                saveAndApplyHost(host)
                checkPermissionsAndStart()
                val reconnectIntent = Intent(this, ForegroundSyncService::class.java).apply {
                    action = ForegroundSyncService.ACTION_RECONNECT
                }
                startService(reconnectIntent)
            }
        }

        // Feature 13: Auto-Delete from Phone Switch
        val isAutoDelete = prefs.getBoolean("auto_delete_synced_screenshots", false)
        binding.swAutoDelete.isChecked = isAutoDelete
        binding.swAutoDelete.setOnCheckedChangeListener { _, isChecked ->
            prefs.edit().putBoolean("auto_delete_synced_screenshots", isChecked).apply()
            if (isChecked) {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R && !Environment.isExternalStorageManager()) {
                    try {
                        val intent = Intent(
                            Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
                            Uri.parse("package:$packageName")
                        )
                        startActivity(intent)
                    } catch (e: Exception) {
                        try {
                            val intent = Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION)
                            startActivity(intent)
                        } catch (_: Exception) {}
                    }
                }
                Toast.makeText(this, "Auto-Delete enabled: Screenshots deleted once stored on laptop", Toast.LENGTH_SHORT).show()
            } else {
                Toast.makeText(this, "Auto-Delete disabled: Screenshots preserved on phone", Toast.LENGTH_SHORT).show()
            }
        }

        // Layout Toggle: Grid (2 on a row) vs List
        var currentLayoutMode = prefs.getString("screenshot_layout_mode", "grid") ?: "grid"
        fun updateLayoutToggleButtons() {
            if (currentLayoutMode == "grid") {
                binding.btnLayoutGrid.setBackgroundResource(R.drawable.bg_pill_toggle_selected)
                binding.btnLayoutGrid.setTextColor(0xFFFFFFFF.toInt())
                binding.btnLayoutGrid.typeface = Typeface.DEFAULT_BOLD

                binding.btnLayoutList.setBackgroundColor(0x00000000)
                binding.btnLayoutList.setTextColor(0xFF94A3B8.toInt())
                binding.btnLayoutList.typeface = Typeface.DEFAULT
            } else {
                binding.btnLayoutList.setBackgroundResource(R.drawable.bg_pill_toggle_selected)
                binding.btnLayoutList.setTextColor(0xFFFFFFFF.toInt())
                binding.btnLayoutList.typeface = Typeface.DEFAULT_BOLD

                binding.btnLayoutGrid.setBackgroundColor(0x00000000)
                binding.btnLayoutGrid.setTextColor(0xFF94A3B8.toInt())
                binding.btnLayoutGrid.typeface = Typeface.DEFAULT
            }
        }
        updateLayoutToggleButtons()

        binding.btnLayoutGrid.setOnClickListener {
            currentLayoutMode = "grid"
            prefs.edit().putString("screenshot_layout_mode", "grid").apply()
            updateLayoutToggleButtons()
            loadPhoneMediaFiles()
        }

        binding.btnLayoutList.setOnClickListener {
            currentLayoutMode = "list"
            prefs.edit().putString("screenshot_layout_mode", "list").apply()
            updateLayoutToggleButtons()
            loadPhoneMediaFiles()
        }

        // Auto-start sync service on launch
        checkPermissionsAndStart()

        // Bottom Navigation Tab Switching
        binding.bottomNavigation.setOnItemSelectedListener { item ->
            when (item.itemId) {
                R.id.nav_connect -> {
                    binding.layoutConnect.visibility = View.VISIBLE
                    binding.layoutClipboard.visibility = View.GONE
                    binding.layoutScreenshots.visibility = View.GONE
                    true
                }
                R.id.nav_clipboard -> {
                    binding.layoutConnect.visibility = View.GONE
                    binding.layoutClipboard.visibility = View.VISIBLE
                    binding.layoutScreenshots.visibility = View.GONE
                    true
                }
                R.id.nav_screenshots -> {
                    binding.layoutConnect.visibility = View.GONE
                    binding.layoutClipboard.visibility = View.GONE
                    binding.layoutScreenshots.visibility = View.VISIBLE
                    loadPhoneMediaFiles()
                    true
                }
                else -> false
            }
        }

        binding.btnClearClipboard.setOnClickListener {
            ForegroundSyncService.clearHistory()
            Toast.makeText(this, "Clipboard history cleared", Toast.LENGTH_SHORT).show()
        }

        binding.btnRefreshMedia.setOnClickListener {
            loadPhoneMediaFiles()
            Toast.makeText(this, "Screenshots refreshed", Toast.LENGTH_SHORT).show()
        }

        binding.btnSelectPc.setOnClickListener {
            showAvailableComputersDialog()
        }

        // Live observation of two-way clipboard stream
        lifecycleScope.launch {
            ForegroundSyncService.clipboardHistory.collect { items ->
                renderClipboardHistory(items)
            }
        }

        // Auto-refresh when files are synced or auto-deleted
        lifecycleScope.launch {
            ForegroundSyncService.fileSyncEvent.collect {
                loadPhoneMediaFiles()
            }
        }

        // Live computer discovery observation (Multi-PC selection & switching)
        lifecycleScope.launch {
            NetworkDiscovery.discoveredDesktops.collect { list ->
                if (list.isEmpty()) {
                    binding.btnSelectPc.visibility = View.GONE
                } else if (list.size == 1) {
                    val pc = list.first()
                    binding.btnSelectPc.visibility = View.VISIBLE
                    binding.btnSelectPc.text = "💻 Computer: ${pc.deviceName}"
                } else {
                    binding.btnSelectPc.visibility = View.VISIBLE
                    binding.btnSelectPc.text = "💻 Switch Computer (${list.size} online)"
                }
            }
        }

        // Live connection status observation
        lifecycleScope.launch {
            ForegroundSyncService.connectionState.collect { state ->
                if (state.isConnected) {
                    val name = if (state.desktopName.isNotEmpty()) state.desktopName else "HP EliteBook Folio 9470M"
                    binding.tvConnectionStatus.text = "🟢 $name"
                    binding.tvConnectionStatus.setTextColor(0xFF10B981.toInt()) // Emerald Green
                    binding.tvDeviceDetails.text = state.lastSyncMessage.ifEmpty { "Connected & Syncing" }
                    binding.tvFeaturePill.visibility = View.VISIBLE
                    binding.btnConnect.text = "Disconnect"
                    binding.btnConnect.setBackgroundColor(0xFF334155.toInt())
                } else {
                    binding.tvConnectionStatus.text = "🟠 Searching for Laptop..."
                    binding.tvConnectionStatus.setTextColor(0xFFF59E0B.toInt()) // Amber
                    binding.tvDeviceDetails.text = state.lastSyncMessage.ifEmpty { "Open LinkSync on your laptop to connect" }
                    binding.tvFeaturePill.visibility = View.GONE
                    binding.btnConnect.text = "⚡ Connect / Reconnect"
                    binding.btnConnect.setBackgroundColor(0xFF2563EB.toInt())
                }
            }
        }

        checkPermissionsAndStart()
        loadPhoneMediaFiles()
    }

    override fun onResume() {
        super.onResume()
        loadPhoneMediaFiles()

        // Sync clipboard if new text was copied while LinkSync was in background
        try {
            val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            val clip = clipboard.primaryClip
            if (clip != null && clip.itemCount > 0) {
                val text = clip.getItemAt(0).text?.toString() ?: ""
                if (text.isNotEmpty()) {
                    ForegroundSyncService.sendClipboardDirect(text)
                }
            }
        } catch (_: Exception) {}
    }

    private fun showAvailableComputersDialog() {
        val desktops = NetworkDiscovery.discoveredDesktops.value
        val prefs = getSharedPreferences("linksync_prefs", Context.MODE_PRIVATE)
        val currentPairedId = prefs.getString("paired_device_id", "") ?: ""

        if (desktops.isEmpty()) {
            Toast.makeText(this, "No LinkSync computers discovered on this network yet.", Toast.LENGTH_SHORT).show()
            return
        }

        val items = desktops.map { desktop ->
            val isCurrent = (desktop.deviceId == currentPairedId)
            "${if (isCurrent) "✓ " else "  "}${desktop.deviceName} (${desktop.ipAddress})"
        }.toTypedArray()

        MaterialAlertDialogBuilder(this)
            .setTitle("Available Computers (${desktops.size})")
            .setItems(items) { _, which ->
                val selected = desktops[which]
                prefs.edit()
                    .putString("paired_device_id", selected.deviceId)
                    .putString("paired_device_name", selected.deviceName)
                    .putString("last_discovered_ip", selected.ipAddress)
                    .apply()
                Toast.makeText(this, "Paired with ${selected.deviceName}", Toast.LENGTH_SHORT).show()
                val startIntent = Intent(this, ForegroundSyncService::class.java)
                startService(startIntent)
            }
            .setNegativeButton("Cancel", null)
            .setNeutralButton("Unpair (Auto-Select)") { _, _ ->
                prefs.edit().remove("paired_device_id").remove("paired_device_name").apply()
                Toast.makeText(this, "Cleared paired computer preference", Toast.LENGTH_SHORT).show()
            }
            .show()
    }

    private fun saveAndApplyHost(host: String) {
        val prefs = getSharedPreferences("linksync_prefs", Context.MODE_PRIVATE)
        prefs.edit()
            .putString("manual_target_host", host)
            .apply()
        ForegroundSyncService.targetHost = host.ifEmpty { "127.0.0.1" }
        if (host.isNotEmpty()) {
            Toast.makeText(this, "Manual target set to $host", Toast.LENGTH_SHORT).show()
        }
    }

    private fun checkPermissionsAndStart() {
        val permissions = mutableListOf<String>()

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            permissions.add(Manifest.permission.READ_MEDIA_IMAGES)
            permissions.add(Manifest.permission.READ_MEDIA_VIDEO)
            permissions.add(Manifest.permission.POST_NOTIFICATIONS)
        } else {
            permissions.add(Manifest.permission.READ_EXTERNAL_STORAGE)
        }

        val missing = permissions.filter {
            ContextCompat.checkSelfPermission(this, it) != PackageManager.PERMISSION_GRANTED
        }

        if (missing.isEmpty()) {
            startSyncService()
            loadPhoneMediaFiles()
        } else {
            permissionLauncher.launch(missing.toTypedArray())
        }
    }

    private fun startSyncService() {
        val intent = Intent(this, ForegroundSyncService::class.java)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            startForegroundService(intent)
        } else {
            startService(intent)
        }
    }

    private fun renderClipboardHistory(items: List<ClipboardHistoryItem>) {
        binding.llClipboardHistory.removeAllViews()

        if (items.isEmpty()) {
            val emptyTv = TextView(this).apply {
                text = "No clipboard history yet.\nCopy any text on your phone or laptop to sync!"
                setTextColor(0xFF64748B.toInt())
                textSize = 13f
                gravity = Gravity.CENTER
                setPadding(0, 80, 0, 80)
            }
            binding.llClipboardHistory.addView(emptyTv)
            return
        }

        val timeFormat = SimpleDateFormat("HH:mm:ss", Locale.getDefault())

        for (item in items) {
            val card = MaterialCardView(this).apply {
                layoutParams = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT
                ).apply { setMargins(0, 0, 0, 12) }
                setCardBackgroundColor(0xFF151D2A.toInt())
                strokeColor = 0xFF232F42.toInt()
                strokeWidth = 1
                radius = 14f
                cardElevation = 0f
            }

            val cardLayout = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(18, 14, 18, 14)
            }

            val headerRow = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
            }

            val sourceText = if (item.fromPhone) "📱 PHONE CLIP" else "💻 PC CLIP"
            val sourceColor = if (item.fromPhone) 0xFF38BDF8.toInt() else 0xFF34D399.toInt()

            val badgeTv = TextView(this).apply {
                text = sourceText
                setTextColor(sourceColor)
                textSize = 11f
                typeface = Typeface.DEFAULT_BOLD
            }

            val timeTv = TextView(this).apply {
                layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f).apply {
                    setMargins(10, 0, 0, 0)
                }
                text = timeFormat.format(Date(item.timestamp))
                setTextColor(0xFF94A3B8.toInt())
                textSize = 11f
            }

            val btnCopy = MaterialButton(this).apply {
                text = "📋 Copy"
                textSize = 11f
                setTextColor(0xFF38BDF8.toInt())
                setBackgroundColor(0x00000000)
                setPadding(0, 0, 0, 0)
                minWidth = 0
                minHeight = 0
                setOnClickListener {
                    val clip = ClipData.newPlainText("LinkSync", item.text)
                    val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                    cm.setPrimaryClip(clip)
                    Toast.makeText(this@MainActivity, "Copied to clipboard!", Toast.LENGTH_SHORT).show()
                }
            }

            headerRow.addView(badgeTv)
            headerRow.addView(timeTv)
            headerRow.addView(btnCopy)

            val textTv = TextView(this).apply {
                layoutParams = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT
                ).apply { setMargins(0, 8, 0, 0) }
                text = item.text
                setTextColor(0xFFF8FAFC.toInt())
                textSize = 13f
                maxLines = 6
                setTextIsSelectable(true)
            }

            cardLayout.addView(headerRow)
            cardLayout.addView(textTv)
            card.addView(cardLayout)
            binding.llClipboardHistory.addView(card)
        }
    }

    private fun loadPhoneMediaFiles() {
        binding.llMediaList.removeAllViews()

        val mediaFiles = mutableListOf<File>()

        // 1. Recordings directory
        val recDir = File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_MOVIES), "LinkSync")
        if (recDir.exists()) {
            recDir.listFiles { file -> file.extension.equals("mp4", ignoreCase = true) }?.let {
                mediaFiles.addAll(it)
            }
        }

        // 2. Screenshots directory (Standard Android)
        val picturesDir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_PICTURES)
        val screenshotsDir = File(picturesDir, "Screenshots")
        if (screenshotsDir.exists()) {
            screenshotsDir.listFiles { file ->
                file.extension.equals("png", ignoreCase = true) || file.extension.equals("jpg", ignoreCase = true)
            }?.let {
                mediaFiles.addAll(it)
            }
        }

        // 3. DCIM Screenshots (Samsung Galaxy standard)
        val dcimScreenshots = File(File(Environment.getExternalStorageDirectory(), "DCIM"), "Screenshots")
        if (dcimScreenshots.exists()) {
            dcimScreenshots.listFiles { file ->
                file.extension.equals("png", ignoreCase = true) || file.extension.equals("jpg", ignoreCase = true)
            }?.let {
                mediaFiles.addAll(it)
            }
        }

        // Sort descending by last modified
        mediaFiles.sortByDescending { it.lastModified() }

        if (mediaFiles.isEmpty()) {
            val emptyTv = TextView(this).apply {
                text = "No recordings or screenshots found yet.\nTake a screenshot (Power + Volume Down) to view and sync!"
                setTextColor(0xFF64748B.toInt())
                textSize = 12f
                gravity = Gravity.CENTER
                setPadding(0, 60, 0, 60)
            }
            binding.llMediaList.addView(emptyTv)
            return
        }

        val prefs = getSharedPreferences("linksync_prefs", Context.MODE_PRIVATE)
        val isGrid = prefs.getString("screenshot_layout_mode", "grid") == "grid"
        val displayFiles = mediaFiles.take(24)

        if (isGrid) {
            renderGridMediaFiles(displayFiles)
        } else {
            renderListMediaFiles(displayFiles)
        }
    }

    /**
     * Renders screenshots arranged 2 on a row with visual thumbnail previews.
     */
    private fun renderGridMediaFiles(files: List<File>) {
        val pairs = files.chunked(2)

        for (pair in pairs) {
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                layoutParams = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT
                ).apply { setMargins(0, 0, 0, 10) }
                weightSum = 2f
            }

            val card1 = buildGridMediaCard(pair[0], isLeft = true)
            row.addView(card1)

            if (pair.size > 1) {
                val card2 = buildGridMediaCard(pair[1], isLeft = false)
                row.addView(card2)
            } else {
                // Invisible placeholder to keep the left card at exactly 50% width
                val placeholder = View(this).apply {
                    layoutParams = LinearLayout.LayoutParams(0, 1, 1f).apply {
                        setMargins(5, 0, 0, 0)
                    }
                    visibility = View.INVISIBLE
                }
                row.addView(placeholder)
            }

            binding.llMediaList.addView(row)
        }
    }

    private fun buildGridMediaCard(file: File, isLeft: Boolean): MaterialCardView {
        val isVideo = file.extension.equals("mp4", ignoreCase = true)
        val sizeMb = file.length() / (1024.0 * 1024.0)
        val sizeText = if (sizeMb >= 1.0) String.format(Locale.US, "%.1f MB", sizeMb) else String.format(Locale.US, "%d KB", file.length() / 1024)
        val dateText = SimpleDateFormat("MMM dd, HH:mm", Locale.US).format(Date(file.lastModified()))

        val card = MaterialCardView(this).apply {
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f).apply {
                if (isLeft) setMargins(0, 0, 5, 0) else setMargins(5, 0, 0, 0)
            }
            setCardBackgroundColor(0xFF151D2A.toInt())
            strokeColor = 0xFF232F42.toInt()
            strokeWidth = 1
            radius = 14f
            cardElevation = 0f
        }

        val cardLayout = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
        }

        // 1. Preview Container
        val previewFrame = FrameLayout(this).apply {
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                (110 * resources.displayMetrics.density).toInt()
            )
            setBackgroundColor(0xFF0B0F17.toInt())
        }

        val ivThumbnail = ImageView(this).apply {
            layoutParams = FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT
            )
            scaleType = ImageView.ScaleType.CENTER_CROP
        }
        previewFrame.addView(ivThumbnail)

        // Load downsampled thumbnail asynchronously
        loadThumbnailIntoView(file, ivThumbnail, reqWidth = 260, reqHeight = 220)

        // Video Badge Overlay
        if (isVideo) {
            val playBadge = TextView(this).apply {
                layoutParams = FrameLayout.LayoutParams(
                    (32 * resources.displayMetrics.density).toInt(),
                    (32 * resources.displayMetrics.density).toInt(),
                    Gravity.CENTER
                )
                text = "▶"
                textSize = 14f
                setTextColor(0xFFFFFFFF.toInt())
                gravity = Gravity.CENTER
                background = ContextCompat.getDrawable(this@MainActivity, R.drawable.badge_video_circle)
            }
            previewFrame.addView(playBadge)
        }

        // Top-right File Size Badge
        val sizeBadge = TextView(this).apply {
            layoutParams = FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.WRAP_CONTENT,
                FrameLayout.LayoutParams.WRAP_CONTENT,
                Gravity.TOP or Gravity.END
            ).apply {
                setMargins(0, (6 * resources.displayMetrics.density).toInt(), (6 * resources.displayMetrics.density).toInt(), 0)
            }
            text = sizeText
            textSize = 9.5f
            typeface = Typeface.DEFAULT_BOLD
            setTextColor(0xFFF8FAFC.toInt())
            setBackgroundColor(0xCC0B0F17.toInt())
            setPadding(
                (6 * resources.displayMetrics.density).toInt(),
                (2 * resources.displayMetrics.density).toInt(),
                (6 * resources.displayMetrics.density).toInt(),
                (2 * resources.displayMetrics.density).toInt()
            )
        }
        previewFrame.addView(sizeBadge)

        // 2. Details (Name & Date)
        val detailsLayout = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(
                (10 * resources.displayMetrics.density).toInt(),
                (8 * resources.displayMetrics.density).toInt(),
                (10 * resources.displayMetrics.density).toInt(),
                (4 * resources.displayMetrics.density).toInt()
            )
        }

        val tvName = TextView(this).apply {
            text = file.name
            setTextColor(0xFFF8FAFC.toInt())
            textSize = 11.5f
            typeface = Typeface.DEFAULT_BOLD
            maxLines = 1
            ellipsize = TextUtils.TruncateAt.END
        }

        val tvDate = TextView(this).apply {
            text = dateText
            setTextColor(0xFF94A3B8.toInt())
            textSize = 10f
        }

        detailsLayout.addView(tvName)
        detailsLayout.addView(tvDate)

        // 3. Action Buttons Row
        val btnRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(
                (8 * resources.displayMetrics.density).toInt(),
                (4 * resources.displayMetrics.density).toInt(),
                (8 * resources.displayMetrics.density).toInt(),
                (8 * resources.displayMetrics.density).toInt()
            )
        }

        val btnOpen = MaterialButton(this).apply {
            layoutParams = LinearLayout.LayoutParams(0, (32 * resources.displayMetrics.density).toInt(), 1f).apply {
                setMargins(0, 0, 4, 0)
            }
            text = "▶ Open"
            textSize = 10f
            typeface = Typeface.DEFAULT_BOLD
            setBackgroundColor(0xFF1E3A8A.toInt()) // Deep Blue
            setTextColor(0xFF93C5FD.toInt())
            cornerRadius = (8 * resources.displayMetrics.density).toInt()
            setPadding(0, 0, 0, 0)
            minWidth = 0
            minHeight = 0
            setOnClickListener { openMediaFile(file) }
        }

        val btnSync = MaterialButton(this).apply {
            layoutParams = LinearLayout.LayoutParams(0, (32 * resources.displayMetrics.density).toInt(), 1f).apply {
                setMargins(4, 0, 0, 0)
            }
            text = "⚡ Sync"
            textSize = 10f
            typeface = Typeface.DEFAULT_BOLD
            setBackgroundColor(0xFF064E3B.toInt()) // Deep Emerald
            setTextColor(0xFF6EE7B7.toInt())
            cornerRadius = (8 * resources.displayMetrics.density).toInt()
            setPadding(0, 0, 0, 0)
            minWidth = 0
            minHeight = 0
            setOnClickListener {
                try {
                    val uri = FileProvider.getUriForFile(this@MainActivity, "$packageName.fileprovider", file)
                    ForegroundSyncService.sendMediaFileDirect(this@MainActivity, uri, file.name)
                    Toast.makeText(this@MainActivity, "Streaming ${file.name} to laptop...", Toast.LENGTH_SHORT).show()
                } catch (e: Exception) {
                    Toast.makeText(this@MainActivity, "Send error: ${e.message}", Toast.LENGTH_SHORT).show()
                }
            }
        }

        btnRow.addView(btnOpen)
        btnRow.addView(btnSync)

        cardLayout.addView(previewFrame)
        cardLayout.addView(detailsLayout)
        cardLayout.addView(btnRow)
        card.addView(cardLayout)

        return card
    }

    /**
     * Renders screenshots in 1-column list view with left thumbnail preview and full actions.
     */
    private fun renderListMediaFiles(files: List<File>) {
        for (file in files) {
            val isVideo = file.extension.equals("mp4", ignoreCase = true)
            val sizeMb = file.length() / (1024.0 * 1024.0)
            val sizeText = if (sizeMb >= 1.0) String.format(Locale.US, "%.1f MB", sizeMb) else String.format(Locale.US, "%d KB", file.length() / 1024)
            val dateText = SimpleDateFormat("MMM dd, HH:mm", Locale.US).format(Date(file.lastModified()))

            val card = MaterialCardView(this).apply {
                layoutParams = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT
                ).apply { setMargins(0, 0, 0, 10) }
                setCardBackgroundColor(0xFF151D2A.toInt())
                strokeColor = 0xFF232F42.toInt()
                strokeWidth = 1
                radius = 14f
                cardElevation = 0f
            }

            val cardLayout = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                setPadding(
                    (12 * resources.displayMetrics.density).toInt(),
                    (10 * resources.displayMetrics.density).toInt(),
                    (12 * resources.displayMetrics.density).toInt(),
                    (10 * resources.displayMetrics.density).toInt()
                )
            }

            // Left Thumbnail
            val thumbFrame = FrameLayout(this).apply {
                layoutParams = LinearLayout.LayoutParams(
                    (68 * resources.displayMetrics.density).toInt(),
                    (68 * resources.displayMetrics.density).toInt()
                )
                setBackgroundColor(0xFF0B0F17.toInt())
            }

            val ivThumbnail = ImageView(this).apply {
                layoutParams = FrameLayout.LayoutParams(
                    FrameLayout.LayoutParams.MATCH_PARENT,
                    FrameLayout.LayoutParams.MATCH_PARENT
                )
                scaleType = ImageView.ScaleType.CENTER_CROP
            }
            thumbFrame.addView(ivThumbnail)
            loadThumbnailIntoView(file, ivThumbnail, reqWidth = 140, reqHeight = 140)

            if (isVideo) {
                val playBadge = TextView(this).apply {
                    layoutParams = FrameLayout.LayoutParams(
                        (24 * resources.displayMetrics.density).toInt(),
                        (24 * resources.displayMetrics.density).toInt(),
                        Gravity.CENTER
                    )
                    text = "▶"
                    textSize = 10f
                    setTextColor(0xFFFFFFFF.toInt())
                    gravity = Gravity.CENTER
                    background = ContextCompat.getDrawable(this@MainActivity, R.drawable.badge_video_circle)
                }
                thumbFrame.addView(playBadge)
            }

            // Center details
            val centerLayout = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f).apply {
                    setMargins((12 * resources.displayMetrics.density).toInt(), 0, (8 * resources.displayMetrics.density).toInt(), 0)
                }
            }

            val tvName = TextView(this).apply {
                text = file.name
                setTextColor(0xFFF8FAFC.toInt())
                textSize = 13f
                typeface = Typeface.DEFAULT_BOLD
                maxLines = 1
                ellipsize = TextUtils.TruncateAt.END
            }

            val tvMeta = TextView(this).apply {
                text = "$dateText • $sizeText"
                setTextColor(0xFF94A3B8.toInt())
                textSize = 11f
                setPadding(0, (2 * resources.displayMetrics.density).toInt(), 0, 0)
            }

            val tvType = TextView(this).apply {
                text = if (isVideo) "🎬 Video Recording" else "📸 Phone Screenshot"
                setTextColor(if (isVideo) 0xFF34D399.toInt() else 0xFF38BDF8.toInt())
                textSize = 10f
                typeface = Typeface.DEFAULT_BOLD
                setPadding(0, (3 * resources.displayMetrics.density).toInt(), 0, 0)
            }

            centerLayout.addView(tvName)
            centerLayout.addView(tvMeta)
            centerLayout.addView(tvType)

            // Right Actions Column
            val actionCol = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                gravity = Gravity.CENTER
            }

            val btnOpen = MaterialButton(this).apply {
                layoutParams = LinearLayout.LayoutParams((76 * resources.displayMetrics.density).toInt(), (32 * resources.displayMetrics.density).toInt()).apply {
                    setMargins(0, 0, 0, (4 * resources.displayMetrics.density).toInt())
                }
                text = "▶ Open"
                textSize = 10f
                typeface = Typeface.DEFAULT_BOLD
                setBackgroundColor(0xFF1E3A8A.toInt())
                setTextColor(0xFF93C5FD.toInt())
                cornerRadius = (8 * resources.displayMetrics.density).toInt()
                setPadding(0, 0, 0, 0)
                minWidth = 0
                minHeight = 0
                setOnClickListener { openMediaFile(file) }
            }

            val btnSync = MaterialButton(this).apply {
                layoutParams = LinearLayout.LayoutParams((76 * resources.displayMetrics.density).toInt(), (32 * resources.displayMetrics.density).toInt())
                text = "⚡ Sync"
                textSize = 10f
                typeface = Typeface.DEFAULT_BOLD
                setBackgroundColor(0xFF064E3B.toInt())
                setTextColor(0xFF6EE7B7.toInt())
                cornerRadius = (8 * resources.displayMetrics.density).toInt()
                setPadding(0, 0, 0, 0)
                minWidth = 0
                minHeight = 0
                setOnClickListener {
                    try {
                        val uri = FileProvider.getUriForFile(this@MainActivity, "$packageName.fileprovider", file)
                        ForegroundSyncService.sendMediaFileDirect(this@MainActivity, uri, file.name)
                        Toast.makeText(this@MainActivity, "Streaming ${file.name} to laptop...", Toast.LENGTH_SHORT).show()
                    } catch (e: Exception) {
                        Toast.makeText(this@MainActivity, "Send error: ${e.message}", Toast.LENGTH_SHORT).show()
                    }
                }
            }

            actionCol.addView(btnOpen)
            actionCol.addView(btnSync)

            cardLayout.addView(thumbFrame)
            cardLayout.addView(centerLayout)
            cardLayout.addView(actionCol)
            card.addView(cardLayout)

            binding.llMediaList.addView(card)
        }
    }

    private fun loadThumbnailIntoView(file: File, imageView: ImageView, reqWidth: Int, reqHeight: Int) {
        val cacheKey = "${file.absolutePath}_${file.lastModified()}_${reqWidth}x$reqHeight"
        val cached = thumbnailCache.get(cacheKey)
        if (cached != null && !cached.isRecycled) {
            imageView.setImageBitmap(cached)
            return
        }

        lifecycleScope.launch(Dispatchers.IO) {
            val bitmap = try {
                if (file.extension.equals("mp4", ignoreCase = true)) {
                    val retriever = MediaMetadataRetriever()
                    retriever.setDataSource(file.absolutePath)
                    val frame = retriever.getFrameAtTime(1000000, MediaMetadataRetriever.OPTION_CLOSEST_SYNC)
                    retriever.release()
                    frame
                } else {
                    val options = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                    BitmapFactory.decodeFile(file.absolutePath, options)
                    var inSampleSize = 1
                    if (options.outHeight > reqHeight || options.outWidth > reqWidth) {
                        val halfHeight = options.outHeight / 2
                        val halfWidth = options.outWidth / 2
                        while ((halfHeight / inSampleSize) >= reqHeight && (halfWidth / inSampleSize) >= reqWidth) {
                            inSampleSize *= 2
                        }
                    }
                    options.inJustDecodeBounds = false
                    options.inSampleSize = inSampleSize
                    BitmapFactory.decodeFile(file.absolutePath, options)
                }
            } catch (_: Exception) {
                null
            }

            if (bitmap != null) {
                thumbnailCache.put(cacheKey, bitmap)
                withContext(Dispatchers.Main) {
                    imageView.setImageBitmap(bitmap)
                }
            }
        }
    }

    private fun openMediaFile(file: File) {
        try {
            val uri = FileProvider.getUriForFile(this, "$packageName.fileprovider", file)
            val mime = if (file.extension.equals("mp4", ignoreCase = true)) "video/mp4" else "image/*"
            val intent = Intent(Intent.ACTION_VIEW).apply {
                setDataAndType(uri, mime)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }
            startActivity(intent)
        } catch (e: Exception) {
            Toast.makeText(this, "Could not open file: ${e.message}", Toast.LENGTH_SHORT).show()
        }
    }
}
