package com.linksync.app

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Bundle
import android.util.Log
import android.view.Gravity
import android.view.ViewGroup
import android.widget.CheckBox
import android.widget.LinearLayout
import android.widget.RadioButton
import android.widget.RadioGroup
import android.widget.TextView
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import com.linksync.app.service.ScreenRecorderService

/**
 * Trampoline launched from the Quick Settings tile or MainActivity.
 *
 * Prompts user for Audio choice (Microphone vs Muted) with a "Remember my choice" option,
 * then launches the native Android 14+ MediaProjection intent allowing the user to choose
 * between "Entire screen" and "A single app".
 */
class ScreenCapturePromptActivity : AppCompatActivity() {

    private var hasLaunched = false
    private var recordAudioChoice = true
    private var activeDialog: AlertDialog? = null

    private val captureLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        Log.i("CapturePrompt", "MediaProjection prompt result: code=${result.resultCode}")
        if (result.resultCode == Activity.RESULT_OK && result.data != null) {
            val serviceIntent = Intent(this, ScreenRecorderService::class.java).apply {
                action = ScreenRecorderService.ACTION_START
                putExtra(ScreenRecorderService.EXTRA_RESULT_CODE, result.resultCode)
                putExtra(ScreenRecorderService.EXTRA_DATA, result.data)
                putExtra(ScreenRecorderService.EXTRA_RECORD_AUDIO, recordAudioChoice)
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                startForegroundService(serviceIntent)
            } else {
                startService(serviceIntent)
            }
        } else {
            // Cancelled or denied by user
            ScreenRecorderService.publishRecordingState(this, false)
        }
        dismissQuietly()
    }

    private val audioPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        Log.i("CapturePrompt", "RECORD_AUDIO permission result: granted=$granted")
        if (!granted) {
            // User denied mic permission, gracefully fallback to muted recording
            recordAudioChoice = false
        }
        launchScreenCaptureIntent()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        overridePendingTransition(0, 0)
        window.setBackgroundDrawableResource(android.R.color.transparent)

        if (savedInstanceState != null) {
            hasLaunched = savedInstanceState.getBoolean(KEY_HAS_LAUNCHED, false)
        }

        if (!hasLaunched) {
            hasLaunched = true
            evaluateAudioAndLaunch()
        }
    }

    private fun evaluateAudioAndLaunch() {
        val prefs = getSharedPreferences("linksync_prefs", Context.MODE_PRIVATE)
        val rememberChoice = prefs.getBoolean("pref_remember_audio_choice", false)
        val savedRecordAudio = prefs.getBoolean("pref_record_audio", true)
        val alwaysAsk = prefs.getBoolean("pref_always_ask_audio", false)

        if (rememberChoice && !alwaysAsk) {
            recordAudioChoice = savedRecordAudio
            proceedWithSelectedAudio(recordAudioChoice)
        } else {
            showAudioChoiceDialog(savedRecordAudio)
        }
    }

    private fun showAudioChoiceDialog(defaultAudio: Boolean) {
        val density = resources.displayMetrics.density
        val padding = (20 * density).toInt()

        val rootLayout = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(padding, padding, padding, padding)
            background = GradientDrawable().apply {
                shape = GradientDrawable.RECTANGLE
                cornerRadius = 16 * density
                setColor(Color.parseColor("#0F172A")) // Slate 900
                setStroke((1.5f * density).toInt(), Color.parseColor("#334155")) // Slate 700
            }
        }

        // Title
        val titleTv = TextView(this).apply {
            text = "🎙️ Screen Recording Audio"
            textSize = 18f
            setTextColor(Color.parseColor("#F8FAFC"))
            typeface = android.graphics.Typeface.DEFAULT_BOLD
            setPadding(0, 0, 0, (6 * density).toInt())
        }
        rootLayout.addView(titleTv)

        // Subtitle
        val subTv = TextView(this).apply {
            text = "Choose audio preference for this recording:"
            textSize = 13f
            setTextColor(Color.parseColor("#94A3B8"))
            setPadding(0, 0, 0, (14 * density).toInt())
        }
        rootLayout.addView(subTv)

        // Radio Group
        val radioGroup = RadioGroup(this).apply {
            orientation = RadioGroup.VERTICAL
        }

        val rbWithAudio = RadioButton(this).apply {
            text = "🎙️ Record with Audio\n   (Microphone & voiceover narration)"
            textSize = 14f
            setTextColor(Color.parseColor("#F8FAFC"))
            isChecked = defaultAudio
            setPadding((8 * density).toInt(), (6 * density).toInt(), 0, (6 * density).toInt())
        }
        radioGroup.addView(rbWithAudio)

        val rbMuted = RadioButton(this).apply {
            text = "🔇 Record without Sound\n   (Silent video only)"
            textSize = 14f
            setTextColor(Color.parseColor("#F8FAFC"))
            isChecked = !defaultAudio
            setPadding((8 * density).toInt(), (6 * density).toInt(), 0, (6 * density).toInt())
        }
        radioGroup.addView(rbMuted)

        rootLayout.addView(radioGroup)

        // Checkbox: Remember my choice
        val cbRemember = CheckBox(this).apply {
            text = "Remember my choice (can change in settings)"
            textSize = 12f
            setTextColor(Color.parseColor("#94A3B8"))
            setPadding((6 * density).toInt(), (10 * density).toInt(), 0, (14 * density).toInt())
        }
        rootLayout.addView(cbRemember)

        // Buttons Row
        val buttonRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.END
            layoutParams = LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT
            ).apply {
                topMargin = (12 * density).toInt()
            }
        }

        val btnCancel = TextView(this).apply {
            text = "Cancel"
            textSize = 14f
            setTextColor(Color.parseColor("#94A3B8"))
            setPadding((16 * density).toInt(), (10 * density).toInt(), (16 * density).toInt(), (10 * density).toInt())
            setOnClickListener {
                activeDialog?.dismiss()
                dismissQuietly()
            }
        }
        buttonRow.addView(btnCancel)

        val btnStart = TextView(this).apply {
            text = "Continue →"
            textSize = 14f
            typeface = android.graphics.Typeface.DEFAULT_BOLD
            setTextColor(Color.WHITE)
            background = GradientDrawable().apply {
                shape = GradientDrawable.RECTANGLE
                cornerRadius = 10 * density
                setColor(Color.parseColor("#2563EB")) // Primary Blue
            }
            setPadding((18 * density).toInt(), (10 * density).toInt(), (18 * density).toInt(), (10 * density).toInt())
            setOnClickListener {
                val chosenAudio = rbWithAudio.isChecked
                recordAudioChoice = chosenAudio

                if (cbRemember.isChecked) {
                    val prefs = getSharedPreferences("linksync_prefs", Context.MODE_PRIVATE)
                    prefs.edit()
                        .putBoolean("pref_record_audio", chosenAudio)
                        .putBoolean("pref_remember_audio_choice", true)
                        .apply()
                }

                activeDialog?.dismiss()
                proceedWithSelectedAudio(chosenAudio)
            }
        }
        buttonRow.addView(btnStart)
        rootLayout.addView(buttonRow)

        activeDialog = AlertDialog.Builder(this)
            .setView(rootLayout)
            .setCancelable(true)
            .setOnCancelListener { dismissQuietly() }
            .create().apply {
                window?.setBackgroundDrawableResource(android.R.color.transparent)
                show()
            }
    }

    private fun proceedWithSelectedAudio(withAudio: Boolean) {
        if (withAudio) {
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
                audioPermissionLauncher.launch(Manifest.permission.RECORD_AUDIO)
            } else {
                launchScreenCaptureIntent()
            }
        } else {
            launchScreenCaptureIntent()
        }
    }

    private fun launchScreenCaptureIntent() {
        try {
            val projectionManager = getSystemService(Context.MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
            // Pure native Android 14+ screen capture intent without createConfigForDefaultDisplay()
            // allowing user to choose between 'Entire screen' AND 'A single app'!
            val captureIntent = projectionManager.createScreenCaptureIntent()
            captureLauncher.launch(captureIntent)
        } catch (e: Exception) {
            Log.e("CapturePrompt", "Failed to launch screen capture intent", e)
            dismissQuietly()
        }
    }

    override fun onSaveInstanceState(outState: Bundle) {
        super.onSaveInstanceState(outState)
        outState.putBoolean(KEY_HAS_LAUNCHED, hasLaunched)
    }

    override fun finish() {
        super.finish()
        overridePendingTransition(0, 0)
    }

    private fun dismissQuietly() {
        try { activeDialog?.dismiss() } catch (_: Exception) {}
        activeDialog = null
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP) {
            finishAndRemoveTask()
        } else {
            finish()
        }
        overridePendingTransition(0, 0)
    }

    companion object {
        private const val KEY_HAS_LAUNCHED = "linksync_capture_prompt_launched"
    }
}
