package br.app.finmy.widget

import android.content.Context
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.glance.GlanceId
import androidx.glance.appwidget.GlanceAppWidget
import androidx.glance.appwidget.GlanceAppWidgetManager
import androidx.glance.appwidget.state.updateAppWidgetState
import androidx.glance.state.PreferencesGlanceStateDefinition
import kotlinx.serialization.SerializationException
import kotlinx.serialization.Serializable

/** Valor em centavos com o texto pt-BR vindo do servidor. */
@Serializable
data class Money(val cents: Long, val text: String)

@Serializable
data class OverviewPending(val count: Int, val toReceive: Money, val toPay: Money)

@Serializable
data class OverviewUpcoming(val kind: String, val description: String, val date: String, val amount: Money, val overdue: Boolean)

@Serializable
data class OverviewSettlement(val tagId: String, val name: String, val color: String, val amount: Money)

/** Contrato `GET /api/v1/widget/overview` (010). */
@Serializable
data class OverviewResponse(
    val today: String,
    val month: String,
    val plannedIncome: Money,
    val plannedExpense: Money,
    val plannedFinal: Money,
    val actualIncome: Money,
    val actualExpense: Money,
    val actualFinal: Money,
    val totalBalance: Money,
    val pending: OverviewPending,
    val upcoming: List<OverviewUpcoming> = emptyList(),
    val settlements: List<OverviewSettlement> = emptyList(),
    val monthPath: String,
    val settlementsPath: String,
)

/** O que os widgets de resumo guardam: o último resumo e o estado da busca. */
@Serializable
data class OverviewSnapshot(
    val state: SnapshotState = SnapshotState.LOADING,
    val data: OverviewResponse? = null,
    val updatedAtMillis: Long? = null,
    val stale: Boolean = false,
) {
    /** Resultado de uma nova busca aplicado sobre o anterior (sem rede mantém os últimos valores). */
    fun next(result: ApiResult<OverviewResponse>, now: Long): OverviewSnapshot = when (result) {
        is ApiResult.Ok -> OverviewSnapshot(SnapshotState.OK, result.value, now, stale = false)
        ApiResult.NeedsLogin -> OverviewSnapshot(SnapshotState.NEEDS_LOGIN, data, updatedAtMillis)
        else -> if (data != null) copy(state = SnapshotState.OK, stale = true) else copy(state = SnapshotState.LOADING, stale = true)
    }
}

/** Estado dos widgets de resumo: o mesmo resumo é buscado uma vez e gravado em cada widget na tela. */
object OverviewState {
    private val KEY = stringPreferencesKey("overview")
    internal val key get() = KEY

    /** Os widgets que mostram o resumo. */
    fun widgets(): List<GlanceAppWidget> = listOf(MesWidget(), VencimentosWidget(), AcertosWidget())

    fun decode(json: String?): OverviewSnapshot? = json?.let {
        try {
            WidgetJson.decodeFromString(OverviewSnapshot.serializer(), it)
        } catch (_: SerializationException) {
            null
        }
    }

    private suspend fun ids(context: Context): List<Pair<GlanceAppWidget, GlanceId>> {
        val manager = GlanceAppWidgetManager(context)
        return widgets().flatMap { w -> manager.getGlanceIds(w.javaClass).map { w to it } }
    }

    suspend fun refreshAll(context: Context, clock: () -> Long = System::currentTimeMillis) {
        val targets = ids(context)
        if (targets.isEmpty()) return
        val result = WidgetServices.get(context).api.overview()
        targets.forEach { (widget, id) ->
            updateAppWidgetState(context, PreferencesGlanceStateDefinition, id) { prefs ->
                val previous = decode(prefs[KEY]) ?: OverviewSnapshot()
                prefs.toMutablePreferences().apply {
                    this[KEY] = WidgetJson.encodeToString(OverviewSnapshot.serializer(), previous.next(result, clock()))
                }
            }
            widget.update(context, id)
        }
    }
}
