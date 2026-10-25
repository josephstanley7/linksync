package com.linksync.app.overlay

import android.content.Context
import android.graphics.*
import android.util.AttributeSet
import android.view.MotionEvent
import android.view.View
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.sin

enum class DrawMode {
    NONE,
    PEN,
    ARROW,
    RECTANGLE,
    STEP_BADGE
}

class DrawOverlayView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : View(context, attrs, defStyleAttr) {

    var currentMode: DrawMode = DrawMode.PEN
    var currentColor: Int = Color.parseColor("#EF4444") // Default vibrant Red
    var strokeThickness: Float = 10f

    private var isDrawingActive = false
    private var stepBadgeCounter = 1

    sealed class ShapeItem(val color: Int, val strokeWidth: Float) {
        class Freehand(color: Int, strokeWidth: Float, val path: Path) : ShapeItem(color, strokeWidth)
        class Arrow(color: Int, strokeWidth: Float, val x1: Float, val y1: Float, val x2: Float, val y2: Float) : ShapeItem(color, strokeWidth)
        class Rect(color: Int, strokeWidth: Float, val left: Float, val top: Float, val right: Float, val bottom: Float) : ShapeItem(color, strokeWidth)
        class StepBadge(color: Int, val x: Float, val y: Float, val number: Int) : ShapeItem(color, 0f)
    }

    private val shapes = mutableListOf<ShapeItem>()

    // Current in-progress shape coordinates
    private var startX = 0f
    private var startY = 0f
    private var currentX = 0f
    private var currentY = 0f
    private var activePath = Path()

