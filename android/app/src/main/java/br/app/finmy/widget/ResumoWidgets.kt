package br.app.finmy.widget

import android.content.ComponentName
import android.content.Context
import android.content.Intent
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.net.toUri
import androidx.datastore.preferences.core.Preferences
import androidx.glance.ColorFilter
import androidx.glance.GlanceId
import androidx.glance.GlanceModifier
import androidx.glance.Image
import androidx.glance.ImageProvider
import androidx.glance.LocalSize
import androidx.glance.action.ActionParameters
import androidx.glance.action.clickable
import androidx.glance.appwidget.GlanceAppWidget
import androidx.glance.appwidget.GlanceAppWidgetReceiver
import androidx.glance.appwidget.LinearProgressIndicator
import androidx.glance.appwidget.SizeMode
import androidx.glance.appwidget.action.ActionCallback
import androidx.glance.appwidget.action.actionRunCallback
import androidx.glance.appwidget.action.actionStartActivity
import androidx.glance.appwidget.cornerRadius
import androidx.glance.appwidget.lazy.LazyColumn
import androidx.glance.appwidget.lazy.items
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
import androidx.glance.layout.height
import androidx.glance.layout.padding
import androidx.glance.layout.size
import androidx.glance.layout.width
import androidx.glance.state.PreferencesGlanceStateDefinition
import androidx.glance.text.FontWeight
import androidx.glance.text.Text
import androidx.glance.text.TextAlign
import androidx.glance.text.TextStyle
import androidx.glance.unit.ColorProvider as ColorProviderType
import br.app.finmy.BuildConfig
import br.app.finmy.R
import java.time.Instant
import java.time.ZoneId

// ---------- Cores do app ----------
/** Cor de destaque escolhida no app ([AccentStore]). */
internal val Primary get() = AccentStore.current.primary
internal val OnPrimary = ColorProvider(day = Color(0xFFFFFFFF), night = Color(0xFF17171C))
internal val Income = ColorProvider(day = Color(0xFF1B7A43), night = Color(0xFF7EE0A6))
internal val Expense = ColorProvider(day = Color(0xFFB3261E), night = Color(0xFFFF8A80))
private val Chip = ColorProvider(day = Color(0xFFF1F1F6), night = Color(0xFF24242C))
private val ChipOverdue = ColorProvider(day = Color(0xFFFDE2E1), night = Color(0xFF4A1C1A))

/** Abre uma tela do app (MainActivity carrega a URL do intent). */
internal fun openAppIntent(path: String): Intent =
    Intent(Intent.ACTION_VIEW, "${BuildConfig.APP_URL}$path".toUri())
        .setComponent(ComponentName("br.app.finmy", "br.app.finmy.MainActivity"))
        .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)

private val MONTH_NAMES = listOf(
    "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho", "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro",
)

/** "2026-11" → "Novembro". */
internal fun monthName(yearMonth: String) = yearMonth.substringAfter('-').toIntOrNull()?.let { MONTH_NAMES.getOrNull(it - 1) } ?: yearMonth

internal fun updatedLabel(snapshot: OverviewSnapshot, zone: ZoneId = ZoneId.systemDefault()): String {
    val time = snapshot.updatedAtMillis?.let { PtBr.time(Instant.ofEpochMilli(it), zone) } ?: return ""
    return if (snapshot.stale) "sem conexão · atualizado às $time" else "atualizado às $time"
}

// ---------- Peças comuns ----------
private fun rootModifier() = GlanceModifier.fillMaxSize().background(Surface).cornerRadius(24.dp).padding(16.dp)

@Composable
private fun Header(title: String, subtitle: String? = null) {
    Row(modifier = GlanceModifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Column(modifier = GlanceModifier.defaultWeight()) {
            if (subtitle != null) Text(subtitle, style = TextStyle(color = Muted, fontSize = 12.sp), maxLines = 1)
            Text(title, style = TextStyle(color = OnSurface, fontSize = 17.sp, fontWeight = FontWeight.Bold), maxLines = 1)
        }
        Image(
            provider = ImageProvider(R.drawable.ic_refresh),
            contentDescription = "Atualizar",
            colorFilter = ColorFilter.tint(Muted),
            modifier = GlanceModifier.size(28.dp).padding(4.dp).clickable(actionRunCallback<OverviewRefreshAction>()),
        )
    }
}

