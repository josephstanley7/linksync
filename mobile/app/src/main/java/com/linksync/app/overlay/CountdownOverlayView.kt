package com.linksync.app.overlay

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Color
import android.graphics.PixelFormat
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.view.Gravity
import android.view.WindowManager
import android.widget.FrameLayout
import android.widget.TextView

/**
 * XRecorder-style 3-2-1 Animated Countdown Overlay.
 *
 * Appears centered on screen prior to video capture starting.
 * Has FLAG_NOT_TOUCHABLE so it never blocks user interactions.
 */
@SuppressLint("ViewConstructor")
class CountdownOverlayView(
    context: Context,
    private val windowManager: WindowManager,
    private val onCountdownFinished: () -> Unit
) : FrameLayout(context) {

    private val countdownText: TextView
    private val handler = Handler(Looper.getMainLooper())
    private var currentCount = 3
    private var isAttached = false

    private val layoutParams: WindowManager.LayoutParams

    init {
        val windowType = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
        } else {
            @Suppress("DEPRECATION")
            WindowManager.LayoutParams.TYPE_PHONE
        }

        layoutParams = WindowManager.LayoutParams(
            WindowManager.LayoutParams.MATCH_PARENT,
            WindowManager.LayoutParams.MATCH_PARENT,
            windowType,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
                WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE or
                WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
            PixelFormat.TRANSLUCENT
        )

        // Centered frosted circle badge
        val badge = FrameLayout(context).apply {
            val bg = GradientDrawable().apply {
                shape = GradientDrawable.OVAL
                setColor(Color.parseColor("#CC0F172A")) // Slate 900 80% opacity
                setStroke(6, Color.parseColor("#38BDF8")) // Sky blue neon accent border
            }
            background = bg

            val size = (120 * resources.displayMetrics.density).toInt()
            layoutParams = LayoutParams(size, size).apply {
                gravity = Gravity.CENTER
            }
        }

        countdownText = TextView(context).apply {
            text = "3"
            textSize = 54f
            setTextColor(Color.WHITE)
            gravity = Gravity.CENTER
            typeface = android.graphics.Typeface.DEFAULT_BOLD
            layoutParams = LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT)
        }

        badge.addView(countdownText)
        addView(badge)
    }

    fun start() {
        if (isAttached) return
        isAttached = true
        try {
            windowManager.addView(this, layoutParams)
        } catch (_: Exception) {
            onCountdownFinished()
            return
        }

        tick()
    }

    private fun tick() {
        if (currentCount > 0) {
            countdownText.text = currentCount.toString()
            countdownText.scaleX = 1.3f
            countdownText.scaleY = 1.3f
            countdownText.animate().scaleX(1.0f).scaleY(1.0f).setDuration(250).start()

            currentCount--
            handler.postDelayed({ tick() }, 1000)
        } else {
            dismiss()
            onCountdownFinished()
        }
    }

    private fun dismiss() {
        if (!isAttached) return
        isAttached = false
        handler.removeCallbacksAndMessages(null)
        try {
            windowManager.removeView(this)
        } catch (_: Exception) {}
    }
}
