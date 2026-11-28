package br.app.finmy.wear

import android.app.RemoteInput
import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.wear.compose.foundation.lazy.ScalingLazyColumn
import androidx.wear.compose.foundation.lazy.items
import androidx.wear.compose.foundation.lazy.rememberScalingLazyListState
import androidx.wear.compose.material.Button
import androidx.wear.compose.material.ButtonDefaults
import androidx.wear.compose.material.Chip
import androidx.wear.compose.material.ChipDefaults
import androidx.wear.compose.material.CircularProgressIndicator
import androidx.wear.compose.material.Colors
import androidx.wear.compose.material.ListHeader
import androidx.wear.compose.material.MaterialTheme
import androidx.wear.compose.material.Scaffold
import androidx.wear.compose.material.Text
import androidx.wear.compose.material.TimeText
import androidx.wear.compose.material.dialog.Alert
import androidx.wear.compose.material.dialog.Dialog
import androidx.wear.input.RemoteInputIntentHelper
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

// Cores do app (modo escuro, como no relógio).
private val Green = Color(0xFF7EE0A6)
private val Red = Color(0xFFFF8A80)
private val Muted = Color(0xFFA9A9B4)

/** Tema do relógio na cor de destaque escolhida no app do celular. */
@Composable
private fun FinMyTheme(accent: WearAccent, content: @Composable () -> Unit) = MaterialTheme(
    colors = Colors(primary = Color(accent.main), primaryVariant = Color(accent.deep), secondary = Green, error = Red, onPrimary = Color(0xFF17171C)),
    content = content,
)

/** App do relógio (010): resumo do mês, próximos vencimentos (marcar como pago) e lançamento rápido. */
class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val startOnLaunch = intent?.getBooleanExtra(EXTRA_LANCAR, false) == true
        setContent { App(startOnLaunch, onFinish = { finish() }) }
    }

    companion object {
        const val EXTRA_LANCAR = "lancar"
    }
}

private sealed interface Route {
    data object Home : Route
    data object Kind : Route
    data class Amount(val kind: String) : Route
    data class Describe(val kind: String, val amount: String) : Route
    data class Sending(val kind: String, val amount: String, val description: String) : Route
    data class Done(val title: String, val detail: String, val ok: Boolean) : Route
}

@Composable
private fun App(startOnLaunch: Boolean, onFinish: () -> Unit) {
    val context = LocalContext.current
    val link = remember { PhoneLink(context) }
    var route by remember { mutableStateOf<Route>(if (startOnLaunch) Route.Kind else Route.Home) }
    var overview by remember { mutableStateOf(link.cachedOverview()) }
    var status by remember { mutableStateOf<Reply?>(null) }
    var loading by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    fun refresh() {
        loading = true
        scope.launch {
            val (data, reply) = link.overview()
            overview = data
            status = reply
            loading = false
            TileAndComplications.requestUpdate(context)
        }
    }

    LaunchedEffect(Unit) { refresh() }

    FinMyTheme(WearAccent.from(overview?.accent)) { Screen(route, startOnLaunch, onFinish, link, overview, status, loading, ::refresh) { route = it } }
}

@Composable
private fun Screen(
    route: Route,
    startOnLaunch: Boolean,
    onFinish: () -> Unit,
    link: PhoneLink,
    overview: Overview?,
    status: Reply?,
    loading: Boolean,
    refresh: () -> Unit,
    go: (Route) -> Unit,
) {
    val context = LocalContext.current
    when (val r = route) {
        Route.Home -> Home(overview, status, loading, onRefresh = refresh, onLaunch = { go(Route.Kind) }, link = link, onSettled = refresh)
        Route.Kind -> KindStep { kind -> go(Route.Amount(kind)) }
        is Route.Amount -> AmountStep(r.kind) { amount -> go(Route.Describe(r.kind, amount)) }
        is Route.Describe -> DescribeStep(
            suggestions = Suggestions.merge(History(context).recent(r.kind), overview?.upcoming.orEmpty(), r.kind),
            preview = AmountInput.preview(r.amount),
        ) { description -> go(Route.Sending(r.kind, r.amount, description)) }
        is Route.Sending -> {
            Progress("Lançando…")
            LaunchedEffect(r) {
                val reply = link.createEntry(r.kind, r.description, r.amount)
                go(if (reply is Reply.Ok) {
                    val created = runCatching { WearJson.decodeFromJsonElement(CreatedEntry.serializer(), reply.data) }.getOrNull()
                    Route.Done(
                        "Lançado",
                        listOfNotNull(created?.amount?.text ?: AmountInput.preview(r.amount), created?.wallet?.takeIf { it.isNotBlank() }).joinToString("\n"),
                        ok = true,
                    )
                } else {
                    Route.Done("Não lançado", reply.message(), ok = false)
                })
            }
        }
        is Route.Done -> {
            Result(r.title, r.detail, r.ok)
            LaunchedEffect(r) {
                delay(if (r.ok) 1800 else 3500)
                if (startOnLaunch && r.ok) onFinish() else {
                    go(Route.Home)
                    refresh()
                }
            }
        }
    }
}

