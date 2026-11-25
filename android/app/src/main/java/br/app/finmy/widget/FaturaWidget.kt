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
import androidx.glance.appwidget.cornerRadius
import androidx.glance.appwidget.provideContent
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

    // 2x2 é o mínimo; em larguras maiores o mesmo layout simplesmente se estica.
    override val sizeMode = androidx.glance.appwidget.SizeMode.Responsive(setOf(DpSize(110.dp, 110.dp), DpSize(250.dp, 110.dp)))

    override suspend fun provideGlance(context: Context, id: GlanceId) {
        val appWidgetId = GlanceAppWidgetManager(context).getAppWidgetId(id)
        provideContent {
            val prefs: Preferences = currentState()
            val ui = WidgetUi.from(WidgetState.decode(prefs[WidgetState.snapshotKey]))
            FaturaWidgetContent(
                ui = ui,
                openIntent = (ui as? WidgetUi.Content)?.let { openInvoiceIntent(BuildConfig.APP_URL, it.appPath) },
                configIntent = configIntent(context, appWidgetId),
            )
        }
    }

    companion object {
        /** Abre a fatura na TWA (a LauncherActivity usa a URL do intent em vez da inicial). */
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
        private const val LAUNCHER = "com.google.androidbrowserhelper.trusted.LauncherActivity"
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

internal val Surface = ColorProvider(day = Color(0xFFFFFFFF), night = Color(0xFF1E1E24))
internal val OnSurface = ColorProvider(day = Color(0xFF1A1A1F), night = Color(0xFFF2F2F5))
private val Muted = ColorProvider(day = Color(0xFF5E5E68), night = Color(0xFFA9A9B4))
private val Warning = ColorProvider(day = Color(0xFFB3261E), night = Color(0xFFFF8A80))

@Composable
fun FaturaWidgetContent(ui: WidgetUi, openIntent: Intent?, configIntent: Intent) {
    val root = GlanceModifier.fillMaxSize().background(Surface).cornerRadius(16.dp).padding(12.dp)
    when (ui) {
        is WidgetUi.Content -> ContentBody(ui, root.clickable(actionStartActivity(requireNotNull(openIntent))))
        WidgetUi.NotConfigured -> Message(root.clickable(actionStartActivity(configIntent)), "Fatura do cartão", "Toque para escolher o cartão")
        is WidgetUi.Loading -> Message(
            root.clickable(actionRunCallback<RefreshAction>()),
            ui.cardName,
            if (ui.offline) "Sem conexão. Toque para tentar de novo" else "Carregando…",
        )
        WidgetUi.NeedsLogin -> Message(root.clickable(actionStartActivity(configIntent)), "Entre de novo", "Toque para entrar")
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
        Text(title, style = TextStyle(color = OnSurface, fontSize = 15.sp, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center), maxLines = 2)
        Spacer(GlanceModifier.size(4.dp))
        Text(hint, style = TextStyle(color = Muted, fontSize = 12.sp, textAlign = TextAlign.Center), maxLines = 2)
    }
}

@Composable
private fun ContentBody(ui: WidgetUi.Content, modifier: GlanceModifier) {
    Column(modifier = modifier) {
        Row(modifier = GlanceModifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Box(GlanceModifier.size(10.dp).cornerRadius(5.dp).background(ColorProvider(day = Color(ui.cardColor), night = Color(ui.cardColor)))) {}
            Spacer(GlanceModifier.width(6.dp))
            Text(
                ui.cardName,
                modifier = GlanceModifier.defaultWeight(),
                style = TextStyle(color = OnSurface, fontSize = 14.sp, fontWeight = FontWeight.Bold),
                maxLines = 1,
            )
            Image(
                provider = ImageProvider(R.drawable.ic_refresh),
                contentDescription = "Atualizar",
                colorFilter = ColorFilter.tint(Muted),
                modifier = GlanceModifier.size(22.dp).padding(2.dp).clickable(actionRunCallback<RefreshAction>()),
            )
        }
        Spacer(GlanceModifier.size(4.dp))
        ValueRow("Atual", ui.totalText, OnSurface)
        ValueRow("Planejado", ui.plannedText, OnSurface)
        Spacer(GlanceModifier.size(2.dp))
        Text("${ui.dueLabel} · ${ui.statusLabel}", style = TextStyle(color = if (ui.statusLabel == "Atrasada") Warning else Muted, fontSize = 11.sp), maxLines = 1)
        Text(
            if (ui.paid) "Restante ${ui.remainingText}" else "Falta ${ui.remainingText}",
            style = TextStyle(color = Muted, fontSize = 11.sp),
            maxLines = 1,
        )
        Text(
            if (ui.offline) "Sem conexão · ${ui.updatedLabel}" else ui.updatedLabel,
            style = TextStyle(color = if (ui.offline) Warning else Muted, fontSize = 10.sp),
            maxLines = 1,
        )
    }
}

@Composable
private fun ValueRow(label: String, value: String, color: ColorProviderType) {
    Row(modifier = GlanceModifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Text(label, modifier = GlanceModifier.defaultWeight(), style = TextStyle(color = Muted, fontSize = 11.sp), maxLines = 1)
        // Valores grandes ("R$ 12.345,67") usam fonte menor para caber sem cortar.
        Text(value, style = TextStyle(color = color, fontSize = if (value.length > 11) 12.sp else 15.sp, fontWeight = FontWeight.Bold), maxLines = 1)
    }
}
