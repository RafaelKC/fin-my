package br.app.finmy.widget

import kotlinx.serialization.Serializable

/** Contrato `GET /api/v1/widget/credit-cards` (specs/009 contracts/api.md). */
@Serializable
data class CardsResponse(val today: String, val cards: List<CardItem>)

@Serializable
data class CardItem(
    val id: String,
    val name: String,
    val color: String,
    val closingDay: Int = 0,
    val dueDay: Int = 0,
)

/** Contrato `GET /api/v1/widget/credit-cards/{id}/current-invoice`. */
@Serializable
data class CurrentInvoiceResponse(
    val today: String,
    val card: CardRef,
    val invoice: InvoiceData,
    val appPath: String,
)

@Serializable
data class CardRef(val id: String, val name: String, val color: String)

@Serializable
data class InvoiceData(
    val dueMonth: String,
    val closingDate: String,
    val dueDate: String,
    val status: String,
    val totalCents: Long,
    val totalText: String,
    val plannedCents: Long,
    val plannedText: String,
    val estimateCents: Long? = null,
    val paidCents: Long = 0,
    val remainingCents: Long,
    val remainingText: String,
)

/** Resposta do endpoint de token do OAuth (008), só os campos usados. */
@Serializable
data class TokenResponse(
    val access_token: String,
    val refresh_token: String? = null,
    val expires_in: Long = 3600,
)

/** Tokens guardados (cifrados) no aparelho. `expiresAtMillis` é o instante em que o access token deixa de valer. */
@Serializable
data class Tokens(val accessToken: String, val refreshToken: String?, val expiresAtMillis: Long)
