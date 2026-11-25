package br.app.finmy.widget

import android.content.ComponentName
import android.content.Context
import android.content.Intent
import androidx.core.net.toUri
import android.appwidget.AppWidgetManager
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.datastore.preferences.core.Preferences
import androidx.glance.ColorFilter
import androidx.glance.GlanceId
import androidx.glance.GlanceModifier
import androidx.glance.Image
import androidx.glance.ImageProvider
import androidx.glance.action.ActionParameters
import androidx.glance.action.clickable
import androidx.glance.appwidget.GlanceAppWidget
import androidx.glance.appwidget.GlanceAppWidgetManager
import androidx.glance.appwidget.GlanceAppWidgetReceiver
import androidx.glance.appwidget.action.ActionCallback
import androidx.glance.appwidget.action.actionRunCallback
import androidx.glance.appwidget.action.actionStartActivity
import androidx.glance.appwidget.LinearProgressIndicator
import androidx.glance.appwidget.cornerRadius
import androidx.glance.appwidget.provideContent
import androidx.glance.LocalSize
import androidx.glance.background
import androidx.glance.color.ColorProvider
import androidx.glance.currentState
import androidx.glance.layout.Alignment
import androidx.glance.layout.Box
import androidx.glance.layout.Column
import androidx.glance.layout.Row
import androidx.glance.layout.Spacer
import androidx.glance.layout.fillMaxSize
import androidx.glance.layout.fillMaxWidth
import androidx.glance.layout.height
import androidx.glance.layout.padding
import androidx.glance.layout.size
import androidx.glance.layout.width
import androidx.glance.state.PreferencesGlanceStateDefinition
import androidx.glance.text.FontWeight
import androidx.glance.text.Text
import androidx.glance.text.TextStyle
import androidx.glance.text.TextAlign
import androidx.glance.unit.ColorProvider as ColorProviderType
import br.app.finmy.BuildConfig
import br.app.finmy.R

/** Widget "Fatura do cartão" (Glance): Atual, Planejado, vencimento, situação e restante do cartão escolhido. */
class FaturaWidget : GlanceAppWidget() {
    override val stateDefinition = PreferencesGlanceStateDefinition

    // Pequeno (2x2): valor e barra. Largo: + planejado ao lado. Alto (a partir de ~4x3): + gráfico das últimas faturas.
    override val sizeMode = androidx.glance.appwidget.SizeMode.Responsive(
        setOf(SMALL, WIDE, TALL),
    )

    override suspend fun provideGlance(context: Context, id: GlanceId) {
        val appWidgetId = GlanceAppWidgetManager(context).getAppWidgetId(id)
        val night = (context.resources.configuration.uiMode and android.content.res.Configuration.UI_MODE_NIGHT_MASK) ==
            android.content.res.Configuration.UI_MODE_NIGHT_YES
        val density = context.resources.displayMetrics.density
        provideContent {
            val prefs: Preferences = currentState()
            val ui = WidgetUi.from(WidgetState.decode(prefs[WidgetState.snapshotKey]))
            FaturaWidgetContent(
                ui = ui,
                openIntent = (ui as? WidgetUi.Content)?.let { openInvoiceIntent(BuildConfig.APP_URL, it.appPath) },
                configIntent = configIntent(context, appWidgetId),
                night = night,
                density = density,
            )
        }
    }

    companion object {
        val SMALL = DpSize(110.dp, 110.dp)
        val WIDE = DpSize(250.dp, 110.dp)
        val TALL = DpSize(250.dp, 180.dp)

        /** Abre a fatura no app (a MainActivity carrega a URL do intent). */
        fun openInvoiceIntent(appUrl: String, appPath: String): Intent =
            Intent(Intent.ACTION_VIEW, "$appUrl$appPath".toUri())
                .setComponent(ComponentName(PACKAGE, LAUNCHER))
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)

        /** Abre a configuração (entrar de novo ou escolher outro cartão) deste widget. */
        fun configIntent(context: Context, appWidgetId: Int): Intent =
            Intent(context, WidgetConfigActivity::class.java)
                .putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, appWidgetId)
                // Um pedido por widget: sem isso o Android reaproveitaria o intent de outro.
                .setData("finmy-widget://config/$appWidgetId".toUri())
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)

        private const val PACKAGE = "br.app.finmy"
        private const val LAUNCHER = "br.app.finmy.MainActivity"
    }
}

