package com.linksync.app.overlay

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Color
import android.graphics.PixelFormat
import android.os.Build
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.WindowManager
import android.widget.HorizontalScrollView
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import com.linksync.app.R

@SuppressLint("ViewConstructor")
class FloatingControlView(
    context: Context,
    private val windowManager: WindowManager,
    private val drawOverlayView: DrawOverlayView,
    private val onStopRecordingClicked: () -> Unit
) : LinearLayout(context) {

    private val layoutParams: WindowManager.LayoutParams
    private var initialX = 0
    private var initialY = 0
    private var initialTouchX = 0f
    private var initialTouchY = 0f

    private var isToolboxExpanded = false
    private val toolContainer: LinearLayout

    init {
        orientation = HORIZONTAL
        gravity = Gravity.CENTER_VERTICAL
        setPadding(16, 12, 16, 12)
        setBackgroundColor(Color.parseColor("#E61E293B")) // Slate 800 with 90% alpha

        // Create window params
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
            x = 50
            y = 200
        }

        // 1. Draggable Handle / Status Indicator
        val dragHandle = TextView(context).apply {
            text = "⏺ REC"
            setTextColor(Color.parseColor("#EF4444"))
            textSize = 12f
            setPadding(8, 6, 12, 6)
        }
        addView(dragHandle)

        // 2. Brush Toggle Button
        val btnBrush = TextView(context).apply {
            text = "✏️ Draw"
            setTextColor(Color.WHITE)
            textSize = 13f
            setPadding(12, 6, 12, 6)
            setOnClickListener {
                toggleDrawingTools()
            }
        }
        addView(btnBrush)

        // 3. Sub-tools Toolbar (Pen, Arrow, Box, Badge, Undo, Clear)
        toolContainer = LinearLayout(context).apply {
            orientation = HORIZONTAL
            visibility = GONE
            gravity = Gravity.CENTER_VERTICAL

            // Pen
            addView(createToolButton("🖊") {
                drawOverlayView.currentMode = DrawMode.PEN
                drawOverlayView.setDrawingActive(true)
            })

            // Arrow
            addView(createToolButton("↗") {
                drawOverlayView.currentMode = DrawMode.ARROW
                drawOverlayView.setDrawingActive(true)
            })

            // Rect
            addView(createToolButton("▭") {
                drawOverlayView.currentMode = DrawMode.RECTANGLE
                drawOverlayView.setDrawingActive(true)
            })

            // Step Badge
            addView(createToolButton("①") {
                drawOverlayView.currentMode = DrawMode.STEP_BADGE
                drawOverlayView.setDrawingActive(true)
            })

            // Undo
            addView(createToolButton("↩") {
                drawOverlayView.undo()
            })

            // Clear
            addView(createToolButton("🗑") {
                drawOverlayView.clearAll()
            })
        }
        addView(toolContainer)

        // 4. Stop Recording Button
        val btnStop = TextView(context).apply {
            text = "⏹ Stop"
            setTextColor(Color.parseColor("#F87171"))
            textSize = 13f
            setPadding(12, 6, 8, 6)
            setOnClickListener {
                onStopRecordingClicked()
            }
        }
        addView(btnStop)

        // Drag listener on the capsule
        setOnTouchListener { _, event ->
            handleTouchDrag(event)
        }
        dragHandle.setOnTouchListener { _, event ->
            handleTouchDrag(event)
        }
    }

    private fun createToolButton(label: String, onClick: () -> Unit): TextView {
        return TextView(context).apply {
            text = label
            textSize = 16f
            setTextColor(Color.parseColor("#38BDF8"))
            setPadding(10, 4, 10, 4)
            setOnClickListener { onClick() }
        }
    }

    private fun toggleDrawingTools() {
        isToolboxExpanded = !isToolboxExpanded
        if (isToolboxExpanded) {
            toolContainer.visibility = VISIBLE
            drawOverlayView.setDrawingActive(true)
        } else {
            toolContainer.visibility = GONE
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
                return false
            }
            MotionEvent.ACTION_MOVE -> {
                val dx = (event.rawX - initialTouchX).toInt()
                val dy = (event.rawY - initialTouchY).toInt()
                if (Math.abs(dx) > 5 || Math.abs(dy) > 5) {
                    layoutParams.x = initialX + dx
                    layoutParams.y = initialY + dy
                    windowManager.updateViewLayout(this, layoutParams)
                    return true
                }
            }
        }
        return false
    }

    fun attach() {
        windowManager.addView(this, layoutParams)
    }

    fun detach() {
        try {
            windowManager.removeView(this)
        } catch (_: Exception) {}
    }
}
