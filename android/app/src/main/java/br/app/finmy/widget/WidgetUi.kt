package br.app.finmy.widget

import java.time.Instant
import java.time.ZoneId

/** O que o widget mostra, já em pt-BR. Os valores em R$ vêm formatados do servidor (os mesmos da tela da fatura, SC-002). */
sealed interface WidgetUi {
    /** Widget sem cartão escolhido (acabou de ser colocado na tela). */
    data object NotConfigured : WidgetUi

    data class Loading(val cardName: String, val offline: Boolean) : WidgetUi

    data object NeedsLogin : WidgetUi

    data object CardUnavailable : WidgetUi

    data class Content(
        val cardName: String,
        val cardColor: Int,
        val dueLabel: String,
        val statusLabel: String,
        val paid: Boolean,
        val totalText: String,
        val plannedText: String,
        val remainingText: String,
        val updatedLabel: String,
        val offline: Boolean,
        val appPath: String,
    ) : WidgetUi

    companion object {
        fun from(snapshot: WidgetSnapshot?, zone: ZoneId = ZoneId.systemDefault()): WidgetUi {
            if (snapshot == null) return NotConfigured
            return when (snapshot.state) {
                SnapshotState.NEEDS_LOGIN -> NeedsLogin
                SnapshotState.CARD_UNAVAILABLE -> CardUnavailable
                SnapshotState.LOADING -> Loading(snapshot.cardName, snapshot.stale)
                SnapshotState.OK -> {
                    val data = snapshot.data ?: return Loading(snapshot.cardName, snapshot.stale)
                    val invoice = data.invoice
                    val time = snapshot.updatedAtMillis?.let { PtBr.time(Instant.ofEpochMilli(it), zone) }
                    Content(
                        cardName = snapshot.cardName,
                        cardColor = PtBr.parseColor(snapshot.cardColor),
                        dueLabel = "Vence ${PtBr.shortDate(invoice.dueDate)}",
                        statusLabel = PtBr.status(invoice.status),
                        paid = invoice.status == "paid",
                        totalText = invoice.totalText,
                        plannedText = invoice.plannedText,
                        remainingText = invoice.remainingText,
                        updatedLabel = if (time == null) "" else "atualizado às $time",
                        offline = snapshot.stale,
                        appPath = data.appPath,
                    )
                }
            }
        }
    }
}

/** Formatação pt-BR feita à mão: o resultado não depende do locale nem do ICU do aparelho. */
object PtBr {
    const val FALLBACK_COLOR = 0xFF5B4FF0.toInt()

    /** "2026-12-25" → "25/12". */
    fun shortDate(iso: String): String {
        val parts = iso.split('-')
        return if (parts.size == 3) "${parts[2]}/${parts[1]}" else iso
    }

    /** "14:05" (24 h). */
    fun time(instant: Instant, zone: ZoneId): String {
        val t = instant.atZone(zone)
        return "%02d:%02d".format(t.hour, t.minute)
    }

    fun status(code: String): String = when (code) {
        "open" -> "Aberta"
        "closed" -> "Fechada"
        "paid" -> "Paga"
        "overdue" -> "Atrasada"
        else -> code
    }

    /** "#3b82f6" → ARGB; cor inválida cai na cor da marca. */
    fun parseColor(hex: String): Int {
        val clean = hex.removePrefix("#")
        val value = if (clean.length == 6) clean.toLongOrNull(16) else null
        return if (value == null) FALLBACK_COLOR else (0xFF000000 or value).toInt()
    }
}