class FaturaWidgetReceiver : GlanceAppWidgetReceiver() {
    override val glanceAppWidget: GlanceAppWidget = FaturaWidget()

    override fun onEnabled(context: Context) {
        super.onEnabled(context)
        WidgetWork.schedule(context)
    }

    override fun onDisabled(context: Context) {
        super.onDisabled(context)
        WidgetWork.cancel(context)
    }
}

/** Toque no ícone de atualizar. */
class RefreshAction : ActionCallback {
    override suspend fun onAction(context: Context, glanceId: GlanceId, parameters: ActionParameters) {
        WidgetState.refresh(context, glanceId)
    }
}

internal val Surface = ColorProvider(day = Color(0xFFFFFFFF), night = Color(0xFF17171C))
internal val OnSurface = ColorProvider(day = Color(0xFF1A1A1F), night = Color(0xFFF2F2F5))
private val Muted = ColorProvider(day = Color(0xFF6B6B76), night = Color(0xFFA9A9B4))
private val Warning = ColorProvider(day = Color(0xFFB3261E), night = Color(0xFFFF8A80))
private val Track = ColorProvider(day = Color(0xFFEFEFF4), night = Color(0xFF2A2A33))

/** Cores do selo de situação (fundo, texto), no tom do app. */
private fun statusColors(code: String): Pair<ColorProviderType, ColorProviderType> = when (code) {
    "paid" -> ColorProvider(day = Color(0xFFDDF5E6), night = Color(0xFF173A26)) to ColorProvider(day = Color(0xFF1B7A43), night = Color(0xFF7EE0A6))
    "overdue" -> ColorProvider(day = Color(0xFFFDE2E1), night = Color(0xFF4A1C1A)) to ColorProvider(day = Color(0xFFB3261E), night = Color(0xFFFF8A80))
    "closed" -> ColorProvider(day = Color(0xFFFFF1D6), night = Color(0xFF3F3110)) to ColorProvider(day = Color(0xFF8A5A00), night = Color(0xFFFFCF66))
    else -> ColorProvider(day = Color(0xFFE9E7FE), night = Color(0xFF2A2652)) to ColorProvider(day = Color(0xFF4A3FD6), night = Color(0xFFB9B2FF))
}

@Composable
fun FaturaWidgetContent(ui: WidgetUi, openIntent: Intent?, configIntent: Intent, night: Boolean = false, density: Float = 2f) {
    val root = GlanceModifier.fillMaxSize().background(Surface).cornerRadius(24.dp).padding(16.dp)
    when (ui) {
        is WidgetUi.Content -> ContentBody(ui, root.clickable(actionStartActivity(requireNotNull(openIntent))), night, density)
        WidgetUi.NotConfigured -> Message(root.clickable(actionStartActivity(configIntent)), "Fatura do cartão", "Toque para escolher o cartão")
        is WidgetUi.Loading -> Message(
            root.clickable(actionRunCallback<RefreshAction>()),
            ui.cardName,
            if (ui.offline) "Sem conexão. Toque para tentar de novo" else "Carregando…",
        )
        WidgetUi.NeedsLogin -> Message(root.clickable(actionStartActivity(configIntent)), "Conecte o widget", "Toque e abra o app Fin-My")
        WidgetUi.CardUnavailable -> Message(
            root.clickable(actionStartActivity(configIntent)),
            "Cartão indisponível",
            "Toque para escolher outro",
        )
    }
}

@Composable
private fun Message(modifier: GlanceModifier, title: String, hint: String) {
    Column(modifier = modifier, verticalAlignment = Alignment.CenterVertically, horizontalAlignment = Alignment.CenterHorizontally) {
        Text(title, style = TextStyle(color = OnSurface, fontSize = 18.sp, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center), maxLines = 2)
        Spacer(GlanceModifier.size(6.dp))
        Text(hint, style = TextStyle(color = Muted, fontSize = 14.sp, textAlign = TextAlign.Center), maxLines = 2)
    }
}

