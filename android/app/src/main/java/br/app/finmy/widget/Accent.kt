package br.app.finmy.widget

import android.content.Context
import androidx.compose.ui.graphics.Color
import androidx.core.content.edit
import androidx.glance.appwidget.GlanceAppWidgetManager
import androidx.glance.color.ColorProvider
import androidx.glance.unit.ColorProvider as ColorProviderType

/**
 * Cor de destaque escolhida no app (Perfil › Aparência, 010) para os widgets e o relógio. Mesmos ids do front
 * (`AcentoService`); cores = o `--primary` do app no claro e no escuro.
 */
enum class Accent(val id: String, val day: Long, val night: Long, val soft: Long, val softNight: Long) {
    INDIGO("indigo", 0xFF4F46E5, 0xFFB9B2FF, 0xFFE9E7FE, 0xFF2A2652),
    AZUL("azul", 0xFF1E5FD8, 0xFF8DB8FF, 0xFFE1ECFD, 0xFF1C2E52),
    TURQUESA("turquesa", 0xFF00818F, 0xFF5FD3DE, 0xFFDDF4F6, 0xFF143C40),
    VERDE("verde", 0xFF157F3C, 0xFF6FDB97, 0xFFDDF5E6, 0xFF173A26),
    AMBAR("ambar", 0xFFA15C00, 0xFFF5B95C, 0xFFFDF0DA, 0xFF40300F),
    LARANJA("laranja", 0xFFC2410C, 0xFFFF9F6E, 0xFFFDE6DA, 0xFF45230F),
    ROSA("rosa", 0xFFC0265E, 0xFFFF8FB5, 0xFFFCE1EB, 0xFF461A2B),
    ROXO("roxo", 0xFF8A2BC9, 0xFFDDA2FF, 0xFFF2E2FC, 0xFF35184A);

    val primary: ColorProviderType get() = ColorProvider(day = Color(day), night = Color(night))
    val primarySoft: ColorProviderType get() = ColorProvider(day = Color(soft), night = Color(softNight))

    companion object {
        fun from(id: String?): Accent = entries.firstOrNull { it.id == id } ?: INDIGO
    }
}

/** Guarda a cor de destaque no celular e redesenha os widgets. */
object AccentStore {
    private const val PREFS = "finmy_accent"
    private const val KEY = "accent"

    /** A cor usada ao desenhar (lida em [load] antes de cada composição dos widgets). */
    @Volatile
    var current: Accent = Accent.INDIGO
        private set

    fun load(context: Context): Accent =
        Accent.from(context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString(KEY, null)).also { current = it }

    suspend fun save(context: Context, id: String) {
        val accent = Accent.from(id)
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit { putString(KEY, accent.id) }
        current = accent
        val manager = GlanceAppWidgetManager(context)
        (OverviewState.widgets() + listOf(FaturaWidget(), LancarWidget())).forEach { widget ->
            manager.getGlanceIds(widget.javaClass).forEach { widget.update(context, it) }
        }
    }
}
