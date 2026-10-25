package com.linksync.app

import android.app.Activity
import android.content.Intent
import android.media.MediaCodec
import android.media.MediaExtractor
import android.media.MediaFormat
import android.media.MediaMetadataRetriever
import android.media.MediaMuxer
import android.media.MediaScannerConnection
import android.net.Uri
import android.os.Bundle
import android.os.Environment
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.view.View
import android.widget.SeekBar
import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.linksync.app.databinding.ActivityVideoPreviewBinding
import com.linksync.app.service.ForegroundSyncService
import java.io.File
import java.nio.ByteBuffer
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * XRecorder-grade post-recording preview & lossless trimming gate.
 *
 * Allows users to inspect playback, trim start and end points, and choose
 * whether to Save & Sync to PC, Save to Phone Only, or Discard.
 */
class VideoPreviewActivity : AppCompatActivity() {

    private lateinit var binding: ActivityVideoPreviewBinding
    private var videoPath: String = ""
    private var durationMs: Int = 0
    private var trimStartMs: Int = 0
    private var trimEndMs: Int = 0

    private val handler = Handler(Looper.getMainLooper())
    private var isPlaying = false

    private val progressUpdater = object : Runnable {
        override fun run() {
            if (binding.videoView.isPlaying) {
                val current = binding.videoView.currentPosition
                binding.tvCurrentPos.text = formatTime(current)
                if (durationMs > 0) {
                    binding.seekBarVideo.progress = (current * 1000L / durationMs).toInt()
                }

                // Loop if we exceeded trimEndMs
                if (trimEndMs > trimStartMs && current >= trimEndMs) {
                    binding.videoView.seekTo(trimStartMs)
                }
            }
            handler.postDelayed(this, 100)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityVideoPreviewBinding.inflate(layoutInflater)
        setContentView(binding.root)

        videoPath = intent.getStringExtra(EXTRA_VIDEO_PATH) ?: ""
        if (videoPath.isEmpty() || !File(videoPath).exists()) {
            Toast.makeText(this, "Recording file not found", Toast.LENGTH_SHORT).show()
            finish()
            return
        }

        val videoFile = File(videoPath)
        val sizeMb = videoFile.length() / (1024.0 * 1024.0)
        binding.tvVideoMetaHeader.text = String.format(Locale.US, "%.1f MB • %s", sizeMb, videoFile.name)

        setupVideoPlayer()
        setupTrimmingControls()
        setupActionButtons()
    }

    private fun setupVideoPlayer() {
        val uri = Uri.fromFile(File(videoPath))
        binding.videoView.setVideoURI(uri)

        binding.videoView.setOnPreparedListener { mp ->
            durationMs = mp.duration
            trimStartMs = 0
            trimEndMs = durationMs

            binding.tvTotalDuration.text = formatTime(durationMs)
            binding.tvDurationHeader.text = formatTime(durationMs)
            updateTrimReadout()

            // Start playing immediately
            binding.videoView.start()
            isPlaying = true
            binding.ivPlayPauseOverlay.visibility = View.GONE
            handler.post(progressUpdater)
        }

        binding.videoView.setOnCompletionListener {
            binding.videoView.seekTo(trimStartMs)
            binding.videoView.start()
        }

        binding.layoutVideoContainer.setOnClickListener {
            togglePlayPause()
        }

        binding.seekBarVideo.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(seekBar: SeekBar?, progress: Int, fromUser: Boolean) {
                if (fromUser && durationMs > 0) {
                    val targetMs = (progress * durationMs.toLong() / 1000L).toInt()
                    binding.videoView.seekTo(targetMs)
                    binding.tvCurrentPos.text = formatTime(targetMs)
                }
            }

            override fun onStartTrackingTouch(seekBar: SeekBar?) {}
            override fun onStopTrackingTouch(seekBar: SeekBar?) {}
        })
    }

    private fun togglePlayPause() {
        if (binding.videoView.isPlaying) {
            binding.videoView.pause()
            isPlaying = false
            binding.ivPlayPauseOverlay.setImageResource(android.R.drawable.ic_media_play)
            binding.ivPlayPauseOverlay.visibility = View.VISIBLE
        } else {
            binding.videoView.start()
            isPlaying = true
            binding.ivPlayPauseOverlay.visibility = View.GONE
        }
    }

    private fun setupTrimmingControls() {
        binding.btnSetStart.setOnClickListener {
            val current = binding.videoView.currentPosition
            if (current < trimEndMs) {
                trimStartMs = current
                updateTrimReadout()
                Toast.makeText(this, "Start point set to ${formatTimePrecise(trimStartMs)}", Toast.LENGTH_SHORT).show()
            } else {
                Toast.makeText(this, "Start point cannot be after End point", Toast.LENGTH_SHORT).show()
            }
        }

        binding.btnSetEnd.setOnClickListener {
            val current = binding.videoView.currentPosition
            if (current > trimStartMs) {
                trimEndMs = current
                updateTrimReadout()
                Toast.makeText(this, "End point set to ${formatTimePrecise(trimEndMs)}", Toast.LENGTH_SHORT).show()
            } else {
                Toast.makeText(this, "End point cannot be before Start point", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun updateTrimReadout() {
        binding.tvTrimStart.text = formatTimePrecise(trimStartMs)
        binding.tvTrimEnd.text = formatTimePrecise(trimEndMs)
        val selectedDurationSec = (trimEndMs - trimStartMs) / 1000.0
        binding.tvTrimDuration.text = String.format(Locale.US, "Selected Duration: %.2fs", selectedDurationSec)
    }

    private fun setupActionButtons() {
        binding.btnBackHeader.setOnClickListener {
            finish()
        }

        binding.btnSaveAndSync.setOnClickListener {
            saveAndFinalize(syncToPc = true)
        }

        binding.btnSaveLocal.setOnClickListener {
            saveAndFinalize(syncToPc = false)
        }

        binding.btnDiscard.setOnClickListener {
            MaterialAlertDialogBuilder(this)
                .setTitle("Discard Recording?")
                .setMessage("Are you sure you want to permanently delete this screen recording?")
                .setPositiveButton("Discard") { _, _ ->
                    discardRecording()
                }
                .setNegativeButton("Cancel", null)
                .show()
        }
    }

    private fun saveAndFinalize(syncToPc: Boolean) {
        binding.btnSaveAndSync.isEnabled = false
        binding.btnSaveLocal.isEnabled = false
        binding.btnDiscard.isEnabled = false

        val sourceFile = File(videoPath)
        val needsTrim = (trimStartMs > 500 || (durationMs - trimEndMs) > 500)

        Thread {
            val finalFile: File
            if (needsTrim) {
                val dir = File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_MOVIES), "LinkSync")
                if (!dir.exists()) dir.mkdirs()
                val timestamp = SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US).format(Date())
                val trimmedFile = File(dir, "LinkSync_REC_${timestamp}_trimmed.mp4")

                val success = losslessTrim(sourceFile, trimmedFile, trimStartMs * 1000L, trimEndMs * 1000L)
                finalFile = if (success && trimmedFile.exists() && trimmedFile.length() > 1024) {
                    try { sourceFile.delete() } catch (_: Exception) {}
                    trimmedFile
                } else {
                    sourceFile
                }
            } else {
                finalFile = sourceFile
            }

            runOnUiThread {
                // Register in MediaStore so Samsung Gallery sees it immediately
                MediaScannerConnection.scanFile(
                    applicationContext,
                    arrayOf(finalFile.absolutePath),
                    arrayOf("video/mp4")
                ) { path, uri ->
                    Log.i("VideoPreview", "Indexed recorded video: $path -> $uri")
                    if (syncToPc && uri != null) {
                        ForegroundSyncService.sendMediaFileDirect(applicationContext, uri, finalFile.name)
                    }
                }

                if (syncToPc) {
                    Toast.makeText(this, "✓ Video Saved & Syncing to Laptop!", Toast.LENGTH_LONG).show()
                } else {
                    Toast.makeText(this, "✓ Video Saved to Phone Gallery!", Toast.LENGTH_SHORT).show()
                }

                finish()
            }
        }.start()
    }

    private fun discardRecording() {
        try {
            val file = File(videoPath)
            if (file.exists()) {
                file.delete()
            }
        } catch (e: Exception) {
            Log.w("VideoPreview", "Error deleting discarded recording: ${e.message}")
        }
        Toast.makeText(this, "Recording discarded", Toast.LENGTH_SHORT).show()
        finish()
    }

    /**
     * Lossless MP4 trim using Android native MediaExtractor and MediaMuxer.
     * Keeps 100% original AVC video & AAC audio bitrate with 0ms re-encoding lag.
     */
    private fun losslessTrim(source: File, dest: File, startUs: Long, endUs: Long): Boolean {
        var extractor: MediaExtractor? = null
        var muxer: MediaMuxer? = null
        try {
            extractor = MediaExtractor().apply { setDataSource(source.absolutePath) }
            muxer = MediaMuxer(dest.absolutePath, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4)

            val trackCount = extractor.trackCount
            val trackIndexMap = HashMap<Int, Int>()
            var maxBufferSize = 2 * 1024 * 1024

            for (i in 0 until trackCount) {
                val format = extractor.getTrackFormat(i)
                val mime = format.getString(MediaFormat.KEY_MIME) ?: ""
                if (mime.startsWith("video/") || mime.startsWith("audio/")) {
                    val muxerTrackIndex = muxer.addTrack(format)
                    trackIndexMap[i] = muxerTrackIndex
                    if (format.containsKey(MediaFormat.KEY_MAX_INPUT_SIZE)) {
                        val size = format.getInteger(MediaFormat.KEY_MAX_INPUT_SIZE)
                        if (size > maxBufferSize) maxBufferSize = size
                    }
                }
            }

            muxer.start()

            val buffer = ByteBuffer.allocate(maxBufferSize)
            val bufferInfo = MediaCodec.BufferInfo()

            for ((extractorTrackIndex, muxerTrackIndex) in trackIndexMap) {
                extractor.selectTrack(extractorTrackIndex)
                extractor.seekTo(startUs, MediaExtractor.SEEK_TO_PREVIOUS_SYNC)

                while (true) {
                    bufferInfo.offset = 0
                    bufferInfo.size = extractor.readSampleData(buffer, 0)
                    if (bufferInfo.size < 0) break

                    bufferInfo.presentationTimeUs = extractor.sampleTime
                    if (bufferInfo.presentationTimeUs > endUs) break

                    if (bufferInfo.presentationTimeUs >= startUs) {
                        bufferInfo.flags = extractor.sampleFlags
                        muxer.writeSampleData(muxerTrackIndex, buffer, bufferInfo)
                    }

                    if (!extractor.advance()) break
                }

                extractor.unselectTrack(extractorTrackIndex)
            }

            muxer.stop()
            return true
        } catch (e: Exception) {
            Log.e("VideoPreview", "Lossless trim error: ${e.message}", e)
            return false
        } finally {
            try { extractor?.release() } catch (_: Exception) {}
            try { muxer?.release() } catch (_: Exception) {}
        }
    }

    private fun formatTime(millis: Int): String {
        val totalSec = millis / 1000
        val min = totalSec / 60
        val sec = totalSec % 60
        return String.format(Locale.US, "%02d:%02d", min, sec)
    }

    private fun formatTimePrecise(millis: Int): String {
        val totalSec = millis / 1000
        val min = totalSec / 60
        val sec = totalSec % 60
        val tenths = (millis % 1000) / 100
        return String.format(Locale.US, "%02d:%02d.%d", min, sec, tenths)
    }

    override fun onDestroy() {
        handler.removeCallbacks(progressUpdater)
        try {
            binding.videoView.stopPlayback()
        } catch (_: Exception) {}
        super.onDestroy()
    }

    companion object {
        const val EXTRA_VIDEO_PATH = "extra_video_path"
    }
}