@Composable
private fun Footer(text: String, warning: Boolean) {
    if (text.isEmpty()) return
    Text(text, style = TextStyle(color = if (warning) Warning else Muted, fontSize = 12.sp), maxLines = 1)
}

/** Estados sem dados (carregando, conectar), comuns aos widgets de resumo. */
@Composable
private fun overviewPlaceholder(snapshot: OverviewSnapshot?, title: String): Boolean {
    val needsLogin = snapshot?.state == SnapshotState.NEEDS_LOGIN
    val data = snapshot?.data
    if (!needsLogin && data != null) return false
    val (headline, hint, action) = when {
        needsLogin -> Triple("Conecte o widget", "Toque e abra o app Fin-My", actionStartActivity(openAppIntent("/")))
        snapshot?.stale == true -> Triple(title, "Sem conexão. Toque para tentar de novo", actionRunCallback<OverviewRefreshAction>())
        else -> Triple(title, "Carregando…", actionRunCallback<OverviewRefreshAction>())
    }
    Column(
        modifier = rootModifier().clickable(action),
        verticalAlignment = Alignment.CenterVertically,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(headline, style = TextStyle(color = OnSurface, fontSize = 18.sp, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center), maxLines = 2)
        Spacer(GlanceModifier.size(6.dp))
        Text(hint, style = TextStyle(color = Muted, fontSize = 14.sp, textAlign = TextAlign.Center), maxLines = 2)
    }
    return true
}

private fun bigValueSize(text: String, wide: Boolean): TextUnit = when {
    wide -> if (text.length > 13) 26.sp else 30.sp
    text.length > 11 -> 20.sp
    else -> 24.sp
}

// ---------- Mês atual ----------
@Composable
fun MesContent(snapshot: OverviewSnapshot?) {
    if (overviewPlaceholder(snapshot, "Mês atual")) return
    val s = requireNotNull(snapshot)
    val d = requireNotNull(s.data)
    val wide = LocalSize.current.width >= 250.dp
    val final = d.plannedFinal
    val toPay = (d.plannedExpense.cents - d.actualExpense.cents).coerceAtLeast(0)
    val toReceive = (d.plannedIncome.cents - d.actualIncome.cents).coerceAtLeast(0)
    Column(modifier = rootModifier().clickable(actionStartActivity(openAppIntent(d.monthPath)))) {
        Header(monthName(d.month), "Mês atual")
        Spacer(GlanceModifier.size(8.dp))
        Text("Saldo previsto no fim do mês", style = TextStyle(color = Muted, fontSize = 13.sp), maxLines = 1)
        Text(
            final.text,
            style = TextStyle(color = if (final.cents < 0) Expense else OnSurface, fontSize = bigValueSize(final.text, wide), fontWeight = FontWeight.Bold),
            maxLines = 1,
        )
        Spacer(GlanceModifier.size(8.dp))
        // Quanto das saídas previstas já saiu.
        LinearProgressIndicator(
            progress = WidgetUi.progress(d.actualExpense.cents, d.plannedExpense.cents),
            modifier = GlanceModifier.fillMaxWidth().height(8.dp).cornerRadius(4.dp),
            color = Primary,
            backgroundColor = Track,
        )
        Spacer(GlanceModifier.size(8.dp))
        Row(modifier = GlanceModifier.fillMaxWidth()) {
            FlowFigure("Entradas", d.plannedIncome.text, Income, GlanceModifier.defaultWeight())
            Spacer(GlanceModifier.width(8.dp))
            FlowFigure("Saídas", d.plannedExpense.text, Expense, GlanceModifier.defaultWeight())
        }
        Spacer(GlanceModifier.defaultWeight())
        Footer(
            listOfNotNull(
                if (toPay > 0) "Falta pagar ${PtBr.cents(toPay)}" else null,
                if (toReceive > 0 && wide) "receber ${PtBr.cents(toReceive)}" else null,
            ).joinToString(" · ").ifEmpty { updatedLabel(s) },
            warning = s.stale,
        )
    }
}

@Composable
private fun FlowFigure(label: String, value: String, color: ColorProviderType, modifier: GlanceModifier) {
    Column(modifier = modifier) {
        Text(label, style = TextStyle(color = Muted, fontSize = 12.sp), maxLines = 1)
        Text(value, style = TextStyle(color = color, fontSize = if (value.length > 12) 14.sp else 16.sp, fontWeight = FontWeight.Bold), maxLines = 1)
    }
}

