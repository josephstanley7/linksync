package com.linksync.app.observer

import android.content.ContentUris
import android.content.Context
import android.database.ContentObserver
import android.net.Uri
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.provider.MediaStore
import android.util.Log

class ScreenshotObserver(
    private val context: Context,
    private val onMediaDetected: (Uri, String) -> Unit
) : ContentObserver(Handler(Looper.getMainLooper())) {

    private var lastObservedId: Long = -1L

    init {
        // Initialize with current latest image ID so existing photos are not re-sent on launch
        try {
            val projection = arrayOf(MediaStore.Images.Media._ID)
            context.contentResolver.query(
                MediaStore.Images.Media.EXTERNAL_CONTENT_URI,
                projection,
                null,
                null,
                "${MediaStore.Images.Media._ID} DESC"
            )?.use { cursor ->
                if (cursor.moveToFirst()) {
                    val idIdx = cursor.getColumnIndex(MediaStore.Images.Media._ID)
                    if (idIdx != -1) {
                        lastObservedId = cursor.getLong(idIdx)
                        Log.i("ScreenshotObserver", "Initialized baseline screenshot ID: $lastObservedId")
                    }
                }
            }
        } catch (e: Exception) {
            Log.w("ScreenshotObserver", "Could not initialize baseline ID", e)
        }
    }

    override fun onChange(selfChange: Boolean, uri: Uri?) {
        super.onChange(selfChange, uri)
        checkForNewScreenshot()
    }

    fun checkForNewScreenshot() {
        try {
            val projection = mutableListOf(
                MediaStore.Images.Media._ID,
                MediaStore.Images.Media.DISPLAY_NAME,
                MediaStore.Images.Media.DATE_ADDED
            )
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                projection.add(MediaStore.Images.Media.RELATIVE_PATH)
                projection.add(MediaStore.Images.Media.IS_PENDING)
            }

            val targetUri = MediaStore.Images.Media.EXTERNAL_CONTENT_URI

            context.contentResolver.query(
                targetUri,
                projection.toTypedArray(),
                null,
                null,
                "${MediaStore.Images.Media._ID} DESC"
            )?.use { cursor ->
                if (cursor.moveToFirst()) {
                    val idIdx = cursor.getColumnIndex(MediaStore.Images.Media._ID)
                    val nameIdx = cursor.getColumnIndex(MediaStore.Images.Media.DISPLAY_NAME)

                    val id = if (idIdx != -1) cursor.getLong(idIdx) else -1L
                    val fileName = if (nameIdx != -1) cursor.getString(nameIdx) ?: "" else ""

                    // Check IS_PENDING on Android 10+ (wait until file write is finalized)
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                        val pendingIdx = cursor.getColumnIndex(MediaStore.Images.Media.IS_PENDING)
                        if (pendingIdx != -1 && cursor.getInt(pendingIdx) == 1) {
                            return
                        }
                    }

                    if (id != -1L && (lastObservedId == -1L || id > lastObservedId)) {
                        var isScreenshot = false

                        val lower = fileName.lowercase()
                        if (lower.contains("screenshot") || lower.contains("screen_shot") ||
                            lower.contains("screencapture") || lower.contains("quickmemo")) {
                            isScreenshot = true
                        }

                        if (!isScreenshot && Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                            val pathIdx = cursor.getColumnIndex(MediaStore.Images.Media.RELATIVE_PATH)
                            val relPath = if (pathIdx != -1) cursor.getString(pathIdx)?.lowercase() ?: "" else ""
                            if (relPath.contains("screenshot")) {
                                isScreenshot = true
                            }
                        }

                        if (isScreenshot) {
                            lastObservedId = id
                            val itemUri = ContentUris.withAppendedId(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, id)
                            Log.i("ScreenshotObserver", "Instant screenshot detected: $fileName (ID: $id, Uri: $itemUri)")
                            onMediaDetected(itemUri, fileName)
                        }
                    }
                }
            }
        } catch (e: Exception) {
            Log.e("ScreenshotObserver", "Error querying MediaStore images", e)
        }
    }
}