    private val strokePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
    }

    private val fillPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.FILL
    }

    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.WHITE
        textSize = 32f
        textAlign = Paint.Align.CENTER
        typeface = Typeface.create(Typeface.DEFAULT, Typeface.BOLD)
    }

    fun setDrawingActive(active: Boolean) {
        isDrawingActive = active
        visibility = if (shapes.isNotEmpty() || active) VISIBLE else GONE
        invalidate()
    }

    fun undo() {
        if (shapes.isNotEmpty()) {
            val removed = shapes.removeAt(shapes.size - 1)
            if (removed is ShapeItem.StepBadge && stepBadgeCounter > 1) {
                stepBadgeCounter--
            }
            invalidate()
        }
    }

    fun clearAll() {
        shapes.clear()
        stepBadgeCounter = 1
        invalidate()
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        if (!isDrawingActive || currentMode == DrawMode.NONE) {
            return false // Let touches pass through to underlying apps
        }

        val x = event.x
        val y = event.y

        when (event.action) {
            MotionEvent.ACTION_DOWN -> {
                startX = x
                startY = y
                currentX = x
                currentY = y

                when (currentMode) {
                    DrawMode.PEN -> {
                        activePath = Path()
                        activePath.moveTo(x, y)
                    }
                    DrawMode.STEP_BADGE -> {
                        shapes.add(ShapeItem.StepBadge(currentColor, x, y, stepBadgeCounter++))
                        invalidate()
                    }
                    else -> {}
                }
                return true
            }

            MotionEvent.ACTION_MOVE -> {
                currentX = x
                currentY = y

                if (currentMode == DrawMode.PEN) {
                    activePath.quadTo(startX, startY, (x + startX) / 2, (y + startY) / 2)
                    startX = x
                    startY = y
                }
                invalidate()
                return true
            }

            MotionEvent.ACTION_UP -> {
                currentX = x
                currentY = y

                when (currentMode) {
                    DrawMode.PEN -> {
                        activePath.lineTo(currentX, currentY)
                        shapes.add(ShapeItem.Freehand(currentColor, strokeThickness, Path(activePath)))
                        activePath.reset()
                    }
                    DrawMode.ARROW -> {
                        shapes.add(ShapeItem.Arrow(currentColor, strokeThickness, startX, startY, currentX, currentY))
                    }
                    DrawMode.RECTANGLE -> {
                        val left = minOf(startX, currentX)
                        val top = minOf(startY, currentY)
                        val right = maxOf(startX, currentX)
                        val bottom = maxOf(startY, currentY)
                        shapes.add(ShapeItem.Rect(currentColor, strokeThickness, left, top, right, bottom))
                    }
                    else -> {}
                }
                invalidate()
                return true
            }
        }
        return super.onTouchEvent(event)
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)

        // 1. Draw committed shapes
        for (item in shapes) {
            when (item) {
                is ShapeItem.Freehand -> {
                    strokePaint.color = item.color
                    strokePaint.strokeWidth = item.strokeWidth
                    canvas.drawPath(item.path, strokePaint)
                }
                is ShapeItem.Arrow -> {
                    drawArrow(canvas, item.x1, item.y1, item.x2, item.y2, item.color, item.strokeWidth)
                }
                is ShapeItem.Rect -> {
                    strokePaint.color = item.color
                    strokePaint.strokeWidth = item.strokeWidth
                    canvas.drawRect(item.left, item.top, item.right, item.bottom, strokePaint)
                }
                is ShapeItem.StepBadge -> {
                    drawStepBadge(canvas, item.x, item.y, item.number, item.color)
                }
            }
        }

        // 2. Draw live preview of current dragging shape
        if (isDrawingActive) {
            when (currentMode) {
                DrawMode.PEN -> {
                    strokePaint.color = currentColor
                    strokePaint.strokeWidth = strokeThickness
                    canvas.drawPath(activePath, strokePaint)
                }
                DrawMode.ARROW -> {
                    drawArrow(canvas, startX, startY, currentX, currentY, currentColor, strokeThickness)
                }
                DrawMode.RECTANGLE -> {
                    strokePaint.color = currentColor
                    strokePaint.strokeWidth = strokeThickness
                    val left = minOf(startX, currentX)
                    val top = minOf(startY, currentY)
                    val right = maxOf(startX, currentX)
                    val bottom = maxOf(startY, currentY)
                    canvas.drawRect(left, top, right, bottom, strokePaint)
                }
                else -> {}
            }
        }
    }

    private fun drawArrow(canvas: Canvas, x1: Float, y1: Float, x2: Float, y2: Float, color: Int, width: Float) {
        strokePaint.color = color
        strokePaint.strokeWidth = width

        // Draw shaft
        canvas.drawLine(x1, y1, x2, y2, strokePaint)

        // Draw head
        val angle = atan2((y2 - y1).toDouble(), (x2 - x1).toDouble())
        val arrowHeadLength = (width * 3.5f).coerceAtLeast(36f)
        val headAngleRad = Math.toRadians(32.0)

        val leftWingX = (x2 - arrowHeadLength * cos(angle - headAngleRad)).toFloat()
        val leftWingY = (y2 - arrowHeadLength * sin(angle - headAngleRad)).toFloat()
        val rightWingX = (x2 - arrowHeadLength * cos(angle + headAngleRad)).toFloat()
        val rightWingY = (y2 - arrowHeadLength * sin(angle + headAngleRad)).toFloat()

        val headPath = Path().apply {
            moveTo(x2, y2)
            lineTo(leftWingX, leftWingY)
            lineTo(rightWingX, rightWingY)
            close()
        }

        fillPaint.color = color
        canvas.drawPath(headPath, fillPaint)
    }

    private fun drawStepBadge(canvas: Canvas, cx: Float, cy: Float, number: Int, color: Int) {
        val radius = 32f

        // Drop shadow / border
        fillPaint.color = Color.BLACK
        canvas.drawCircle(cx + 2f, cy + 2f, radius + 2f, fillPaint)

        // Filled Badge
        fillPaint.color = color
        canvas.drawCircle(cx, cy, radius, fillPaint)

        // Text centered vertically
        val textY = cy - ((textPaint.descent() + textPaint.ascent()) / 2)
        canvas.drawText(number.toString(), cx, textY, textPaint)
    }
}