@Composable
private fun ContentBody(ui: WidgetUi.Content, modifier: GlanceModifier, night: Boolean, density: Float) {
    val size = LocalSize.current
    val wide = size.width >= FaturaWidget.WIDE.width
    val tall = size.height >= FaturaWidget.TALL.height
    val cardColor = ColorProvider(day = Color(ui.cardColor), night = Color(ui.cardColor))
    Column(modifier = modifier) {
        // Cabeçalho: cor e nome do cartão, situação e atualizar.
        Row(modifier = GlanceModifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Box(GlanceModifier.size(12.dp).cornerRadius(6.dp).background(cardColor)) {}
            Spacer(GlanceModifier.width(8.dp))
            Text(
                ui.cardName,
                modifier = GlanceModifier.defaultWeight(),
                style = TextStyle(color = OnSurface, fontSize = 16.sp, fontWeight = FontWeight.Bold),
                maxLines = 1,
            )
            if (wide) {
                val (bg, fg) = statusColors(ui.statusCode)
                Box(GlanceModifier.background(bg).cornerRadius(999.dp).padding(horizontal = 10.dp, vertical = 3.dp)) {
                    Text(ui.statusLabel, style = TextStyle(color = fg, fontSize = 12.sp, fontWeight = FontWeight.Medium), maxLines = 1)
                }
                Spacer(GlanceModifier.width(6.dp))
            }
            Image(
                provider = ImageProvider(R.drawable.ic_refresh),
                contentDescription = "Atualizar",
                colorFilter = ColorFilter.tint(Muted),
                modifier = GlanceModifier.size(28.dp).padding(4.dp).clickable(actionRunCallback<RefreshAction>()),
            )
        }
        Spacer(GlanceModifier.size(if (tall) 10.dp else 6.dp))

        // Valor atual em destaque.
        Text("Fatura atual", style = TextStyle(color = Muted, fontSize = 13.sp), maxLines = 1)
        Text(
            ui.totalText,
            style = TextStyle(color = OnSurface, fontSize = valueSize(ui.totalText, wide), fontWeight = FontWeight.Bold),
            maxLines = 1,
        )
        Spacer(GlanceModifier.size(6.dp))

        // Barra Atual ÷ Planejado na cor do cartão.
        LinearProgressIndicator(
            progress = ui.progress,
            modifier = GlanceModifier.fillMaxWidth().height(8.dp).cornerRadius(4.dp),
            color = cardColor,
            backgroundColor = Track,
        )
        Spacer(GlanceModifier.size(6.dp))
        Row(modifier = GlanceModifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(
                "Planejado ${ui.plannedText}",
                modifier = GlanceModifier.defaultWeight(),
                style = TextStyle(color = OnSurface, fontSize = 14.sp, fontWeight = FontWeight.Medium),
                maxLines = 1,
            )
            if (wide) {
                Text(
                    ui.dueLabel,
                    style = TextStyle(color = if (ui.statusCode == "overdue") Warning else Muted, fontSize = 14.sp),
                    maxLines = 1,
                )
            }
        }
        if (!wide) {
            Text(
                "${ui.dueLabel} · ${ui.statusLabel}",
                style = TextStyle(color = if (ui.statusCode == "overdue") Warning else Muted, fontSize = 13.sp),
                maxLines = 1,
            )
        }

        if (tall && ui.chart.isNotEmpty()) {
            Spacer(GlanceModifier.size(12.dp))
            val chartHeight = (size.height - 178.dp).coerceIn(48.dp, 140.dp)
            val widthPx = ((size.width - 32.dp).value * density).toInt()
            val heightPx = (chartHeight.value * density).toInt()
            Image(
                provider = ImageProvider(WidgetChart.render(ui.chart, ui.cardColor, night, widthPx, heightPx, density)),
                contentDescription = "Últimas faturas: " + ui.chart.joinToString { "${it.label} ${PtBr.cents(it.cents)}" },
                modifier = GlanceModifier.fillMaxWidth().height(chartHeight),
            )
        }

        Spacer(GlanceModifier.defaultWeight())
        Text(
            listOfNotNull(
                if (ui.paid) null else "Falta ${ui.remainingText}",
                if (ui.offline) "sem conexão" else null,
                ui.updatedLabel.ifEmpty { null },
            ).joinToString(" · "),
            style = TextStyle(color = if (ui.offline) Warning else Muted, fontSize = 12.sp),
            maxLines = 1,
        )
    }
}

/** Valores grandes ("R$ 12.345,67") diminuem para caber sem cortar. */
private fun valueSize(text: String, wide: Boolean) = when {
    wide -> if (text.length > 13) 26.sp else 30.sp
    text.length > 11 -> 20.sp
    else -> 24.sp
}