@Composable
private fun Home(
    overview: Overview?,
    status: Reply?,
    loading: Boolean,
    onRefresh: () -> Unit,
    onLaunch: () -> Unit,
    link: PhoneLink,
    onSettled: () -> Unit,
) {
    val listState = rememberScalingLazyListState()
    var confirm by remember { mutableStateOf<Upcoming?>(null) }
    val scope = rememberCoroutineScope()
    Scaffold(timeText = { TimeText() }) {
        ScalingLazyColumn(state = listState, modifier = Modifier.fillMaxSize(), horizontalAlignment = Alignment.CenterHorizontally) {
            item { ListHeader { Text(overview?.let { WearFormat.monthName(it.month) } ?: "Fin-My", color = Muted) } }
            item {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("Saldo previsto", fontSize = 12.sp, color = Muted)
                    Text(
                        overview?.plannedFinal?.text ?: "—",
                        fontSize = 24.sp,
                        fontWeight = FontWeight.Bold,
                        color = if ((overview?.plannedFinal?.cents ?: 0) < 0) Red else Color.White,
                        maxLines = 1,
                    )
                    if (status != null && status !is Reply.Ok) {
                        Text(status.message(), fontSize = 11.sp, color = Red, textAlign = TextAlign.Center)
                    }
                }
            }
            item {
                Chip(
                    onClick = onLaunch,
                    label = { Text("Lançar", fontWeight = FontWeight.Bold) },
                    icon = { Text("+", fontSize = 22.sp, fontWeight = FontWeight.Bold) },
                    colors = ChipDefaults.primaryChipColors(),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
            if (overview != null) {
                item {
                    Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), horizontalArrangement = Arrangement.SpaceBetween) {
                        Column { Text("Entradas", fontSize = 11.sp, color = Muted); Text(overview.plannedIncome.text, fontSize = 13.sp, color = Green, maxLines = 1) }
                        Column(horizontalAlignment = Alignment.End) { Text("Saídas", fontSize = 11.sp, color = Muted); Text(overview.plannedExpense.text, fontSize = 13.sp, color = Red, maxLines = 1) }
                    }
                }
                item { ListHeader { Text(if (overview.upcoming.isEmpty()) "Tudo em dia" else "Próximos vencimentos", color = Muted) } }
                items(overview.upcoming) { item ->
                    Chip(
                        onClick = { if (item.entryId != null) confirm = item },
                        enabled = true,
                        label = { Text(item.description, maxLines = 1, overflow = TextOverflow.Ellipsis) },
                        secondaryLabel = {
                            Text(
                                "${WearFormat.shortDate(item.date)} · ${if (item.kind == "income") "+" else ""}${item.amount.text}",
                                color = if (item.overdue) Red else Muted,
                                maxLines = 1,
                            )
                        },
                        colors = ChipDefaults.secondaryChipColors(),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            }
            item {
                Chip(
                    onClick = onRefresh,
                    label = { Text(if (loading) "Atualizando…" else "Atualizar") },
                    colors = ChipDefaults.childChipColors(),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        }
    }

    val pending = confirm
    Dialog(showDialog = pending != null, onDismissRequest = { confirm = null }) {
        if (pending != null) {
            Alert(
                title = { Text(if (pending.kind == "income") "Marcar como recebida?" else "Marcar como paga?", textAlign = TextAlign.Center) },
                negativeButton = { Button(onClick = { confirm = null }, colors = ButtonDefaults.secondaryButtonColors()) { Text("Não") } },
                positiveButton = {
                    Button(onClick = {
                        val id = pending.entryId
                        confirm = null
                        if (id != null) scope.launch {
                            link.settle(id)
                            onSettled()
                        }
                    }) { Text("Sim") }
                },
            ) {
                Text("${pending.description}\n${pending.amount.text}", textAlign = TextAlign.Center, color = Muted)
            }
        }
    }
}

@Composable
private fun KindStep(onPick: (String) -> Unit) {
    Scaffold(timeText = { TimeText() }) {
        Column(Modifier.fillMaxSize().padding(horizontal = 16.dp), verticalArrangement = Arrangement.Center, horizontalAlignment = Alignment.CenterHorizontally) {
            Text("O que lançar?", fontSize = 15.sp, color = Muted)
            Chip(
                onClick = { onPick("expense") },
                label = { Text("Saída", fontSize = 18.sp, fontWeight = FontWeight.Bold) },
                icon = { Text("−", fontSize = 22.sp, color = Red) },
                colors = ChipDefaults.secondaryChipColors(),
                modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
            )
            Chip(
                onClick = { onPick("income") },
                label = { Text("Entrada", fontSize = 18.sp, fontWeight = FontWeight.Bold) },
                icon = { Text("+", fontSize = 22.sp, color = Green) },
                colors = ChipDefaults.secondaryChipColors(),
                modifier = Modifier.fillMaxWidth().padding(top = 6.dp),
            )
        }
    }
}

@Composable
private fun AmountStep(kind: String, onDone: (String) -> Unit) {
    var text by remember { mutableStateOf("") }
    val keys = listOf("123", "456", "789", ",0<")
    Column(Modifier.fillMaxSize().padding(top = 22.dp, bottom = 8.dp), horizontalAlignment = Alignment.CenterHorizontally) {
        Text(
            AmountInput.preview(text),
            fontSize = 20.sp,
            fontWeight = FontWeight.Bold,
            color = if (kind == "income") Green else Red,
            maxLines = 1,
        )
        keys.forEach { row ->
            Row(horizontalArrangement = Arrangement.spacedBy(4.dp), modifier = Modifier.padding(top = 3.dp)) {
                row.forEach { key ->
                    Button(
                        onClick = { text = AmountInput.press(text, key) },
                        colors = ButtonDefaults.secondaryButtonColors(),
                        modifier = Modifier.size(width = 46.dp, height = 30.dp),
                    ) { Text(if (key == '<') "⌫" else key.toString(), fontSize = 16.sp) }
                }
            }
        }
        Button(
            onClick = { onDone(text) },
            enabled = AmountInput.isValid(text),
            modifier = Modifier.padding(top = 4.dp).size(width = 90.dp, height = 30.dp),
        ) { Text("OK", fontWeight = FontWeight.Bold) }
    }
}

@Composable
private fun DescribeStep(suggestions: List<String>, preview: String, onDone: (String) -> Unit) {
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        val text = result.data?.let { RemoteInput.getResultsFromIntent(it) }?.getCharSequence(INPUT_KEY)?.toString()?.trim()
        if (!text.isNullOrEmpty()) onDone(text)
    }
    Scaffold(timeText = { TimeText() }) {
        ScalingLazyColumn(Modifier.fillMaxSize(), horizontalAlignment = Alignment.CenterHorizontally) {
            item { ListHeader { Text("Descrição · $preview", color = Muted) } }
            item {
                Chip(
                    onClick = {
                        val intent: Intent = RemoteInputIntentHelper.createActionRemoteInputIntent()
                        RemoteInputIntentHelper.putRemoteInputsExtra(intent, listOf(RemoteInput.Builder(INPUT_KEY).setLabel("Descrição").build()))
                        launcher.launch(intent)
                    },
                    label = { Text("Falar ou digitar", fontWeight = FontWeight.Bold) },
                    colors = ChipDefaults.primaryChipColors(),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
            items(suggestions) { s ->
                Chip(onClick = { onDone(s) }, label = { Text(s, maxLines = 1, overflow = TextOverflow.Ellipsis) }, colors = ChipDefaults.secondaryChipColors(), modifier = Modifier.fillMaxWidth())
            }
        }
    }
}

private const val INPUT_KEY = "descricao"

@Composable
private fun Progress(label: String) {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            CircularProgressIndicator()
            Text(label, color = Muted, modifier = Modifier.padding(top = 8.dp))
        }
    }
}

@Composable
private fun Result(title: String, detail: String, ok: Boolean) {
    Box(Modifier.fillMaxSize().padding(16.dp), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text(if (ok) "✓" else "!", fontSize = 34.sp, fontWeight = FontWeight.Bold, color = if (ok) Green else Red)
            Text(title, fontSize = 18.sp, fontWeight = FontWeight.Bold)
            Text(detail, fontSize = 13.sp, color = Muted, textAlign = TextAlign.Center)
        }
    }
}
