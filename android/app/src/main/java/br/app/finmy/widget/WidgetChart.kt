package br.app.finmy.widget

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Typeface
import androidx.core.graphics.ColorUtils
import androidx.core.graphics.createBitmap

/**
 * Gráfico de barras das últimas faturas, desenhado num bitmap (o Glance não tem gráficos). A fatura atual usa a cor do
 * cartão; as anteriores, a mesma cor mais clara. Rótulos de mês embaixo, como no gráfico do painel do app.
 */
object WidgetChart {
    fun render(bars: List<ChartBar>, cardColor: Int, night: Boolean, widthPx: Int, heightPx: Int, density: Float): Bitmap {
        val bitmap = createBitmap(widthPx.coerceAtLeast(1), heightPx.coerceAtLeast(1))
        if (bars.isEmpty()) return bitmap
        val canvas = Canvas(bitmap)
        val labelColor = if (night) 0xFFA9A9B4.toInt() else 0xFF6B6B76.toInt()
        val trackColor = if (night) 0xFF2A2A33.toInt() else 0xFFEFEFF4.toInt()
        val label = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = labelColor
            textSize = 11f * density
            textAlign = Paint.Align.CENTER
        }
        val labelCurrent = Paint(label).apply {
            typeface = Typeface.DEFAULT_BOLD
            color = if (night) 0xFFF2F2F5.toInt() else 0xFF1A1A1F.toInt()
        }
        val labelHeight = label.textSize * 1.6f
        val chartHeight = heightPx - labelHeight
        val slot = widthPx.toFloat() / bars.size
        val barWidth = (slot * 0.56f).coerceAtMost(28f * density)
        val radius = barWidth / 3f
        val max = bars.maxOf { it.cents }.coerceAtLeast(1)
        val minBar = 3f * density
        val paint = Paint(Paint.ANTI_ALIAS_FLAG)
        bars.forEachIndexed { i, bar ->
            val cx = slot * i + slot / 2
            val left = cx - barWidth / 2
            paint.color = trackColor
            canvas.drawRoundRect(RectF(left, 0f, left + barWidth, chartHeight), radius, radius, paint)
            val h = if (bar.cents <= 0) 0f else (chartHeight * bar.cents / max).coerceAtLeast(minBar)
            if (h > 0f) {
                paint.color = if (bar.current) cardColor else ColorUtils.setAlphaComponent(cardColor, 0x73)
                canvas.drawRoundRect(RectF(left, chartHeight - h, left + barWidth, chartHeight), radius, radius, paint)
            }
            canvas.drawText(bar.label, cx, heightPx - label.textSize * 0.35f, if (bar.current) labelCurrent else label)
        }
        return bitmap
    }
}