// ---------- Próximos vencimentos ----------
@Composable
fun VencimentosContent(snapshot: OverviewSnapshot?) {
    if (overviewPlaceholder(snapshot, "Próximos vencimentos")) return
    val s = requireNotNull(snapshot)
    val d = requireNotNull(s.data)
    Column(modifier = rootModifier().clickable(actionStartActivity(openAppIntent(d.monthPath)))) {
        Header("Próximos vencimentos", if (d.upcoming.any { it.overdue }) "${d.upcoming.count { it.overdue }} em atraso" else null)
        Spacer(GlanceModifier.size(8.dp))
        if (d.upcoming.isEmpty()) {
            Box(GlanceModifier.fillMaxWidth().defaultWeight(), contentAlignment = Alignment.Center) {
                Text("Tudo em dia por aqui", style = TextStyle(color = Muted, fontSize = 15.sp), maxLines = 1)
            }
        } else {
            LazyColumn(modifier = GlanceModifier.fillMaxWidth().defaultWeight()) {
                items(d.upcoming) { item -> UpcomingRow(item) }
            }
        }
        Footer(updatedLabel(s), s.stale)
    }
}

@Composable
private fun UpcomingRow(item: OverviewUpcoming) {
    Row(modifier = GlanceModifier.fillMaxWidth().padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(
            GlanceModifier.width(52.dp).background(if (item.overdue) ChipOverdue else Chip).cornerRadius(10.dp).padding(vertical = 4.dp),
            contentAlignment = Alignment.Center,
        ) {
            Text(
                PtBr.shortDate(item.date),
                style = TextStyle(color = if (item.overdue) Warning else OnSurface, fontSize = 13.sp, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center),
                maxLines = 1,
            )
        }
        Spacer(GlanceModifier.width(10.dp))
        Text(item.description, modifier = GlanceModifier.defaultWeight(), style = TextStyle(color = OnSurface, fontSize = 15.sp), maxLines = 1)
        Spacer(GlanceModifier.width(8.dp))
        val color = when (item.kind) {
            "income" -> Income
            "expense" -> Expense
            else -> Primary
        }
        Text(
            (if (item.kind == "income") "+" else "") + item.amount.text,
            style = TextStyle(color = color, fontSize = 15.sp, fontWeight = FontWeight.Bold),
            maxLines = 1,
        )
    }
}

// ---------- Acertos ----------
@Composable
fun AcertosContent(snapshot: OverviewSnapshot?) {
    if (overviewPlaceholder(snapshot, "Acertos")) return
    val s = requireNotNull(snapshot)
    val d = requireNotNull(s.data)
    val toReceive = d.settlements.filter { it.amount.cents > 0 }.sumOf { it.amount.cents }
    Column(modifier = rootModifier().clickable(actionStartActivity(openAppIntent(d.settlementsPath)))) {
        Header("Acertos", if (toReceive > 0) "A receber ${PtBr.cents(toReceive)}" else null)
        Spacer(GlanceModifier.size(8.dp))
        if (d.settlements.isEmpty()) {
            Box(GlanceModifier.fillMaxWidth().defaultWeight(), contentAlignment = Alignment.Center) {
                Text("Ninguém deve nada", style = TextStyle(color = Muted, fontSize = 15.sp), maxLines = 1)
            }
        } else {
            LazyColumn(modifier = GlanceModifier.fillMaxWidth().defaultWeight()) {
                items(d.settlements) { person -> PersonRow(person) }
            }
        }
        Footer(updatedLabel(s), s.stale)
    }
}

@Composable
private fun PersonRow(person: OverviewSettlement) {
    val color = PtBr.parseColor(person.color)
    val owes = person.amount.cents > 0
    Row(modifier = GlanceModifier.fillMaxWidth().padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(
            GlanceModifier.size(34.dp).cornerRadius(17.dp).background(ColorProvider(day = Color(color), night = Color(color))),
            contentAlignment = Alignment.Center,
        ) {
            Text(
                person.name.take(1).uppercase(),
                style = TextStyle(color = ColorProvider(day = Color.White, night = Color.White), fontSize = 16.sp, fontWeight = FontWeight.Bold),
            )
        }
        Spacer(GlanceModifier.width(10.dp))
        Column(modifier = GlanceModifier.defaultWeight()) {
            Text(person.name, style = TextStyle(color = OnSurface, fontSize = 15.sp, fontWeight = FontWeight.Medium), maxLines = 1)
            Text(if (owes) "deve para você" else "tem crédito", style = TextStyle(color = Muted, fontSize = 12.sp), maxLines = 1)
        }
        Text(
            PtBr.cents(kotlin.math.abs(person.amount.cents)),
            style = TextStyle(color = if (owes) Income else Muted, fontSize = 16.sp, fontWeight = FontWeight.Bold),
            maxLines = 1,
        )
    }
}

