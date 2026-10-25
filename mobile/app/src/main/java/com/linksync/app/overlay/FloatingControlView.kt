package com.linksync.app.overlay

import android.animation.ValueAnimator
import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Color
import android.graphics.PixelFormat
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.WindowManager
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast

/**
 * XRecorder-grade Floating Bubble Controller.
 *
 * Features:
 * - Live formatted recording timer (MM:SS)
 * - Audio indicator (🎙️ Mic Active / 🔇 Muted)
 * - Pause / Resume controls (Android 7+)
 * - Drag-to-edge magnetic snapping
 * - Drawing toolkit with "👆 Touch" (Interact) tool to navigate apps while drawings remain visible
 * - 1-tap Minimize / Hide (👁️) to keep recordings completely clean
 * - Tactile OLED styling adhering to anti-ai-fingerprint standards
 */
@SuppressLint("ViewConstructor")
class FloatingControlView(
    context: Context,
    private val windowManager: WindowManager,
    private val drawOverlayView: DrawOverlayView,
    private val recordAudio: Boolean = true,
    private val onPauseResumeClicked: (isPaused: Boolean) -> Unit,
    private val onStopRecordingClicked: () -> Unit
) : LinearLayout(context) {

    private val layoutParams: WindowManager.LayoutParams
    private var initialX = 0
    private var initialY = 0
    private var initialTouchX = 0f
    private var initialTouchY = 0f
    private var isDragging = false

    private var isToolboxExpanded = false
    private val toolContainer: LinearLayout
    private val timerText: TextView
    private val btnPauseResume: TextView
    private val btnBrush: TextView

    private val handler = Handler(Looper.getMainLooper())
    private var startTime = 0L
    private var elapsedBeforePause = 0L
    private var isPaused = false

    private val timerRunnable = object : Runnable {
        override fun run() {
            if (!isPaused && startTime > 0) {
                val totalElapsed = (SystemClock.elapsedRealtime() - startTime) + elapsedBeforePause
                val seconds = (totalElapsed / 1000) % 60
                val minutes = (totalElapsed / (1000 * 60))
                timerText.text = String.format("%02d:%02d", minutes, seconds)
                handler.postDelayed(this, 1000)
            }
        }
    }

    init {
        orientation = HORIZONTAL
        gravity = Gravity.CENTER_VERTICAL
        val density = resources.displayMetrics.density
        setPadding((12 * density).toInt(), (8 * density).toInt(), (12 * density).toInt(), (8 * density).toInt())

        // Tactile OLED slate pill background with subtle cyan border
        background = GradientDrawable().apply {
            shape = GradientDrawable.RECTANGLE
            cornerRadius = 24 * density
            setColor(Color.parseColor("#E60F172A")) // Slate 900 90% opacity
            setStroke((1.5f * density).toInt(), Color.parseColor("#3338BDF8"))
        }

        val windowType = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
        } else {
            @Suppress("DEPRECATION")
            WindowManager.LayoutParams.TYPE_PHONE
        }

        layoutParams = WindowManager.LayoutParams(
            WindowManager.LayoutParams.WRAP_CONTENT,
            WindowManager.LayoutParams.WRAP_CONTENT,
            windowType,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
            PixelFormat.TRANSLUCENT
        ).apply {
            gravity = Gravity.TOP or Gravity.START
            x = 24
            y = (180 * density).toInt()
        }

        // 1. Live Rec Indicator & Timer
        timerText = TextView(context).apply {
            text = "⏺ 00:00"
            setTextColor(Color.parseColor("#F87171")) // Soft Red
            textSize = 13f
            typeface = android.graphics.Typeface.MONOSPACE
            setPadding((4 * density).toInt(), (4 * density).toInt(), (4 * density).toInt(), (4 * density).toInt())
        }
        addView(timerText)

        // 2. Audio State Indicator (🎙️ / 🔇)
        val audioIndicator = TextView(context).apply {
            text = if (recordAudio) "🎙️" else "🔇"
            textSize = 12f
            setPadding((2 * density).toInt(), (4 * density).toInt(), (6 * density).toInt(), (4 * density).toInt())
        }
        addView(audioIndicator)

        // 3. Pause / Resume Button
        btnPauseResume = TextView(context).apply {
            text = "⏸"
            textSize = 14f
            setTextColor(Color.parseColor("#38BDF8"))
            setPadding((6 * density).toInt(), (4 * density).toInt(), (6 * density).toInt(), (4 * density).toInt())
            setOnClickListener {
                togglePauseResume()
            }
        }
        addView(btnPauseResume)

        // 4. Brush / Palette Toggle Button
        btnBrush = TextView(context).apply {
            text = "✏️"
            textSize = 14f
            setTextColor(Color.WHITE)
            setPadding((6 * density).toInt(), (4 * density).toInt(), (6 * density).toInt(), (4 * density).toInt())
            setOnClickListener {
                toggleDrawingTools()
            }
        }
        addView(btnBrush)

        // 5. Sub-tools Toolbar (Pen, Arrow, Box, Badge, Touch Mode, Undo, Clear, Close)
        toolContainer = LinearLayout(context).apply {
            orientation = HORIZONTAL
            visibility = GONE
            gravity = Gravity.CENTER_VERTICAL

            // 👆 Touch Mode: Lets user tap and navigate apps normally while keeping all drawings on screen!
            addView(createToolButton("👆 Touch") {
                drawOverlayView.setDrawingActive(false)
            })

            // 🖊 Pen
            addView(createToolButton("🖊") {
                drawOverlayView.currentMode = DrawMode.PEN
                drawOverlayView.setDrawingActive(true)
            })

            // ↗ Arrow
            addView(createToolButton("↗") {
                drawOverlayView.currentMode = DrawMode.ARROW
                drawOverlayView.setDrawingActive(true)
            })

            // ▭ Rectangle
            addView(createToolButton("▭") {
                drawOverlayView.currentMode = DrawMode.RECTANGLE
                drawOverlayView.setDrawingActive(true)
            })

            // ① Step Badge
            addView(createToolButton("①") {
                drawOverlayView.currentMode = DrawMode.STEP_BADGE
                drawOverlayView.setDrawingActive(true)
            })

            // ↩ Undo
            addView(createToolButton("↩") {
                drawOverlayView.undo()
            })

            // 🗑 Clear
            addView(createToolButton("🗑") {
                drawOverlayView.clearAll()
            })
        }
        addView(toolContainer)

        // 6. Hide / Minimize Pill Button (Keeps video clean)
        val btnHide = TextView(context).apply {
            text = "👁️"
            textSize = 13f
            setTextColor(Color.parseColor("#94A3B8"))
            setPadding((6 * density).toInt(), (4 * density).toInt(), (6 * density).toInt(), (4 * density).toInt())
            setOnClickListener {
                hideBubble()
                Toast.makeText(context, "Bubble hidden. Swipe notification or shake phone to stop.", Toast.LENGTH_SHORT).show()
            }
        }
        addView(btnHide)

        // 7. Stop Recording Button
        val btnStop = TextView(context).apply {
            text = "⏹ Stop"
            setTextColor(Color.parseColor("#EF4444"))
            textSize = 13f
            typeface = android.graphics.Typeface.DEFAULT_BOLD
            setPadding((8 * density).toInt(), (4 * density).toInt(), (6 * density).toInt(), (4 * density).toInt())
            setOnClickListener {
                onStopRecordingClicked()
            }
        }
        addView(btnStop)

        // Drag listener for moving capsule
        setOnTouchListener { _, event ->
            handleTouchDrag(event)
        }
        timerText.setOnTouchListener { _, event ->
            handleTouchDrag(event)
        }
    }

    fun hideBubble() {
        visibility = GONE
    }

    fun showBubble() {
        visibility = VISIBLE
    }

    fun startTimer() {
        startTime = SystemClock.elapsedRealtime()
        elapsedBeforePause = 0L
        isPaused = false
        handler.removeCallbacks(timerRunnable)
        handler.post(timerRunnable)
    }

    fun setPausedState(paused: Boolean) {
        if (paused != isPaused) {
            togglePauseResume()
        }
    }

    private fun togglePauseResume() {
        if (!isPaused) {
            isPaused = true
            elapsedBeforePause += SystemClock.elapsedRealtime() - startTime
            startTime = 0L
            btnPauseResume.text = "▶"
            timerText.setTextColor(Color.parseColor("#FBBF24")) // Amber warning color
            handler.removeCallbacks(timerRunnable)
            onPauseResumeClicked(true)
        } else {
            isPaused = false
            startTime = SystemClock.elapsedRealtime()
            btnPauseResume.text = "⏸"
            timerText.setTextColor(Color.parseColor("#F87171")) // Red active color
            handler.post(timerRunnable)
            onPauseResumeClicked(false)
        }
    }

    private fun createToolButton(label: String, onClick: () -> Unit): TextView {
        val density = resources.displayMetrics.density
        return TextView(context).apply {
            text = label
            textSize = 14f
            setTextColor(Color.parseColor("#38BDF8"))
            setPadding((8 * density).toInt(), (4 * density).toInt(), (8 * density).toInt(), (4 * density).toInt())
            setOnClickListener { onClick() }
        }
    }

    fun toggleDrawingTools() {
        isToolboxExpanded = !isToolboxExpanded
        if (isToolboxExpanded) {
            toolContainer.visibility = VISIBLE
            btnBrush.setTextColor(Color.parseColor("#38BDF8"))
            drawOverlayView.setDrawingActive(true)
        } else {
            toolContainer.visibility = GONE
            btnBrush.setTextColor(Color.WHITE)
            drawOverlayView.setDrawingActive(false)
        }
    }

    private fun handleTouchDrag(event: MotionEvent): Boolean {
        when (event.action) {
            MotionEvent.ACTION_DOWN -> {
                initialX = layoutParams.x
                initialY = layoutParams.y
                initialTouchX = event.rawX
                initialTouchY = event.rawY
                isDragging = false
                return false
            }
            MotionEvent.ACTION_MOVE -> {
                val dx = (event.rawX - initialTouchX).toInt()
                val dy = (event.rawY - initialTouchY).toInt()
                if (Math.abs(dx) > 10 || Math.abs(dy) > 10) {
                    isDragging = true
                    layoutParams.x = initialX + dx
                    layoutParams.y = initialY + dy
                    windowManager.updateViewLayout(this, layoutParams)
                    return true
                }
            }
            MotionEvent.ACTION_UP -> {
                if (isDragging) {
                    snapToNearestEdge()
                    return true
                }
            }
        }
        return false
    }

    private fun snapToNearestEdge() {
        val screenWidth = resources.displayMetrics.widthPixels
        val viewWidth = width.coerceAtLeast(100)
        val targetX = if (layoutParams.x + viewWidth / 2 < screenWidth / 2) {
            16 // Snap to left edge
        } else {
            screenWidth - viewWidth - 16 // Snap to right edge
        }

        val startX = layoutParams.x
        val animator = ValueAnimator.ofInt(startX, targetX).apply {
            duration = 180
            addUpdateListener { anim ->
                layoutParams.x = anim.animatedValue as Int
                try {
                    windowManager.updateViewLayout(this@FloatingControlView, layoutParams)
                } catch (_: Exception) {}
            }
        }
        animator.start()
    }

    fun attach() {
        windowManager.addView(this, layoutParams)
    }

    fun detach() {
        handler.removeCallbacksAndMessages(null)
        try {
            windowManager.removeView(this)
        } catch (_: Exception) {}
    }
}
