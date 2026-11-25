package br.app.finmy.widget

import kotlinx.serialization.Serializable

/** Situação guardada do widget (uma por widget na tela). */
@Serializable
enum class SnapshotState { LOADING, OK, NEEDS_LOGIN, CARD_UNAVAILABLE }

/**
 * Estado de um widget: cartão escolhido, último payload e hora da última atualização bem-sucedida.
 * [stale] = a última tentativa falhou por rede/servidor; os valores mostrados são os últimos conhecidos.
 */
@Serializable
data class WidgetSnapshot(
    val cardId: String,
    val cardName: String,
    val cardColor: String,
    val state: SnapshotState = SnapshotState.LOADING,
    val data: CurrentInvoiceResponse? = null,
    val updatedAtMillis: Long? = null,
    val stale: Boolean = false,
) {
    companion object {
        fun chosen(card: CardItem) = WidgetSnapshot(cardId = card.id, cardName = card.name, cardColor = card.color)
    }
}

/** Atualiza um [WidgetSnapshot] com o resultado da API, sem nunca apagar os últimos valores conhecidos. */
class InvoiceRefresher(private val api: WidgetApi, private val clock: () -> Long = System::currentTimeMillis) {
    suspend fun refresh(previous: WidgetSnapshot): WidgetSnapshot = when (val result = api.currentInvoice(previous.cardId)) {
        is ApiResult.Ok -> previous.copy(
            cardName = result.value.card.name,
            cardColor = result.value.card.color,
            state = SnapshotState.OK,
            data = result.value,
            updatedAtMillis = clock(),
            stale = false,
        )
        ApiResult.NeedsLogin -> previous.copy(state = SnapshotState.NEEDS_LOGIN)
        ApiResult.CardUnavailable -> previous.copy(state = SnapshotState.CARD_UNAVAILABLE)
        // Sem rede (ou servidor fora): mantém tudo e só marca como desatualizado.
        ApiResult.Offline, is ApiResult.Failure -> previous.copy(stale = true)
    }
}