// ---------- Lançar ----------
@Composable
fun LancarContent() {
    val wide = LocalSize.current.width >= 110.dp
    Box(
        modifier = GlanceModifier.fillMaxSize().background(Primary).cornerRadius(24.dp).clickable(actionStartActivity(openAppIntent("/mes?lancar=1"))),
        contentAlignment = Alignment.Center,
    ) {
        if (wide) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("+", style = TextStyle(color = OnPrimary, fontSize = 30.sp, fontWeight = FontWeight.Bold))
                Spacer(GlanceModifier.width(8.dp))
                Text("Lançar", style = TextStyle(color = OnPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold))
            }
        } else {
            Text("+", style = TextStyle(color = OnPrimary, fontSize = 34.sp, fontWeight = FontWeight.Bold))
        }
    }
}

// ---------- Widgets e receivers ----------
/** Base dos widgets de resumo: leem o [OverviewSnapshot] do próprio estado. */
abstract class OverviewWidget : GlanceAppWidget() {
    override val stateDefinition = PreferencesGlanceStateDefinition
    override val sizeMode = SizeMode.Responsive(setOf(DpSize(110.dp, 110.dp), DpSize(250.dp, 110.dp), DpSize(250.dp, 180.dp)))

    @Composable
    abstract fun Content(snapshot: OverviewSnapshot?)

    override suspend fun provideGlance(context: Context, id: GlanceId) {
        AccentStore.load(context)
        provideContent {
            val prefs: Preferences = currentState()
            Content(OverviewState.decode(prefs[OverviewState.key]))
        }
    }
}

class MesWidget : OverviewWidget() {
    @Composable
    override fun Content(snapshot: OverviewSnapshot?) = MesContent(snapshot)
}

class VencimentosWidget : OverviewWidget() {
    @Composable
    override fun Content(snapshot: OverviewSnapshot?) = VencimentosContent(snapshot)
}

class AcertosWidget : OverviewWidget() {
    @Composable
    override fun Content(snapshot: OverviewSnapshot?) = AcertosContent(snapshot)
}

class LancarWidget : GlanceAppWidget() {
    override val sizeMode = SizeMode.Responsive(setOf(DpSize(48.dp, 48.dp), DpSize(110.dp, 48.dp)))
    override suspend fun provideGlance(context: Context, id: GlanceId) {
        AccentStore.load(context)
        provideContent { LancarContent() }
    }
}

/** Receiver dos widgets de resumo: agenda a atualização periódica e busca já ao entrar na tela. */
abstract class OverviewReceiver : GlanceAppWidgetReceiver() {
    override fun onEnabled(context: Context) {
        super.onEnabled(context)
        WidgetWork.schedule(context)
    }

    override fun onUpdate(context: Context, appWidgetManager: android.appwidget.AppWidgetManager, appWidgetIds: IntArray) {
        super.onUpdate(context, appWidgetManager, appWidgetIds)
        WidgetWork.refreshNow(context)
    }

    override fun onDisabled(context: Context) {
        super.onDisabled(context)
        WidgetWork.cancelIfNoWidgets(context)
    }
}

class MesWidgetReceiver : OverviewReceiver() {
    override val glanceAppWidget: GlanceAppWidget = MesWidget()
}

class VencimentosWidgetReceiver : OverviewReceiver() {
    override val glanceAppWidget: GlanceAppWidget = VencimentosWidget()
}

class AcertosWidgetReceiver : OverviewReceiver() {
    override val glanceAppWidget: GlanceAppWidget = AcertosWidget()
}

class LancarWidgetReceiver : GlanceAppWidgetReceiver() {
    override val glanceAppWidget: GlanceAppWidget = LancarWidget()
}

/** Toque no ícone de atualizar dos widgets de resumo. */
class OverviewRefreshAction : ActionCallback {
    override suspend fun onAction(context: Context, glanceId: GlanceId, parameters: ActionParameters) {
        OverviewState.refreshAll(context)
    }
}
