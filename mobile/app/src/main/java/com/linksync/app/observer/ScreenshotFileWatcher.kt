package com.linksync.app.observer

import android.content.Context
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.os.FileObserver
import android.util.Log
import androidx.core.content.FileProvider
import java.io.File

class ScreenshotFileWatcher(
    private val context: Context,
    private val onScreenshotDetected: (Uri, String, File) -> Unit
) {
    private val observers = mutableListOf<FileObserver>()
    private val processedFiles = mutableSetOf<String>()

    fun start() {
        stop()

        val directories = listOf(
            File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DCIM), "Screenshots"),
            File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_PICTURES), "Screenshots"),
            File(Environment.getExternalStorageDirectory(), "DCIM/Screenshots"),
            File(Environment.getExternalStorageDirectory(), "Pictures/Screenshots")
        ).filter { it.exists() && it.isDirectory }

        for (dir in directories.distinctBy { it.absolutePath }) {
            try {
                val observer = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                    object : FileObserver(dir, CLOSE_WRITE or MOVED_TO) {
                        override fun onEvent(event: Int, path: String?) {
                            handleEvent(event, dir, path)
                        }
                    }
                } else {
                    @Suppress("DEPRECATION")
                    object : FileObserver(dir.absolutePath, CLOSE_WRITE or MOVED_TO) {
                        override fun onEvent(event: Int, path: String?) {
                            handleEvent(event, dir, path)
                        }
                    }
                }
                observer.startWatching()
                observers.add(observer)
                Log.i("ScreenshotFileWatcher", "Watching screenshot directory: ${dir.absolutePath}")
            } catch (e: Exception) {
                Log.w("ScreenshotFileWatcher", "Could not watch directory ${dir.absolutePath}: ${e.message}")
            }
        }
    }

    private fun handleEvent(event: Int, dir: File, path: String?) {
        if (path == null) return
        val lower = path.lowercase()
        if (lower.endsWith(".png") || lower.endsWith(".jpg") || lower.endsWith(".jpeg")) {
            val file = File(dir, path)
            if (file.exists() && file.length() > 0) {
                val key = "${file.name}_${file.length()}"
                synchronized(processedFiles) {
                    if (processedFiles.contains(key)) return
                    processedFiles.add(key)
                    if (processedFiles.size > 100) {
                        processedFiles.clear()
                    }
                }

                Log.i("ScreenshotFileWatcher", "Instant inotify detection: ${file.absolutePath} (${file.length()} bytes)")
                try {
                    val uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
                    onScreenshotDetected(uri, file.name, file)
                } catch (e: Exception) {
                    val fileUri = Uri.fromFile(file)
                    onScreenshotDetected(fileUri, file.name, file)
                }
            }
        }
    }

    fun stop() {
        observers.forEach {
            try { it.stopWatching() } catch (_: Exception) {}
        }
        observers.clear()
    }
}
